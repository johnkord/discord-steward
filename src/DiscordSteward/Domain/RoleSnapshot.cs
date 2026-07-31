using System.Collections.Immutable;
using System.Globalization;
using DiscordSteward.Operations;

namespace DiscordSteward.Domain;

public sealed record RoleSnapshot
{
    private RoleSnapshot(
        string guildId,
        string id,
        string name,
        int position,
        bool isManaged,
        bool isEveryone,
        bool isProtected,
        ImmutableArray<string> protectionReasons,
        ImmutableArray<string> permissions,
        string? colorHex,
        string? secondaryColorHex,
        string? tertiaryColorHex,
        bool isHoisted,
        bool isMentionable,
        string? iconHash,
        string? unicodeEmoji)
    {
        GuildId = guildId;
        Id = id;
        Name = name;
        Position = position;
        IsManaged = isManaged;
        IsEveryone = isEveryone;
        IsProtected = isProtected;
        ProtectionReasons = protectionReasons;
        Permissions = permissions;
        ColorHex = colorHex;
        SecondaryColorHex = secondaryColorHex;
        TertiaryColorHex = tertiaryColorHex;
        IsHoisted = isHoisted;
        IsMentionable = isMentionable;
        IconHash = SnapshotText.Optional(iconHash, 256);
        UnicodeEmoji = SnapshotText.Optional(unicodeEmoji, 100);
        StateDigest = ResourceStateCanonicalizer.ComputeRoleDigest(this);
    }

    public string GuildId { get; }

    public string Id { get; }

    public string Name { get; }

    public int Position { get; }

    public bool IsManaged { get; }

    public bool IsEveryone { get; }

    public bool IsProtected { get; }

    public ImmutableArray<string> ProtectionReasons { get; }

    public ImmutableArray<string> Permissions { get; }

    public string? ColorHex { get; }

    public string? SecondaryColorHex { get; }

    public string? TertiaryColorHex { get; }

    public bool IsHoisted { get; }

    public bool IsMentionable { get; }

    public string? IconHash { get; }

    public string? UnicodeEmoji { get; }

    public string StateDigest { get; }

    public static RoleSnapshot Create(
        ulong guildId,
        ulong id,
        string name,
        int position,
        bool isManaged,
        bool isEveryone,
        ResourceProtection protection,
        IEnumerable<string> permissions,
        string? colorHex,
        bool isHoisted,
        bool isMentionable,
        string? iconHash = null,
        string? unicodeEmoji = null,
        string? secondaryColorHex = null,
        string? tertiaryColorHex = null) => new(
            guildId.ToString(CultureInfo.InvariantCulture),
            id.ToString(CultureInfo.InvariantCulture),
            SnapshotText.Required(name, SnapshotText.NameLimit),
            position,
            isManaged,
            isEveryone,
            protection.IsProtected,
            protection.Reasons,
            permissions
                .Select(permission => SnapshotText.Required(permission, SnapshotText.NameLimit))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            SnapshotText.Optional(colorHex, 16),
            SnapshotText.Optional(secondaryColorHex, 16),
            SnapshotText.Optional(tertiaryColorHex, 16),
            isHoisted,
            isMentionable,
            iconHash,
            unicodeEmoji);
}
