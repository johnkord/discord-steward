using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class MutationWorkflowIntegrationTests
{
    private const ulong GuildId = 100000000000000001UL;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-07-25T12:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task DirectUpdateVerifyReplay_PersistsAcrossJournalReopen()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"discord-steward-workflow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var databasePath = Path.Combine(directory, "operations.db");
            var policy = StewardPolicy.Create([], ["discsteward"], ["Administrator"]);
            var manifest = CreateManifest();
            var reader = new WorkflowGuildReader(CreateSnapshot());
            var writer = new WorkflowGuildWriter(reader);
            var request = new ChannelUpdateInvocation(
                "01900000-0000-7000-8000-000000000222",
                "21",
                "Set a concise welcome topic",
                ChannelStateCanonicalizer.ComputeDigest(reader.CurrentChannel),
                new ChannelUpdateSet(
                    Name: null,
                    Topic: "Welcome to Example Community",
                    SlowModeSeconds: 10),
                Clear: []);

            ChannelUpdateResult applied;
            using (var executionJournal = new SqliteOperationJournal(databasePath))
            {
                var executor = CreateExecutor(
                    reader,
                    writer,
                    executionJournal,
                    policy,
                    manifest);
                applied = await executor.ExecuteAsync(request, CancellationToken.None);
            }

            using var reopenedJournal = new SqliteOperationJournal(databasePath);
            var stored = await reopenedJournal.GetAsync(request.RequestId, CancellationToken.None);
            var replayExecutor = CreateExecutor(
                reader,
                writer,
                reopenedJournal,
                policy,
                manifest);
            var replay = await replayExecutor.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal(1, writer.UpdateCount);
            Assert.Equal(OperationStatuses.Succeeded, applied.Status);
            Assert.True(applied.Verified);
            Assert.Equal("Welcome to Example Community", applied.Observed?.Topic);
            Assert.Equal(10, applied.Observed?.SlowModeSeconds);
            Assert.NotNull(stored);
            Assert.Equal(OperationStatuses.Succeeded, stored.Status);
            Assert.Equal(applied.InvocationDigest, stored.InvocationDigest);
            Assert.Equal(applied, replay);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ChannelUpdateOperationExecutor CreateExecutor(
        IDiscordGuildReader reader,
        IDiscordGuildWriter writer,
        IOperationJournal journal,
        StewardPolicy policy,
        StewardToolManifest manifest) => new(
            reader,
            writer,
            journal,
            policy,
            manifest,
            new StewardRuntimeSettings(GuildId, DiscordTokenConfigured: true),
            new FixedTimeProvider(Now));

    private static StewardToolManifest CreateManifest() => StewardToolManifest.Create(
        [
            "get_steward_capabilities",
            "get_channel",
            "get_operation",
            "reconcile_operation",
            "update_channel"
        ],
        new StewardRiskGates(
            SensitiveReads: true,
            ReversibleWrites: true,
            PersonImpactingWrites: false,
            IrreversibleWrites: false));

    private static GuildSnapshot CreateSnapshot() => GuildSnapshot.Create(
        GuildId,
        "Guild",
        ownerId: 1,
        description: null,
        DateTimeOffset.UnixEpoch,
        [],
        [],
        [CategorySnapshot.Create(GuildId, 20, "Text", 0, [])],
        [ChannelSnapshot.Create(GuildId, 21, "general", "text", 0, 20, null, false, 0, [])]);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class WorkflowGuildReader(GuildSnapshot snapshot) : IDiscordGuildReader
    {
        private GuildSnapshot _snapshot = snapshot;

        public ChannelSnapshot CurrentChannel => _snapshot.Channels.Single();

        public Task<GuildSnapshot> GetGuildSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshot);
        }

        public void Apply(string channelId, ChannelMutableState desired)
        {
            var current = _snapshot.Channels.Single(channel => channel.Id == channelId);
            var updated = ChannelSnapshot.Create(
                GuildId,
                ulong.Parse(current.Id, System.Globalization.CultureInfo.InvariantCulture),
                desired.Name,
                current.Kind,
                current.Position,
                desired.CategoryId is null
                    ? null
                    : ulong.Parse(desired.CategoryId, System.Globalization.CultureInfo.InvariantCulture),
                desired.Topic,
                current.IsNsfw,
                desired.SlowModeSeconds,
                current.PermissionOverwrites);
            _snapshot = GuildSnapshot.Create(
                GuildId,
                _snapshot.Name,
                ulong.Parse(_snapshot.OwnerId, System.Globalization.CultureInfo.InvariantCulture),
                _snapshot.Description,
                _snapshot.CreatedAt,
                _snapshot.Features,
                _snapshot.Roles,
                _snapshot.Categories,
                _snapshot.Channels.Select(channel => channel.Id == channelId ? updated : channel));
        }
    }

    private sealed class WorkflowGuildWriter(WorkflowGuildReader reader) : IDiscordGuildWriter
    {
        public int UpdateCount { get; private set; }

        public Task<DiscordChannelWritePreflight> PreflightChannelUpdateAsync(
            string channelId,
            CancellationToken cancellationToken) => Task.FromResult(
                new DiscordChannelWritePreflight(
                    HasManageChannels: true,
                    HasAdministrator: false));

        public Task UpdateTextChannelAsync(
            string requestId,
            string channelId,
            ChannelMutableState desired,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateCount++;
            reader.Apply(channelId, desired);
            return Task.CompletedTask;
        }
    }
}
