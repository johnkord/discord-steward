using System.Collections.Immutable;

namespace DiscordSteward.Discord;

public interface IDiscordBotInspector
{
    Task<BotEffectivePermissions> GetEffectivePermissionsAsync(
        CancellationToken cancellationToken);
}

public sealed record BotEffectivePermissions(
    string GuildId,
    string BotUserId,
    ImmutableArray<string> EffectivePermissions,
    string? HighestRoleId,
    string? HighestRoleName,
    int? HighestRolePosition,
    bool HasAdministrator)
{
    public bool HasPermission(string permission) =>
        HasAdministrator || EffectivePermissions.Contains(permission, StringComparer.Ordinal);
}
