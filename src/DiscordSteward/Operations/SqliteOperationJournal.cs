using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using DiscordSteward.Snapshots;
using Microsoft.Data.Sqlite;

namespace DiscordSteward.Operations;

public sealed class SqliteOperationJournal : IOperationJournal, IConfigurationSnapshotStore, IDisposable
{
    private const int SchemaVersion = 4;
    private static readonly object SqliteInitializationLock = new();
    private static int _sqliteInitialized;
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;

    public SqliteOperationJournal(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        EnsureSqliteProvider();

        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true
        }.ToString();
    }

    public async Task<OperationReservation> ReserveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Status != OperationStatuses.Reserved)
        {
            throw new ArgumentException("New operations must be reserved before execution.", nameof(operation));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        int inserted;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO operation_journal (
                    request_id, schema_version, operation_kind, guild_id, resource_id,
                    reason, risk, policy_digest, manifest_digest, invocation_digest,
                    invocation_json, request_metadata_json, created_at, status)
                VALUES (
                    $requestId, $schemaVersion, $operationKind, $guildId, $resourceId,
                    $reason, $risk, $policyDigest, $manifestDigest, $invocationDigest,
                    $invocationJson, $requestMetadataJson, $createdAt, $status)
                ON CONFLICT(request_id) DO NOTHING;
                """;
            AddOperationParameters(command, operation);
            inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var stored = await GetCoreAsync(connection, operation.RequestId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The operation reservation could not be read back.");
        var disposition = string.Equals(
                stored.InvocationDigest,
                operation.InvocationDigest,
                StringComparison.Ordinal)
                ? inserted == 1
                ? OperationReservationDisposition.Reserved
                : OperationReservationDisposition.Existing
            : OperationReservationDisposition.Conflict;
        return new OperationReservation(disposition, stored);
    }

    public async Task<OperationRecord?> GetAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await GetCoreAsync(connection, requestId, cancellationToken).ConfigureAwait(false)
            ?? await GetLegacyCoreAsync(connection, requestId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OperationRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectColumns}
            ORDER BY created_at DESC, request_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var operations = new List<OperationRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            operations.Add(ReadOperation(reader));
        }

        await reader.DisposeAsync().ConfigureAwait(false);
        if (await LegacyTableExistsAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            await using var legacyCommand = connection.CreateCommand();
            legacyCommand.CommandText = $"""
                {LegacySelectColumns}
                ORDER BY created_at DESC, id DESC
                LIMIT $limit;
                """;
            legacyCommand.Parameters.AddWithValue("$limit", limit);
            await using var legacyReader = await legacyCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await legacyReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                operations.Add(ReadLegacyOperation(legacyReader));
            }
        }

        return operations
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenByDescending(operation => operation.RequestId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async Task<bool> FailPreflightAsync(
        string requestId,
        string invocationDigest,
        string outcome,
        string errorCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operation_journal
            SET status = $failed,
                completed_at = $completedAt,
                outcome = $outcome,
                error_code = $errorCode
            WHERE request_id = $requestId
              AND invocation_digest = $invocationDigest
              AND status = $reserved;
            """;
        command.Parameters.AddWithValue("$failed", OperationStatuses.Failed);
        command.Parameters.AddWithValue("$completedAt", FormatTimestamp(completedAt));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$errorCode", errorCode);
        command.Parameters.AddWithValue("$requestId", requestId);
        command.Parameters.AddWithValue("$invocationDigest", invocationDigest);
        command.Parameters.AddWithValue("$reserved", OperationStatuses.Reserved);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> BeginExecutionAsync(
        string requestId,
        string invocationDigest,
        string beforeStateJson,
        string desiredStateJson,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operation_journal
            SET status = $executing,
                execution_started_at = $startedAt,
                before_state_json = $beforeStateJson,
                desired_state_json = $desiredStateJson
            WHERE request_id = $requestId
              AND invocation_digest = $invocationDigest
              AND status = $reserved;
            """;
        command.Parameters.AddWithValue("$executing", OperationStatuses.Executing);
        command.Parameters.AddWithValue("$startedAt", FormatTimestamp(startedAt));
        command.Parameters.AddWithValue("$beforeStateJson", beforeStateJson);
        command.Parameters.AddWithValue("$desiredStateJson", desiredStateJson);
        command.Parameters.AddWithValue("$requestId", requestId);
        command.Parameters.AddWithValue("$invocationDigest", invocationDigest);
        command.Parameters.AddWithValue("$reserved", OperationStatuses.Reserved);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> CompleteExecutionAsync(
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

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operation_journal
            SET status = $status,
                completed_at = $completedAt,
                outcome = $outcome,
                error_code = $errorCode,
                observed_state_json = $observedStateJson
            WHERE request_id = $requestId
              AND status = $executing;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$completedAt", FormatTimestamp(completedAt));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$errorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$observedStateJson", (object?)observedStateJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$requestId", requestId);
        command.Parameters.AddWithValue("$executing", OperationStatuses.Executing);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> ResolveUnknownAsync(
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

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE operation_journal
            SET status = $status,
                completed_at = $completedAt,
                outcome = $outcome,
                error_code = $errorCode,
                observed_state_json = $observedStateJson
            WHERE request_id = $requestId
              AND status = $unknown;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$completedAt", FormatTimestamp(completedAt));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$errorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$observedStateJson", observedStateJson);
        command.Parameters.AddWithValue("$requestId", requestId);
        command.Parameters.AddWithValue("$unknown", OperationStatuses.Unknown);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task SaveSnapshotAsync(
        StoredConfigurationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO configuration_snapshots (
                snapshot_id, guild_id, digest, captured_at, snapshot_json)
            VALUES (
                $snapshotId, $guildId, $digest, $capturedAt, $snapshotJson);
            """;
        command.Parameters.AddWithValue("$snapshotId", snapshot.SnapshotId);
        command.Parameters.AddWithValue("$guildId", snapshot.GuildId);
        command.Parameters.AddWithValue("$digest", snapshot.Digest);
        command.Parameters.AddWithValue("$capturedAt", FormatTimestamp(snapshot.CapturedAt));
        command.Parameters.AddWithValue("$snapshotJson", snapshot.SnapshotJson);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredConfigurationSnapshot?> GetSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT snapshot_id, guild_id, digest, captured_at, snapshot_json
            FROM configuration_snapshots
            WHERE snapshot_id = $snapshotId;
            """;
        command.Parameters.AddWithValue("$snapshotId", snapshotId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new StoredConfigurationSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                ReadTimestamp(reader, 3)!.Value,
                reader.GetString(4))
            : null;
    }

    public void Dispose()
    {
        _initializationGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string SelectColumns => """
        SELECT request_id, schema_version, operation_kind, guild_id, resource_id,
               reason, risk, policy_digest, manifest_digest, invocation_digest,
               invocation_json, created_at, status, execution_started_at,
               completed_at, outcome, error_code, before_state_json,
             desired_state_json, observed_state_json, request_metadata_json
        FROM operation_journal
        """;

    private static string LegacySelectColumns => """
        SELECT id, schema_version, guild_id, resource_id, intent, policy_digest,
               plan_digest, plan_json, created_at, status, execution_started_at,
               completed_at, outcome, error_code, observed_state_json
        FROM change_plans
        """;

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath)
                ?? throw new InvalidOperationException("The journal database path has no directory.");
            Directory.CreateDirectory(directory);
            RestrictDirectory(directory);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            RestrictDatabaseFile(_databasePath);
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version;";
            var versionValue = await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var version = Convert.ToInt32(versionValue, CultureInfo.InvariantCulture);
            if (version == 0)
            {
                await CreateSchemaV4Async(connection, cancellationToken).ConfigureAwait(false);
            }
            else if (version == 1)
            {
                await MigrateSchemaV1ToV2Async(connection, cancellationToken).ConfigureAwait(false);
                await MigrateSchemaV2ToV3Async(connection, cancellationToken).ConfigureAwait(false);
                await MigrateSchemaV3ToV4Async(connection, cancellationToken).ConfigureAwait(false);
            }
            else if (version == 2)
            {
                await MigrateSchemaV2ToV3Async(connection, cancellationToken).ConfigureAwait(false);
                await MigrateSchemaV3ToV4Async(connection, cancellationToken).ConfigureAwait(false);
            }
            else if (version == 3)
            {
                await MigrateSchemaV3ToV4Async(connection, cancellationToken).ConfigureAwait(false);
            }
            else if (version != SchemaVersion)
            {
                throw new InvalidOperationException($"Unsupported journal database schema version {version}.");
            }
            else
            {
                await EnsureSchemaV4Async(connection, cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task<OperationRecord?> GetCoreAsync(
        SqliteConnection connection,
        string requestId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectColumns}
            WHERE request_id = $requestId;
            """;
        command.Parameters.AddWithValue("$requestId", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadOperation(reader)
            : null;
    }

    private static async Task<OperationRecord?> GetLegacyCoreAsync(
        SqliteConnection connection,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!await LegacyTableExistsAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var legacyId = Guid.TryParse(requestId, out var parsed)
            ? parsed.ToString("N", CultureInfo.InvariantCulture)
            : requestId;
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {LegacySelectColumns}
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", legacyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadLegacyOperation(reader)
            : null;
    }

    private static async Task<bool> LegacyTableExistsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table' AND name = 'change_plans');
            """;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) == 1;
    }

    private static OperationRecord ReadOperation(SqliteDataReader reader) => new(
        reader.GetInt32(1),
        reader.GetString(0),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        ReadTimestamp(reader, 11)!.Value,
        reader.GetString(12),
        ReadTimestamp(reader, 13),
        ReadTimestamp(reader, 14),
        ReadNullableString(reader, 15),
        ReadNullableString(reader, 16),
        ReadNullableString(reader, 17),
        ReadNullableString(reader, 18),
        ReadNullableString(reader, 19),
        ReadNullableString(reader, 20));

    private static OperationRecord ReadLegacyOperation(SqliteDataReader reader) => new(
        SchemaVersion: reader.GetInt32(1),
        RequestId: reader.GetString(0),
        Kind: "legacy_change_plan",
        GuildId: reader.GetString(2),
        ResourceId: reader.GetString(3),
        Reason: reader.GetString(4),
        Risk: "legacy",
        PolicyDigest: reader.GetString(5),
        ManifestDigest: "legacy-plan-approval",
        InvocationDigest: reader.GetString(6),
        InvocationJson: reader.GetString(7),
        CreatedAt: ReadTimestamp(reader, 8)!.Value,
        Status: reader.GetString(9),
        ExecutionStartedAt: ReadTimestamp(reader, 10),
        CompletedAt: ReadTimestamp(reader, 11),
        Outcome: ReadNullableString(reader, 12),
        ErrorCode: ReadNullableString(reader, 13),
        BeforeStateJson: null,
        DesiredStateJson: null,
        ObservedStateJson: ReadNullableString(reader, 14),
        RequestMetadataJson: null);

    private static void AddOperationParameters(SqliteCommand command, OperationRecord operation)
    {
        command.Parameters.AddWithValue("$requestId", operation.RequestId);
        command.Parameters.AddWithValue("$schemaVersion", operation.SchemaVersion);
        command.Parameters.AddWithValue("$operationKind", operation.Kind);
        command.Parameters.AddWithValue("$guildId", operation.GuildId);
        command.Parameters.AddWithValue("$resourceId", operation.ResourceId);
        command.Parameters.AddWithValue("$reason", operation.Reason);
        command.Parameters.AddWithValue("$risk", operation.Risk);
        command.Parameters.AddWithValue("$policyDigest", operation.PolicyDigest);
        command.Parameters.AddWithValue("$manifestDigest", operation.ManifestDigest);
        command.Parameters.AddWithValue("$invocationDigest", operation.InvocationDigest);
        command.Parameters.AddWithValue("$invocationJson", operation.InvocationJson);
        command.Parameters.AddWithValue("$requestMetadataJson", (object?)operation.RequestMetadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(operation.CreatedAt));
        command.Parameters.AddWithValue("$status", operation.Status);
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA synchronous = FULL;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateSchemaV4Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        await CreateOperationTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await CreateConfigurationSnapshotTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await using var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = "PRAGMA user_version = 4;";
        await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateSchemaV1ToV2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE change_plans
            ADD COLUMN approval_mode TEXT NOT NULL DEFAULT 'externalApproval'
                CHECK (approval_mode IN ('externalApproval', 'trustedOperator'));
            PRAGMA user_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateSchemaV3ToV4Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pragma_table_info('operation_journal')
                WHERE name = 'request_metadata_json');
            """;
        var hasMetadataColumn = Convert.ToInt32(
            await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture) == 1;
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = hasMetadataColumn
            ? "PRAGMA user_version = 4;"
            : """
                ALTER TABLE operation_journal
                ADD COLUMN request_metadata_json TEXT NULL;
                PRAGMA user_version = 4;
                """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateSchemaV2ToV3Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        await CreateOperationTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await CreateConfigurationSnapshotTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await using var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = "PRAGMA user_version = 3;";
        await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateOperationTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE operation_journal (
                request_id TEXT PRIMARY KEY,
                schema_version INTEGER NOT NULL CHECK (schema_version = 1),
                operation_kind TEXT NOT NULL,
                guild_id TEXT NOT NULL,
                resource_id TEXT NOT NULL,
                reason TEXT NOT NULL,
                risk TEXT NOT NULL,
                policy_digest TEXT NOT NULL,
                manifest_digest TEXT NOT NULL,
                invocation_digest TEXT NOT NULL,
                invocation_json TEXT NOT NULL,
                request_metadata_json TEXT NULL,
                created_at TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN (
                    'reserved', 'executing', 'succeeded', 'failed',
                    'partial_failure', 'unknown')),
                execution_started_at TEXT NULL,
                completed_at TEXT NULL,
                outcome TEXT NULL,
                error_code TEXT NULL,
                before_state_json TEXT NULL,
                desired_state_json TEXT NULL,
                observed_state_json TEXT NULL
            ) STRICT;
            CREATE INDEX ix_operation_journal_created
                ON operation_journal(created_at DESC, request_id DESC);
            CREATE INDEX ix_operation_journal_resource
                ON operation_journal(resource_id, created_at DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSchemaV4Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        await CreateConfigurationSnapshotTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateConfigurationSnapshotTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS configuration_snapshots (
                snapshot_id TEXT PRIMARY KEY,
                guild_id TEXT NOT NULL,
                digest TEXT NOT NULL,
                captured_at TEXT NOT NULL,
                snapshot_json TEXT NOT NULL
            ) STRICT;
            CREATE INDEX IF NOT EXISTS ix_configuration_snapshots_captured
                ON configuration_snapshots(captured_at DESC, snapshot_id DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ReadTimestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void RestrictDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictDatabaseFile(string databasePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(databasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void EnsureSqliteProvider()
    {
        if (Volatile.Read(ref _sqliteInitialized) == 1)
        {
            return;
        }

        lock (SqliteInitializationLock)
        {
            if (_sqliteInitialized == 1)
            {
                return;
            }

            if (OperatingSystem.IsLinux())
            {
                NativeLibrary.SetDllImportResolver(
                    typeof(SQLitePCL.SQLite3Provider_sqlite3).Assembly,
                    ResolveSystemSqlite);
            }

            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
            Volatile.Write(ref _sqliteInitialized, 1);
        }
    }

    private static nint ResolveSystemSqlite(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath) =>
        string.Equals(libraryName, "sqlite3", StringComparison.Ordinal)
            ? NativeLibrary.Load("libsqlite3.so.0", assembly, searchPath)
            : nint.Zero;
}
