using DiscordSteward.Operations;
using DiscordSteward.Snapshots;

namespace DiscordSteward.Tests;

public sealed class FileBackedOperationJournalTests
{
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse(
        "2026-07-31T12:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task ReserveExecuteAndReopen_PreservesOperationEvidence()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operations.json");
        var operation = CreateOperation();
        using (var journal = new FileBackedOperationJournal(path))
        {
            var reservation = await journal.ReserveAsync(operation, CancellationToken.None);
            Assert.Equal(OperationReservationDisposition.Reserved, reservation.Disposition);
            Assert.True(await journal.BeginExecutionAsync(
                operation.RequestId,
                operation.InvocationDigest,
                "before",
                "desired",
                CreatedAt.AddSeconds(1),
                CancellationToken.None));
            Assert.True(await journal.CompleteExecutionAsync(
                operation.RequestId,
                OperationStatuses.Succeeded,
                "verified_desired_state",
                errorCode: null,
                observedStateJson: "observed",
                CreatedAt.AddSeconds(2),
                CancellationToken.None));
        }

        using var reopened = new FileBackedOperationJournal(path);
        var stored = await reopened.GetAsync(operation.RequestId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(OperationStatuses.Succeeded, stored.Status);
        Assert.Equal("before", stored.BeforeStateJson);
        Assert.Equal("desired", stored.DesiredStateJson);
        Assert.Equal("observed", stored.ObservedStateJson);
        Assert.Equal("{\"runId\":\"run-1\"}", stored.RequestMetadataJson);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentReservations_AllowOneWinnerAndPersistTheRecord()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operations.json");
        var operation = CreateOperation();
        using var journal = new FileBackedOperationJournal(path);

        var reservations = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            journal.ReserveAsync(operation, CancellationToken.None)));

        Assert.Equal(1, reservations.Count(result => result.Disposition == OperationReservationDisposition.Reserved));
        Assert.Equal(7, reservations.Count(result => result.Disposition == OperationReservationDisposition.Existing));
        Assert.Single(await journal.ListAsync(20, CancellationToken.None));
    }

    [Fact]
    public async Task ConfigurationSnapshot_PersistsAcrossJournalInstances()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operations.json");
        var snapshot = new StoredConfigurationSnapshot(
            "01900000-0000-7000-8000-000000000333",
            "100000000000000001",
            "snapshot-digest",
            CreatedAt,
            "{\"id\":\"guild\"}");
        using (var journal = new FileBackedOperationJournal(path))
        {
            await journal.SaveSnapshotAsync(snapshot, CancellationToken.None);
        }

        using var reopened = new FileBackedOperationJournal(path);
        Assert.Equal(snapshot, await reopened.GetSnapshotAsync(snapshot.SnapshotId, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveUnknown_PersistsTerminalRecoveryAcrossJournalInstances()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "operations.json");
        var operation = CreateOperation();
        using (var journal = new FileBackedOperationJournal(path))
        {
            await journal.ReserveAsync(operation, CancellationToken.None);
            Assert.True(await journal.BeginExecutionAsync(
                operation.RequestId,
                operation.InvocationDigest,
                "before",
                "desired",
                CreatedAt.AddSeconds(1),
                CancellationToken.None));
            Assert.True(await journal.CompleteExecutionAsync(
                operation.RequestId,
                OperationStatuses.Unknown,
                "write_outcome_unresolved",
                errorCode: "outcome_unknown",
                observedStateJson: null,
                CreatedAt.AddSeconds(2),
                CancellationToken.None));
            Assert.True(await journal.ResolveUnknownAsync(
                operation.RequestId,
                OperationStatuses.Succeeded,
                "reconciled_verified_desired_state",
                errorCode: null,
                observedStateJson: "observed",
                CreatedAt.AddSeconds(3),
                CancellationToken.None));
        }

        using var reopened = new FileBackedOperationJournal(path);
        var stored = await reopened.GetAsync(operation.RequestId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(OperationStatuses.Succeeded, stored.Status);
        Assert.Equal("reconciled_verified_desired_state", stored.Outcome);
        Assert.Equal("observed", stored.ObservedStateJson);
    }

    [Theory]
    [InlineData(null, OperationJournalBackend.Sqlite)]
    [InlineData("Sqlite", OperationJournalBackend.Sqlite)]
    [InlineData("file", OperationJournalBackend.File)]
    public void JournalBackendParser_ParsesSupportedValues(string? configuredValue, OperationJournalBackend expected)
    {
        Assert.Equal(expected, OperationJournalBackendParser.Parse(configuredValue));
    }

    private static OperationRecord CreateOperation() => new(
        SchemaVersion: 1,
        RequestId: "01900000-0000-7000-8000-000000000001",
        Kind: OperationKinds.ChannelUpdate,
        GuildId: "100000000000000001",
        ResourceId: "20",
        Reason: "Set the channel topic",
        Risk: "R3",
        PolicyDigest: "policy",
        ManifestDigest: "manifest",
        InvocationDigest: "invocation",
        InvocationJson: "{}",
        CreatedAt: CreatedAt,
        Status: OperationStatuses.Reserved,
        ExecutionStartedAt: null,
        CompletedAt: null,
        Outcome: null,
        ErrorCode: null,
        BeforeStateJson: null,
        DesiredStateJson: null,
        ObservedStateJson: null,
        RequestMetadataJson: "{\"runId\":\"run-1\"}");

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"discord-steward-file-journal-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}