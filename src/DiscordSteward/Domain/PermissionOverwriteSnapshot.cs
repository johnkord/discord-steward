using System.Collections.Immutable;
using System.Globalization;

namespace DiscordSteward.Domain;

public sealed record PermissionOverwriteSnapshot
{
    private PermissionOverwriteSnapshot(
        string targetType,
        string targetId,
        ImmutableArray<string> allowedPermissions,
        ImmutableArray<string> deniedPermissions)
    {
        TargetType = targetType;
        TargetId = targetId;
        AllowedPermissions = allowedPermissions;
        DeniedPermissions = deniedPermissions;
    }

    public string TargetType { get; }

    public string TargetId { get; }

    public ImmutableArray<string> AllowedPermissions { get; }

    public ImmutableArray<string> DeniedPermissions { get; }

    public static PermissionOverwriteSnapshot Create(
        string targetType,
        ulong targetId,
        IEnumerable<string> allowedPermissions,
        IEnumerable<string> deniedPermissions) => new(
            SnapshotText.Required(targetType, 32).ToLowerInvariant(),
            targetId.ToString(CultureInfo.InvariantCulture),
            NormalizePermissions(allowedPermissions),
            NormalizePermissions(deniedPermissions));

    private static ImmutableArray<string> NormalizePermissions(IEnumerable<string> permissions) =>
        permissions
            .Select(permission => SnapshotText.Required(permission, SnapshotText.NameLimit))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
}
