using System.Collections.Immutable;
using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Permissions;

public sealed record RoleHierarchyEntry(
    string RoleId,
    string RoleName,
    int Position,
    bool IsManaged,
    bool IsEveryone,
    bool IsProtected,
    bool CanManage,
    string DecisionCode,
    string Explanation);

public sealed record RoleHierarchyReport(
    string GuildId,
    string BotUserId,
    string? BotHighestRoleId,
    int? BotHighestRolePosition,
    bool HasManageRoles,
    bool HasAdministrator,
    ImmutableArray<RoleHierarchyEntry> Roles);

public sealed class RoleHierarchyAnalyzer(
    IDiscordGuildReader guildReader,
    IDiscordBotInspector botInspector,
    StewardRuntimeSettings settings)
{
    public async Task<RoleHierarchyReport> AnalyzeAsync(CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            throw DiscordErrorMapper.NotConfigured();
        }

        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var configuredGuildId = settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(snapshot.Id, configuredGuildId, StringComparison.Ordinal) ||
            !string.Equals(bot.GuildId, configuredGuildId, StringComparison.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.GuildMismatch,
                "Discord returned a resource outside the configured guild.");
        }

        var hasManageRoles = bot.EffectivePermissions.Contains("ManageRoles", StringComparer.Ordinal);
        var entries = snapshot.Roles.Select(role => AnalyzeRole(
            role.Id,
            role.Name,
            role.Position,
            role.IsManaged,
            role.IsEveryone,
            role.IsProtected,
            bot.HighestRolePosition,
            hasManageRoles,
            bot.HasAdministrator)).ToImmutableArray();
        return new RoleHierarchyReport(
            snapshot.Id,
            bot.BotUserId,
            bot.HighestRoleId,
            bot.HighestRolePosition,
            hasManageRoles,
            bot.HasAdministrator,
            entries);
    }

    private static RoleHierarchyEntry AnalyzeRole(
        string roleId,
        string roleName,
        int position,
        bool isManaged,
        bool isEveryone,
        bool isProtected,
        int? botHighestRolePosition,
        bool hasManageRoles,
        bool hasAdministrator)
    {
        string code;
        string explanation;
        bool canManage;
        if (hasAdministrator)
        {
            code = "denied_bot_administrator";
            explanation = "Steward refuses all writes while the bot has Administrator.";
            canManage = false;
        }
        else if (!hasManageRoles)
        {
            code = "denied_missing_manage_roles";
            explanation = "The bot lacks Manage Roles.";
            canManage = false;
        }
        else if (isEveryone)
        {
            code = "denied_everyone_role";
            explanation = "The @everyone role cannot be managed as an ordinary role.";
            canManage = false;
        }
        else if (isManaged)
        {
            code = "denied_managed_role";
            explanation = "Discord or an integration manages this role.";
            canManage = false;
        }
        else if (isProtected)
        {
            code = "denied_steward_protection";
            explanation = "Local Steward policy protects this role.";
            canManage = false;
        }
        else if (!botHighestRolePosition.HasValue || botHighestRolePosition.Value <= position)
        {
            code = "denied_role_hierarchy";
            explanation = "The role is not below the bot's highest role.";
            canManage = false;
        }
        else
        {
            code = "manageable";
            explanation = "The role is unmanaged, unprotected, and below the bot's highest role.";
            canManage = true;
        }

        return new RoleHierarchyEntry(
            roleId,
            roleName,
            position,
            isManaged,
            isEveryone,
            isProtected,
            canManage,
            code,
            explanation);
    }
}
