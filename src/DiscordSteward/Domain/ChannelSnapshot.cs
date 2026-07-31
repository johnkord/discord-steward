using System.Collections.Immutable;
using System.Globalization;

namespace DiscordSteward.Domain;

public sealed record CategorySnapshot
{
    private CategorySnapshot(
        string guildId,
        string id,
        string name,
        int position,
        ImmutableArray<PermissionOverwriteSnapshot> permissionOverwrites)
    {
        GuildId = guildId;
        Id = id;
        Name = name;
        Position = position;
        PermissionOverwrites = permissionOverwrites;
    }

    public string GuildId { get; }

    public string Id { get; }

    public string Name { get; }

    public int Position { get; }

    public ImmutableArray<PermissionOverwriteSnapshot> PermissionOverwrites { get; }

    public static CategorySnapshot Create(
        ulong guildId,
        ulong id,
        string name,
        int position,
        IEnumerable<PermissionOverwriteSnapshot> permissionOverwrites) => new(
            guildId.ToString(CultureInfo.InvariantCulture),
            id.ToString(CultureInfo.InvariantCulture),
            SnapshotText.Required(name, SnapshotText.NameLimit),
            position,
            NormalizeOverwrites(permissionOverwrites));

    internal static ImmutableArray<PermissionOverwriteSnapshot> NormalizeOverwrites(
        IEnumerable<PermissionOverwriteSnapshot> permissionOverwrites) =>
        permissionOverwrites
            .OrderBy(overwrite => overwrite.TargetType, StringComparer.Ordinal)
            .ThenBy(overwrite => overwrite.TargetId, StringComparer.Ordinal)
            .ToImmutableArray();
}

public sealed record ChannelSnapshot
{
    private ChannelSnapshot(
        string guildId,
        string id,
        string name,
        string kind,
        int position,
        string? categoryId,
        string? parentChannelId,
        string? topic,
        bool? isNsfw,
        int? slowModeSeconds,
        ImmutableArray<PermissionOverwriteSnapshot> permissionOverwrites)
    {
        GuildId = guildId;
        Id = id;
        Name = name;
        Kind = kind;
        Position = position;
        CategoryId = categoryId;
        ParentChannelId = parentChannelId;
        Topic = topic;
        IsNsfw = isNsfw;
        SlowModeSeconds = slowModeSeconds;
        PermissionOverwrites = permissionOverwrites;
    }

    public string GuildId { get; }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public int Position { get; }

    public string? CategoryId { get; }

    public string? ParentChannelId { get; }

    public string? Topic { get; }

    public bool? IsNsfw { get; }

    public int? SlowModeSeconds { get; }

    public ImmutableArray<PermissionOverwriteSnapshot> PermissionOverwrites { get; }

    public static ChannelSnapshot Create(
        ulong guildId,
        ulong id,
        string name,
        string kind,
        int position,
        ulong? categoryId,
        string? topic,
        bool? isNsfw,
        int? slowModeSeconds,
        IEnumerable<PermissionOverwriteSnapshot> permissionOverwrites,
        ulong? parentChannelId = null) => new(
            guildId.ToString(CultureInfo.InvariantCulture),
            id.ToString(CultureInfo.InvariantCulture),
            SnapshotText.Required(name, SnapshotText.NameLimit),
            SnapshotText.Required(kind, 64).ToLowerInvariant(),
            position,
            categoryId?.ToString(CultureInfo.InvariantCulture),
            parentChannelId?.ToString(CultureInfo.InvariantCulture),
            string.IsNullOrEmpty(topic) ? null : SnapshotText.Optional(topic, SnapshotText.TopicLimit),
            isNsfw,
            slowModeSeconds.HasValue ? Math.Max(0, slowModeSeconds.Value) : null,
            CategorySnapshot.NormalizeOverwrites(permissionOverwrites));
}
