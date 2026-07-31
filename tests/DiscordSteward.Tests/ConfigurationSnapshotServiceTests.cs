using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Snapshots;

namespace DiscordSteward.Tests;

public sealed class ConfigurationSnapshotServiceTests
{
    private const ulong GuildId = 100000000000000001UL;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-07-25T12:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task CaptureAndCompare_DetectsExactResourceChanges()
    {
        var reader = new MutableReader(CreateSnapshot(topic: null));
        var store = new InMemorySnapshotStore();
        var service = new ConfigurationSnapshotService(
            reader,
            store,
            new StewardRuntimeSettings(GuildId, DiscordTokenConfigured: true),
            new FixedTimeProvider(Now));

        var captured = await service.CaptureAsync(CancellationToken.None);
        var unchanged = await service.CompareAsync(captured.SnapshotId, CancellationToken.None);
        reader.Snapshot = CreateSnapshot(topic: "Changed");
        var changed = await service.CompareAsync(captured.SnapshotId, CancellationToken.None);

        Assert.Equal(64, captured.Digest.Length);
        Assert.False(unchanged.Changed);
        Assert.Empty(unchanged.Channels.ChangedIds);
        Assert.True(changed.Changed);
        Assert.Equal("21", Assert.Single(changed.Channels.ChangedIds));
        Assert.Empty(changed.Roles.ChangedIds);
        Assert.Empty(changed.Categories.ChangedIds);
    }

    private static GuildSnapshot CreateSnapshot(string? topic) => GuildSnapshot.Create(
        GuildId,
        "Guild",
        ownerId: 1,
        description: null,
        DateTimeOffset.UnixEpoch,
        [],
        [RoleSnapshot.Create(
            GuildId,
            GuildId,
            "@everyone",
            0,
            isManaged: false,
            isEveryone: true,
            ResourceProtection.None,
            ["ViewChannel"],
            colorHex: null,
            isHoisted: false,
            isMentionable: false)],
        [CategorySnapshot.Create(GuildId, 20, "Text", 0, [])],
        [ChannelSnapshot.Create(GuildId, 21, "general", "text", 0, 20, topic, false, 0, [])]);

    private sealed class MutableReader(GuildSnapshot snapshot) : IDiscordGuildReader
    {
        public GuildSnapshot Snapshot { get; set; } = snapshot;

        public Task<GuildSnapshot> GetGuildSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class InMemorySnapshotStore : IConfigurationSnapshotStore
    {
        private readonly Dictionary<string, StoredConfigurationSnapshot> _snapshots = new(StringComparer.Ordinal);

        public Task SaveSnapshotAsync(
            StoredConfigurationSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshots.Add(snapshot.SnapshotId, snapshot);
            return Task.CompletedTask;
        }

        public Task<StoredConfigurationSnapshot?> GetSnapshotAsync(
            string snapshotId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshots.GetValueOrDefault(snapshotId));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
