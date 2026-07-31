using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

internal sealed class InMemoryOperationJournal : IOperationJournal
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OperationRecord> _operations = new(StringComparer.Ordinal);

    public Exception? ReserveException { get; set; }

    public Exception? BeginExecutionException { get; set; }

    public Exception? CompleteExecutionException { get; set; }

    public Task<OperationReservation> ReserveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReserveException is not null)
        {
            throw ReserveException;
        }

        lock (_gate)
        {
            if (_operations.TryGetValue(operation.RequestId, out var existing))
            {
                var disposition = existing.InvocationDigest == operation.InvocationDigest
                    ? OperationReservationDisposition.Existing
                    : OperationReservationDisposition.Conflict;
                return Task.FromResult(new OperationReservation(disposition, existing));
            }

            _operations.Add(operation.RequestId, operation);
            return Task.FromResult(new OperationReservation(
                OperationReservationDisposition.Reserved,
                operation));
        }
    }

    public Task<OperationRecord?> GetAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_operations.GetValueOrDefault(requestId));
        }
    }

    public Task<IReadOnlyList<OperationRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<OperationRecord>>(
                _operations.Values
                    .OrderByDescending(operation => operation.CreatedAt)
                    .ThenByDescending(operation => operation.RequestId, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray());
        }
    }

    public Task<bool> FailPreflightAsync(
        string requestId,
        string invocationDigest,
        string outcome,
        string errorCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_operations.TryGetValue(requestId, out var operation) ||
                operation.InvocationDigest != invocationDigest ||
                operation.Status != OperationStatuses.Reserved)
            {
                return Task.FromResult(false);
            }

            _operations[requestId] = operation with
            {
                Status = OperationStatuses.Failed,
                CompletedAt = completedAt,
                Outcome = outcome,
                ErrorCode = errorCode
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> BeginExecutionAsync(
        string requestId,
        string invocationDigest,
        string beforeStateJson,
        string desiredStateJson,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BeginExecutionException is not null)
        {
            throw BeginExecutionException;
        }

        lock (_gate)
        {
            if (!_operations.TryGetValue(requestId, out var operation) ||
                operation.InvocationDigest != invocationDigest ||
                operation.Status != OperationStatuses.Reserved)
            {
                return Task.FromResult(false);
            }

            _operations[requestId] = operation with
            {
                Status = OperationStatuses.Executing,
                ExecutionStartedAt = startedAt,
                BeforeStateJson = beforeStateJson,
                DesiredStateJson = desiredStateJson
            };
            return Task.FromResult(true);
        }
    }

    public Task<bool> CompleteExecutionAsync(
        string requestId,
        string status,
        string outcome,
        string? errorCode,
        string? observedStateJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CompleteExecutionException is not null)
        {
            throw CompleteExecutionException;
        }

        lock (_gate)
        {
            if (!_operations.TryGetValue(requestId, out var operation) ||
                operation.Status != OperationStatuses.Executing)
            {
                return Task.FromResult(false);
            }

            _operations[requestId] = operation with
            {
                Status = status,
                CompletedAt = completedAt,
                Outcome = outcome,
                ErrorCode = errorCode,
                ObservedStateJson = observedStateJson
            };
            return Task.FromResult(true);
        }
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
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_operations.TryGetValue(requestId, out var operation) ||
                operation.Status != OperationStatuses.Unknown)
            {
                return Task.FromResult(false);
            }

            _operations[requestId] = operation with
            {
                Status = status,
                CompletedAt = completedAt,
                Outcome = outcome,
                ErrorCode = errorCode,
                ObservedStateJson = observedStateJson
            };
            return Task.FromResult(true);
        }
    }
}
