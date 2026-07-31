using DiscordSteward.Operations;
using DiscordSteward.Snapshots;
using Microsoft.Data.Sqlite;

namespace DiscordSteward.Tests;

public sealed class SqliteOperationJournalTests
{
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse(
        "2026-07-25T12:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task ReserveAsync_DistinguishesFirstReservationReplayAndCollision()
    {
        using var directory = new TemporaryDirectory();
        using var journal = new SqliteOperationJournal(Path.Combine(directory.Path, "operations.db"));
        var operation = CreateOperation();

        var first = await journal.ReserveAsync(operation, CancellationToken.None);
        var replay = await journal.ReserveAsync(operation, CancellationToken.None);
        var collision = await journal.ReserveAsync(
            operation with { InvocationDigest = "different" },
            CancellationToken.None);

        Assert.Equal(OperationReservationDisposition.Reserved, first.Disposition);
        Assert.Equal(OperationReservationDisposition.Existing, replay.Disposition);
        Assert.Equal(OperationReservationDisposition.Conflict, collision.Disposition);
        Assert.Equal(operation.InvocationDigest, collision.Record.InvocationDigest);
    }

    [Fact]
    public async Task ReserveAndBeginExecution_AllowOneConcurrentWinner()
    {
        using var directory = new TemporaryDirectory();
        using var journal = new SqliteOperationJournal(Path.Combine(directory.Path, "operations.db"));
        var operation = CreateOperation();

        var reservations = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            journal.ReserveAsync(operation, CancellationToken.None)));
        Assert.Equal(1, reservations.Count(result =>
            result.Disposition == OperationReservationDisposition.Reserved));
        Assert.Equal(7, reservations.Count(result =>
            result.Disposition == OperationReservationDisposition.Existing));

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            journal.BeginExecutionAsync(
                operation.RequestId,
                operation.InvocationDigest,
                "before",
                "desired",
                CreatedAt.AddSeconds(1),
                CancellationToken.None)));
        Assert.Equal(1, claims.Count(claim => claim));
        Assert.Equal(7, claims.Count(claim => !claim));
    }

    [Fact]
    public async Task CompleteExecution_PersistsEvidenceAcrossStoreInstances()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "operations.db");
        var operation = CreateOperation();
        using (var journal = new SqliteOperationJournal(databasePath))
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
                OperationStatuses.Succeeded,
                "verified_desired_state",
                errorCode: null,
                observedStateJson: "observed",
                CreatedAt.AddSeconds(2),
                CancellationToken.None));
        }

        using var reopened = new SqliteOperationJournal(databasePath);
        var stored = await reopened.GetAsync(operation.RequestId, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(OperationStatuses.Succeeded, stored.Status);
        Assert.Equal("before", stored.BeforeStateJson);
        Assert.Equal("desired", stored.DesiredStateJson);
        Assert.Equal("observed", stored.ObservedStateJson);
        Assert.Equal("verified_desired_state", stored.Outcome);
        Assert.Equal("{\"runId\":\"run-1\"}", stored.RequestMetadataJson);
    }

    [Fact]
    public async Task SchemaV2Migration_PreservesLegacyRowsAndAddsOperationJournal()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "operations.db");
        using (var providerInitializer = new SqliteOperationJournal(databasePath))
        {
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE change_plans (
                    id TEXT PRIMARY KEY,
                    schema_version INTEGER NOT NULL,
                    guild_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    intent TEXT NOT NULL,
                    policy_digest TEXT NOT NULL,
                    resource_id TEXT NOT NULL,
                    plan_digest TEXT NOT NULL,
                    plan_json TEXT NOT NULL,
                    status TEXT NOT NULL,
                    execution_started_at TEXT NULL,
                    completed_at TEXT NULL,
                    outcome TEXT NULL,
                    error_code TEXT NULL,
                    observed_state_json TEXT NULL,
                    approval_mode TEXT NOT NULL
                ) STRICT;
                INSERT INTO change_plans (
                    id, schema_version, guild_id, created_at, intent, policy_digest,
                    resource_id, plan_digest, plan_json, status, approval_mode)
                VALUES (
                    '01900000000070008000000000000000', 1, '667956000757776386',
                    '2026-07-25T11:00:00.0000000+00:00', 'Legacy intent',
                    'legacy-policy', '20', 'legacy-digest', '{}', 'succeeded',
                    'externalApproval');
                PRAGMA user_version = 2;
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        using var journal = new SqliteOperationJournal(databasePath);
        var reservation = await journal.ReserveAsync(CreateOperation(), CancellationToken.None);
        Assert.Equal(OperationReservationDisposition.Reserved, reservation.Disposition);
        var legacy = await journal.GetAsync(
            "01900000-0000-7000-8000-000000000000",
            CancellationToken.None);
        Assert.NotNull(legacy);
        Assert.Equal("legacy_change_plan", legacy.Kind);
        Assert.Equal("legacy-plan-approval", legacy.ManifestDigest);
        Assert.Equal("Legacy intent", legacy.Reason);

        await using var verification = new SqliteConnection($"Data Source={databasePath}");
        await verification.OpenAsync(CancellationToken.None);
        await using var verifyCommand = verification.CreateCommand();
        verifyCommand.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM change_plans),
                (SELECT COUNT(*) FROM operation_journal),
                (SELECT user_version FROM pragma_user_version);
            """;
        await using var reader = await verifyCommand.ExecuteReaderAsync(CancellationToken.None);
        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(4, reader.GetInt32(2));
    }

    [Fact]
    public async Task ConfigurationSnapshot_PersistsAcrossStoreInstances()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "operations.db");
        var snapshot = new StoredConfigurationSnapshot(
            "01900000-0000-7000-8000-000000000333",
            "667956000757776386",
            "snapshot-digest",
            CreatedAt,
            "{\"id\":\"guild\"}");
        using (var journal = new SqliteOperationJournal(databasePath))
        {
            await journal.SaveSnapshotAsync(snapshot, CancellationToken.None);
        }

        using var reopened = new SqliteOperationJournal(databasePath);
        var stored = await reopened.GetSnapshotAsync(snapshot.SnapshotId, CancellationToken.None);

        Assert.Equal(snapshot, stored);
    }

    private static OperationRecord CreateOperation() => new(
        SchemaVersion: 1,
        RequestId: "01900000-0000-7000-8000-000000000001",
        Kind: OperationKinds.ChannelUpdate,
        GuildId: "667956000757776386",
        ResourceId: "20",
        Reason: "Set the channel topic",
        Risk: "R3",
        PolicyDigest: "policy",
        ManifestDigest: "manifest",
        InvocationDigest: "invocation",
        InvocationJson: "{}",
        CreatedAt,
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
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"discord-steward-operation-tests-{Guid.NewGuid():N}");
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
