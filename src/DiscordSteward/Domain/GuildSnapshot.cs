using System.Collections.Immutable;
using System.Globalization;

namespace DiscordSteward.Domain;

public sealed record GuildSnapshot
{
    private GuildSnapshot(
        string id,
        string name,
        string ownerId,
        string? description,
        DateTimeOffset createdAt,
        ImmutableArray<string> features,
        ImmutableArray<RoleSnapshot> roles,
        ImmutableArray<CategorySnapshot> categories,
        ImmutableArray<ChannelSnapshot> channels)
    {
        Id = id;
        Name = name;
        OwnerId = ownerId;
        Description = description;
        CreatedAt = createdAt;
        Features = features;
        Roles = roles;
        Categories = categories;
        Channels = channels;
    }

    public string Id { get; }

    public string Name { get; }

    public string OwnerId { get; }

    public string? Description { get; }

    public DateTimeOffset CreatedAt { get; }

    public ImmutableArray<string> Features { get; }

    public ImmutableArray<RoleSnapshot> Roles { get; }

    public ImmutableArray<CategorySnapshot> Categories { get; }

    public ImmutableArray<ChannelSnapshot> Channels { get; }

    public static GuildSnapshot Create(
        ulong id,
        string name,
        ulong ownerId,
        string? description,
        DateTimeOffset createdAt,
        IEnumerable<string> features,
        IEnumerable<RoleSnapshot> roles,
        IEnumerable<CategorySnapshot> categories,
        IEnumerable<ChannelSnapshot> channels) => new(
            id.ToString(CultureInfo.InvariantCulture),
            SnapshotText.Required(name, SnapshotText.NameLimit),
            ownerId.ToString(CultureInfo.InvariantCulture),
            SnapshotText.Optional(description, SnapshotText.DescriptionLimit),
            createdAt,
            features
                .Select(feature => SnapshotText.Required(feature, SnapshotText.NameLimit))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            roles
                .OrderByDescending(role => role.Position)
                .ThenBy(role => role.Id, StringComparer.Ordinal)
                .ToImmutableArray(),
            categories
                .OrderBy(category => category.Position)
                .ThenBy(category => category.Id, StringComparer.Ordinal)
                .ToImmutableArray(),
            channels
                .OrderBy(channel => channel.Position)
                .ThenBy(channel => channel.Id, StringComparer.Ordinal)
                .ToImmutableArray());
}

public sealed record GuildReference(string Id, string? Name);
