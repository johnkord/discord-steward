using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record CreateRoleInput(
    string ExpectedRolesStateDigest,
    string Name,
    ImmutableArray<string> Permissions,
    string? ColorHex,
    bool IsHoisted,
    bool IsMentionable,
    string? IconAssetId,
    string? UnicodeEmoji,
    string? SecondaryColorHex = null,
    string? TertiaryColorHex = null);

public sealed record RoleAppearanceSet(
    string? Name,
    string? ColorHex,
    bool? IsHoisted,
    bool? IsMentionable,
    string? IconAssetId,
    string? UnicodeEmoji,
    string? SecondaryColorHex = null,
    string? TertiaryColorHex = null);

public sealed record UpdateRoleAppearanceInput(
    string ExpectedStateDigest,
    RoleAppearanceSet Set,
    ImmutableArray<string> Clear);

public sealed record SetRolePermissionsInput(
    string ExpectedStateDigest,
    ImmutableArray<string> Permissions);

public sealed record RolePositionInput(
    string RoleId,
    int Position);

public sealed record ReorderRolesInput(
    string ExpectedRolesStateDigest,
    ImmutableArray<RolePositionInput> Positions);

public sealed record SetMemberRoleInput(
    string UserId,
    string RoleId,
    bool Present,
    string ExpectedMemberStateDigest);

public sealed record DeleteRoleInput(
    string ExpectedStateDigest,
    string ExpectedCascadeDigest);
