using System.Text.Json;
using DiscordSteward.Snapshots;

namespace DiscordSteward.Operations;

public sealed class FileBackedOperationJournal : IOperationJournal, IConfigurationSnapshotStore, IDisposable
{
    private const int SnapshotVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(OperationJson.SerializerOptions)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JournalSnapshot _snapshot = new();
    private bool _loaded;

    public FileBackedOperationJournal(string snapshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        _snapshotPath = Path.GetFullPath(snapshotPath);
    }

    public Task<OperationReservation> ReserveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Status != OperationStatuses.Reserved)
        {
            throw new ArgumentException("New operations must be reserved before execution.", nameof(operation));
        }

        return MutateAsync(snapshot =>
        {
            if (snapshot.Operations.TryGetValue(operation.RequestId, out var existing))
            {
                return new OperationReservation(
                    string.Equals(existing.InvocationDigest, operation.InvocationDigest, StringComparison.Ordinal)
                        ? OperationReservationDisposition.Existing
                        : OperationReservationDisposition.Conflict,
                    existing);
            }

            snapshot.Operations.Add(operation.RequestId, operation);
            return new OperationReservation(OperationReservationDisposition.Reserved, operation);
        }, cancellationToken);
    }

    public Task<OperationRecord?> GetAsync(string requestId, CancellationToken cancellationToken) =>
        ReadAsync(snapshot => snapshot.Operations.GetValueOrDefault(requestId), cancellationToken);

    public Task<IReadOnlyList<OperationRecord>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        return ReadAsync(snapshot => (IReadOnlyList<OperationRecord>)snapshot.Operations.Values
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenByDescending(operation => operation.RequestId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray(), cancellationToken);
    }

    public Task<bool> FailPreflightAsync(
        string requestId,
        string invocationDigest,
        string outcome,
        string errorCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        UpdateOperationAsync(requestId, operation =>
        {
            if (!string.Equals(operation.InvocationDigest, invocationDigest, StringComparison.Ordinal) ||
                operation.Status != OperationStatuses.Reserved)
            {
                return null;
            }

            return operation with
            {
                Status = OperationStatuses.Failed,
                CompletedAt = completedAt,
                Outcome = outcome,
                ErrorCode = errorCode
            };
        }, cancellationToken);

    public Task<bool> BeginExecutionAsync(
        string requestId,
        string invocationDigest,
        string beforeStateJson,
        string desiredStateJson,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken) =>
        UpdateOperationAsync(requestId, operation =>
        {
            if (!string.Equals(operation.InvocationDigest, invocationDigest, StringComparison.Ordinal) ||
                operation.Status != OperationStatuses.Reserved)
            {
                return null;
            }

            return operation with
            {
                Status = OperationStatuses.Executing,
                ExecutionStartedAt = startedAt,
                BeforeStateJson = beforeStateJson,
                DesiredStateJson = desiredStateJson
            };
        }, cancellationToken);

    public Task<bool> CompleteExecutionAsync(
        string requestId,
        string status,
        string outcome,
        string? errorCode,
        string? observedStateJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        if (status is not OperationStatuses.Succeeded and
            not OperationStatuses.Failed and
            not OperationStatuses.PartialFailure and
            not OperationStatuses.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return UpdateOperationAsync(requestId, operation => operation.Status == OperationStatuses.Executing
            ? operation with
            {
                Status = status,
                CompletedAt = completedAt,
                Outcome = outcome,
                ErrorCode = errorCode,
                ObservedStateJson = observedStateJson
            }
            : null, cancellationToken);
    }

    public Task<bool> ResolveUnknownAsync(
        string requestId,
        string status,
        string outcome,
        string? errorCode,
        string observedStateJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        if (status is not OperationStatuses.Succeeded and
            not OperationStatuses.Failed and
            not OperationStatuses.PartialFailure)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return UpdateOperationAsync(requestId, operation => operation.Status == OperationStatuses.Unknown
            ? operation with
            {
                Status = status,
                CompletedAt = completedAt,
                Outcome = outcome,
                ErrorCode = errorCode,
                ObservedStateJson = observedStateJson
            }
            : null, cancellationToken);
    }

    public Task SaveSnapshotAsync(StoredConfigurationSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return MutateAsync(state =>
        {
            if (!state.ConfigurationSnapshots.TryAdd(snapshot.SnapshotId, snapshot))
            {
                throw new InvalidOperationException($"Configuration snapshot '{snapshot.SnapshotId}' already exists.");
            }

            return true;
        }, cancellationToken);
    }

    public Task<StoredConfigurationSnapshot?> GetSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken) =>
        ReadAsync(snapshot => snapshot.ConfigurationSnapshots.GetValueOrDefault(snapshotId), cancellationToken);

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<bool> UpdateOperationAsync(
        string requestId,
        Func<OperationRecord, OperationRecord?> update,
        CancellationToken cancellationToken) =>
        MutateAsync(snapshot =>
        {
            if (!snapshot.Operations.TryGetValue(requestId, out var existing))
            {
                return false;
            }

            var updated = update(existing);
            if (updated is null)
            {
                return false;
            }

            snapshot.Operations[requestId] = updated;
            return true;
        }, cancellationToken);

    private async Task<T> ReadAsync<T>(Func<JournalSnapshot, T> read, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return read(_snapshot);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> MutateAsync<T>(Func<JournalSnapshot, T> mutation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var next = _snapshot.Clone();
            var result = mutation(next);
            await PersistAsync(next, cancellationToken).ConfigureAwait(false);
            _snapshot = next;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_snapshotPath)
            ?? throw new InvalidOperationException("The journal snapshot path has no directory.");
        Directory.CreateDirectory(directory);
        if (File.Exists(_snapshotPath))
        {
            var json = await File.ReadAllTextAsync(_snapshotPath, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<JournalSnapshot>(json, JsonOptions)
                ?? throw new InvalidOperationException("The journal snapshot was empty.");
            if (loaded.Version != SnapshotVersion)
            {
                throw new InvalidOperationException($"Unsupported journal snapshot version {loaded.Version}.");
            }

            _snapshot = loaded.Normalize();
        }

        _loaded = true;
    }

    private async Task PersistAsync(JournalSnapshot snapshot, CancellationToken cancellationToken)
    {
        var tempPath = $"{_snapshotPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _snapshotPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private sealed class JournalSnapshot
    {
        public int Version { get; init; } = SnapshotVersion;

        public Dictionary<string, OperationRecord> Operations { get; init; } = new(StringComparer.Ordinal);

        public Dictionary<string, StoredConfigurationSnapshot> ConfigurationSnapshots { get; init; } = new(StringComparer.Ordinal);

        public JournalSnapshot Clone() => new()
        {
            Version = Version,
            Operations = new Dictionary<string, OperationRecord>(Operations, StringComparer.Ordinal),
            ConfigurationSnapshots = new Dictionary<string, StoredConfigurationSnapshot>(ConfigurationSnapshots, StringComparer.Ordinal)
        };

        public JournalSnapshot Normalize() => new()
        {
            Version = Version,
            Operations = Operations is null
                ? new Dictionary<string, OperationRecord>(StringComparer.Ordinal)
                : new Dictionary<string, OperationRecord>(Operations, StringComparer.Ordinal),
            ConfigurationSnapshots = ConfigurationSnapshots is null
                ? new Dictionary<string, StoredConfigurationSnapshot>(StringComparer.Ordinal)
                : new Dictionary<string, StoredConfigurationSnapshot>(ConfigurationSnapshots, StringComparer.Ordinal)
        };
    }
}