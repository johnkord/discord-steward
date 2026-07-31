using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace DiscordSteward.Configuration;

public enum StewardToolRisk
{
    R0,
    R1,
    R2,
    R3,
    R4,
    R5
}

public sealed record StewardRiskGates(
    bool SensitiveReads,
    bool ReversibleWrites,
    bool PersonImpactingWrites,
    bool IrreversibleWrites)
{
    public static StewardRiskGates AllOpen { get; } = new(
        SensitiveReads: true,
        ReversibleWrites: true,
        PersonImpactingWrites: true,
        IrreversibleWrites: true);
}

public sealed record StewardToolDefinition(
    string Name,
    StewardToolRisk Risk,
    ImmutableArray<string> CompanionReads);

public sealed class StewardToolManifest
{
    private static readonly ImmutableDictionary<string, StewardToolDefinition> Definitions =
        new[]
        {
            Define("get_steward_capabilities", StewardToolRisk.R0),
            Define("register_asset", StewardToolRisk.R0),
            Define("list_assets", StewardToolRisk.R0),
            Define("get_asset", StewardToolRisk.R0),
            Define("delete_asset", StewardToolRisk.R0),
            Define("simulate_automod_rule", StewardToolRisk.R0),
            Define("capture_guild_configuration_snapshot", StewardToolRisk.R1),
            Define("compare_guild_configuration", StewardToolRisk.R1, "capture_guild_configuration_snapshot"),
            Define("get_bot_effective_permissions", StewardToolRisk.R1),
            Define("get_bot_guild_profile", StewardToolRisk.R1),
            Define("get_operation", StewardToolRisk.R2),
            Define("list_operations", StewardToolRisk.R2),
            Define("reconcile_operation", StewardToolRisk.R2),
            Define("get_guild", StewardToolRisk.R1),
            Define("get_guild_configuration_snapshot", StewardToolRisk.R1),
            Define("get_guild_preview", StewardToolRisk.R1),
            Define("get_guild_widget", StewardToolRisk.R1),
            Define("get_guild_widget_image", StewardToolRisk.R1),
            Define("get_incident_status", StewardToolRisk.R1),
            Define("get_guild_vanity_url", StewardToolRisk.R1),
            Define("list_guild_templates", StewardToolRisk.R2),
            Define("get_guild_template", StewardToolRisk.R2),
            Define("list_roles", StewardToolRisk.R1),
            Define("get_role", StewardToolRisk.R1),
            Define("list_channels", StewardToolRisk.R1),
            Define("get_channel", StewardToolRisk.R1),
            Define("list_channel_member_overwrites", StewardToolRisk.R2),
            Define("lint_guild_configuration", StewardToolRisk.R1),
            Define("explain_role_permissions", StewardToolRisk.R1),
            Define("analyze_role_hierarchy", StewardToolRisk.R1),
            Define("analyze_channel_access", StewardToolRisk.R1),
            Define("analyze_privilege_escalation_paths", StewardToolRisk.R1),
            Define("analyze_moderation_configuration", StewardToolRisk.R1),
            Define("analyze_onboarding_configuration", StewardToolRisk.R1),
            Define("analyze_deletion_impact", StewardToolRisk.R2),
            Define("explain_member_permissions", StewardToolRisk.R2),
            Define("list_automod_rules", StewardToolRisk.R1),
            Define("get_automod_rule", StewardToolRisk.R1),
            Define("get_onboarding", StewardToolRisk.R1),
            Define("get_welcome_screen", StewardToolRisk.R1),
            Define("list_scheduled_events", StewardToolRisk.R1),
            Define("get_scheduled_event", StewardToolRisk.R1),
            Define("list_bans", StewardToolRisk.R2),
            Define("get_ban", StewardToolRisk.R2),
            Define("list_invites", StewardToolRisk.R2),
            Define("get_invite", StewardToolRisk.R2),
            Define("list_integrations", StewardToolRisk.R2),
            Define("list_webhooks", StewardToolRisk.R2),
            Define("get_webhook", StewardToolRisk.R2),
            Define("list_audit_events", StewardToolRisk.R2),
            Define("list_guild_expressions", StewardToolRisk.R1),
            Define("get_guild_expression", StewardToolRisk.R1),
            Define("list_voice_regions", StewardToolRisk.R1),
            Define("list_sticker_packs", StewardToolRisk.R1),
            Define("get_sticker_pack", StewardToolRisk.R1),
            Define("list_default_soundboard_sounds", StewardToolRisk.R1),
            Define("list_active_threads", StewardToolRisk.R2),
            Define("list_archived_threads", StewardToolRisk.R2),
            Define("get_thread", StewardToolRisk.R2),
            Define("list_thread_members", StewardToolRisk.R2),
            Define("get_thread_member", StewardToolRisk.R2, "get_thread"),
            Define("list_channel_invites", StewardToolRisk.R2, "get_channel"),
            Define("get_member", StewardToolRisk.R2),
            Define("search_members", StewardToolRisk.R2),
            Define("list_members", StewardToolRisk.R2),
            Define("list_members_by_role", StewardToolRisk.R2),
            Define("get_member_voice_state", StewardToolRisk.R2),
            Define("get_role_member_counts", StewardToolRisk.R2),
            Define("preview_member_prune", StewardToolRisk.R2),
            Define("list_scheduled_event_subscribers", StewardToolRisk.R2),
            Define("get_stage_instance", StewardToolRisk.R1),
            Define("list_invite_target_users", StewardToolRisk.R2),
            Define("get_invite_target_user_job_status", StewardToolRisk.R2),
            Define("get_message", StewardToolRisk.R2),
            Define("list_messages", StewardToolRisk.R2),
            Define("search_messages", StewardToolRisk.R2),
            Define("list_channel_pins", StewardToolRisk.R2),
            Define("list_message_reactions", StewardToolRisk.R2),
            Define("list_poll_voters", StewardToolRisk.R2),
            Define(
                "create_role",
                StewardToolRisk.R3,
                "get_guild_configuration_snapshot",
                "get_operation",
                "reconcile_operation"),
            Define(
                "update_role_appearance",
                StewardToolRisk.R3,
                "get_role",
                "get_operation",
                "reconcile_operation"),
            Define(
                "set_role_permissions",
                StewardToolRisk.R4,
                "get_role",
                "analyze_privilege_escalation_paths",
                "get_operation",
                "reconcile_operation"),
            Define(
                "reorder_roles",
                StewardToolRisk.R4,
                "get_guild_configuration_snapshot",
                "analyze_role_hierarchy",
                "get_operation",
                "reconcile_operation"),
            Define(
                "set_member_role",
                StewardToolRisk.R4,
                "get_member",
                "get_role",
                "get_operation",
                "reconcile_operation"),
            Define(
                "delete_role",
                StewardToolRisk.R5,
                "get_role",
                "analyze_deletion_impact",
                "get_operation",
                "reconcile_operation"),
            Define("create_category", StewardToolRisk.R3, "get_guild_configuration_snapshot", "get_operation", "reconcile_operation"),
            Define("create_text_channel", StewardToolRisk.R3, "get_guild_configuration_snapshot", "get_channel", "get_operation", "reconcile_operation"),
            Define("create_announcement_channel", StewardToolRisk.R3, "get_guild_configuration_snapshot", "get_channel", "get_operation", "reconcile_operation"),
            Define("create_voice_channel", StewardToolRisk.R3, "get_guild_configuration_snapshot", "get_channel", "get_operation", "reconcile_operation"),
            Define("create_stage_channel", StewardToolRisk.R3, "get_guild_configuration_snapshot", "get_channel", "get_operation", "reconcile_operation"),
            Define("create_forum_channel", StewardToolRisk.R3, "get_guild_configuration_snapshot", "get_channel", "get_operation", "reconcile_operation"),
            Define("create_media_channel", StewardToolRisk.R3, "get_guild_configuration_snapshot", "get_channel", "get_operation", "reconcile_operation"),
            Define("update_channel", StewardToolRisk.R3, "get_channel", "get_operation", "reconcile_operation"),
            Define("move_channel", StewardToolRisk.R3, "get_channel", "get_guild_configuration_snapshot", "get_operation", "reconcile_operation"),
            Define("move_channel_and_sync_permissions", StewardToolRisk.R4, "get_channel", "get_guild_configuration_snapshot", "get_operation", "reconcile_operation"),
            Define("reorder_channels", StewardToolRisk.R3, "get_guild_configuration_snapshot", "get_operation", "reconcile_operation"),
            Define("set_role_channel_permissions", StewardToolRisk.R3, "get_channel", "get_operation", "reconcile_operation"),
            Define("remove_role_channel_permissions", StewardToolRisk.R3, "get_channel", "get_operation", "reconcile_operation"),
            Define("set_member_channel_permissions", StewardToolRisk.R4, "get_channel", "get_operation", "reconcile_operation"),
            Define("remove_member_channel_permissions", StewardToolRisk.R4, "get_channel", "get_operation", "reconcile_operation"),
            Define("replace_channel_permission_overwrites", StewardToolRisk.R4, "get_channel", "list_channel_member_overwrites", "get_operation", "reconcile_operation"),
            Define("archive_channel", StewardToolRisk.R4, "get_channel", "get_operation", "reconcile_operation"),
            Define("restore_archived_channel", StewardToolRisk.R4, "get_channel", "get_operation", "reconcile_operation"),
            Define("set_voice_channel_status", StewardToolRisk.R3, "get_channel", "get_operation", "reconcile_operation"),
            Define("trigger_typing_indicator", StewardToolRisk.R4, "get_channel", "get_operation", "reconcile_operation"),
            Define("follow_announcement_channel", StewardToolRisk.R4, "get_channel", "list_webhooks", "get_operation", "reconcile_operation"),
            Define("create_thread", StewardToolRisk.R3, "get_channel", "get_operation", "reconcile_operation"),
            Define("create_forum_post", StewardToolRisk.R4, "get_channel", "get_operation", "reconcile_operation"),
            Define("update_thread", StewardToolRisk.R3, "get_thread", "get_operation", "reconcile_operation"),
            Define("set_thread_member", StewardToolRisk.R4, "get_thread", "get_operation", "reconcile_operation"),
            Define("delete_thread", StewardToolRisk.R5, "get_thread", "get_operation", "reconcile_operation"),
            Define("delete_channel", StewardToolRisk.R5, "get_channel", "analyze_deletion_impact", "get_operation", "reconcile_operation"),
            Define("set_member_nickname", StewardToolRisk.R4, "get_member", "get_operation", "reconcile_operation"),
            Define("set_member_timeout", StewardToolRisk.R4, "get_member", "explain_member_permissions", "get_operation", "reconcile_operation"),
            Define("set_member_voice_channel", StewardToolRisk.R4, "get_member", "get_channel", "get_operation", "reconcile_operation"),
            Define("update_member_voice_state", StewardToolRisk.R4, "get_member", "get_operation", "reconcile_operation"),
            Define("set_member_verification_bypass", StewardToolRisk.R4, "get_member", "get_operation", "reconcile_operation"),
            Define("kick_member", StewardToolRisk.R5, "get_member", "get_operation", "reconcile_operation"),
            Define("ban_member", StewardToolRisk.R5, "get_member", "get_ban", "get_operation", "reconcile_operation"),
            Define("bulk_ban_members", StewardToolRisk.R5, "get_member", "get_ban", "get_operation", "reconcile_operation"),
            Define("unban_member", StewardToolRisk.R4, "get_ban", "get_operation", "reconcile_operation"),
            Define("send_message", StewardToolRisk.R4, "get_channel", "get_message", "list_assets", "get_operation", "reconcile_operation"),
            Define("edit_own_message", StewardToolRisk.R4, "get_message", "list_assets", "get_operation", "reconcile_operation"),
            Define("delete_message", StewardToolRisk.R5, "get_message", "get_operation", "reconcile_operation"),
            Define("bulk_delete_messages", StewardToolRisk.R5, "get_message", "get_operation", "reconcile_operation"),
            Define("crosspost_announcement_message", StewardToolRisk.R4, "get_message", "get_operation", "reconcile_operation"),
            Define("set_message_pinned", StewardToolRisk.R4, "get_message", "get_operation", "reconcile_operation"),
            Define("set_own_reaction", StewardToolRisk.R4, "get_message", "list_message_reactions", "get_operation", "reconcile_operation"),
            Define("remove_user_reaction", StewardToolRisk.R5, "get_message", "list_message_reactions", "get_operation", "reconcile_operation"),
            Define("clear_message_reactions", StewardToolRisk.R5, "get_message", "get_operation", "reconcile_operation"),
            Define("create_poll", StewardToolRisk.R4, "get_channel", "get_operation", "reconcile_operation"),
            Define("end_poll", StewardToolRisk.R4, "get_message", "get_operation", "reconcile_operation"),
            Define("create_automod_rule", StewardToolRisk.R4, "list_automod_rules", "get_operation", "reconcile_operation"),
            Define("update_automod_rule", StewardToolRisk.R4, "get_automod_rule", "get_operation", "reconcile_operation"),
            Define("set_automod_rule_enabled", StewardToolRisk.R4, "get_automod_rule", "get_operation", "reconcile_operation"),
            Define("delete_automod_rule", StewardToolRisk.R5, "get_automod_rule", "get_operation", "reconcile_operation"),
            Define("update_guild_metadata", StewardToolRisk.R3, "get_guild", "list_assets", "get_operation", "reconcile_operation"),
            Define("update_guild_system_routing", StewardToolRisk.R3, "get_guild", "get_operation", "reconcile_operation"),
            Define("update_guild_safety", StewardToolRisk.R4, "get_guild", "analyze_moderation_configuration", "get_operation", "reconcile_operation"),
            Define("update_guild_access_controls", StewardToolRisk.R4, "get_guild", "analyze_moderation_configuration", "get_operation", "reconcile_operation"),
            Define("update_guild_widget", StewardToolRisk.R3, "get_guild_widget", "get_operation", "reconcile_operation"),
            Define("update_welcome_screen", StewardToolRisk.R3, "get_welcome_screen", "get_operation", "reconcile_operation"),
            Define("update_onboarding", StewardToolRisk.R4, "get_onboarding", "analyze_onboarding_configuration", "get_operation", "reconcile_operation"),
            Define("set_incident_actions", StewardToolRisk.R4, "get_incident_status", "get_operation", "reconcile_operation"),
            Define("create_scheduled_event", StewardToolRisk.R4, "list_scheduled_events", "list_assets", "get_operation", "reconcile_operation"),
            Define("update_scheduled_event", StewardToolRisk.R4, "get_scheduled_event", "list_assets", "get_operation", "reconcile_operation"),
            Define("start_scheduled_event", StewardToolRisk.R4, "get_scheduled_event", "get_operation", "reconcile_operation"),
            Define("complete_scheduled_event", StewardToolRisk.R5, "get_scheduled_event", "get_operation", "reconcile_operation"),
            Define("cancel_scheduled_event", StewardToolRisk.R5, "get_scheduled_event", "get_operation", "reconcile_operation"),
            Define("delete_scheduled_event", StewardToolRisk.R5, "get_scheduled_event", "get_operation", "reconcile_operation"),
            Define("start_stage_instance", StewardToolRisk.R4, "get_channel", "get_stage_instance", "get_operation", "reconcile_operation"),
            Define("update_stage_instance", StewardToolRisk.R3, "get_stage_instance", "get_operation", "reconcile_operation"),
            Define("end_stage_instance", StewardToolRisk.R4, "get_stage_instance", "get_operation", "reconcile_operation"),
            Define("set_stage_speaker", StewardToolRisk.R4, "get_stage_instance", "get_member", "get_operation", "reconcile_operation"),
            Define("request_stage_speaking", StewardToolRisk.R4, "get_stage_instance", "get_member_voice_state", "get_operation", "reconcile_operation"),
            Define("create_guild_expression", StewardToolRisk.R3, "list_guild_expressions", "list_assets", "get_operation", "reconcile_operation"),
            Define("update_guild_expression", StewardToolRisk.R3, "get_guild_expression", "get_operation", "reconcile_operation"),
            Define("delete_guild_expression", StewardToolRisk.R5, "get_guild_expression", "get_operation", "reconcile_operation"),
            Define("create_invite", StewardToolRisk.R4, "get_channel", "get_operation", "reconcile_operation"),
            Define("create_targeted_invite", StewardToolRisk.R4, "get_channel", "get_invite_target_user_job_status", "get_operation", "reconcile_operation"),
            Define("update_invite_target_users", StewardToolRisk.R4, "list_invite_target_users", "get_invite_target_user_job_status", "get_operation", "reconcile_operation"),
            Define("delete_invite", StewardToolRisk.R5, "get_invite", "get_operation", "reconcile_operation"),
            Define("create_webhook", StewardToolRisk.R4, "get_channel", "list_webhooks", "get_operation", "reconcile_operation"),
            Define("update_webhook", StewardToolRisk.R4, "get_webhook", "get_operation", "reconcile_operation"),
            Define("delete_webhook", StewardToolRisk.R5, "get_webhook", "get_operation", "reconcile_operation"),
            Define("send_webhook_message", StewardToolRisk.R4, "get_webhook", "list_assets", "get_operation", "reconcile_operation"),
            Define("edit_webhook_message", StewardToolRisk.R4, "get_webhook", "get_message", "list_assets", "get_operation", "reconcile_operation"),
            Define("delete_webhook_message", StewardToolRisk.R5, "get_webhook", "get_message", "get_operation", "reconcile_operation"),
            Define("create_guild_template", StewardToolRisk.R4, "get_guild_configuration_snapshot", "get_operation", "reconcile_operation"),
            Define("sync_guild_template", StewardToolRisk.R4, "get_guild_configuration_snapshot", "get_guild_template", "get_operation", "reconcile_operation"),
            Define("update_guild_template", StewardToolRisk.R3, "get_guild_template", "get_operation", "reconcile_operation"),
            Define("delete_guild_template", StewardToolRisk.R5, "get_guild_template", "get_operation", "reconcile_operation"),
            Define("update_bot_guild_profile", StewardToolRisk.R3, "get_bot_guild_profile", "list_assets", "get_operation", "reconcile_operation")
        }.ToImmutableDictionary(definition => definition.Name, StringComparer.Ordinal);

    internal static ImmutableArray<string> DefinedNames => Definitions.Keys.Order(StringComparer.Ordinal).ToImmutableArray();

    private static readonly ImmutableArray<string> DefaultObserverAllowlist =
    [
        "get_steward_capabilities",
        "capture_guild_configuration_snapshot",
        "compare_guild_configuration",
        "get_bot_effective_permissions",
        "get_bot_guild_profile",
        "get_guild",
        "get_guild_configuration_snapshot",
        "get_guild_preview",
        "list_roles",
        "get_role",
        "list_channels",
        "get_channel",
        "lint_guild_configuration",
        "explain_role_permissions",
        "analyze_role_hierarchy",
        "analyze_channel_access",
        "analyze_privilege_escalation_paths",
        "analyze_moderation_configuration",
        "analyze_onboarding_configuration",
        "list_automod_rules",
        "get_onboarding",
        "get_welcome_screen",
        "list_scheduled_events",
        "list_guild_expressions",
        "list_voice_regions",
        "list_sticker_packs",
        "get_sticker_pack",
        "list_default_soundboard_sounds",
        "get_guild_widget_image"
    ];

    private StewardToolManifest(
        ImmutableArray<string> configuredNames,
        ImmutableArray<string> registeredNames,
        StewardRiskGates riskGates,
        StewardAuthorizationMode authorizationMode,
        ImmutableArray<string> warnings)
    {
        ConfiguredNames = configuredNames;
        RegisteredNames = registeredNames;
        RiskGates = riskGates;
        AuthorizationMode = authorizationMode;
        Warnings = warnings;
        RequiredDiscordPermissions = registeredNames
            .SelectMany(GetRequiredPermissions)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        Digest = ComputeDigest();
    }

    public ImmutableArray<string> ConfiguredNames { get; }

    public ImmutableArray<string> RegisteredNames { get; }

    public StewardRiskGates RiskGates { get; }

    public StewardAuthorizationMode AuthorizationMode { get; }

    public ImmutableArray<string> Warnings { get; }

    public ImmutableArray<string> RequiredDiscordPermissions { get; }

    public string Digest { get; }

    public bool IsCompleteCatalog => RegisteredNames.SequenceEqual(DefinedNames);

    public bool RepresentsEveryRiskClass => Enum.GetValues<StewardToolRisk>()
        .All(risk => RegisteredNames.Any(name => Definitions[name].Risk == risk));

    public static StewardToolManifest Create(
        IEnumerable<string?> toolAllowlist,
        StewardRiskGates riskGates,
        StewardAuthorizationMode authorizationMode = StewardAuthorizationMode.InteractiveApproval)
    {
        ArgumentNullException.ThrowIfNull(toolAllowlist);
        ArgumentNullException.ThrowIfNull(riskGates);

        var configuredNames = toolAllowlist
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (configuredNames.IsDefaultOrEmpty)
        {
            configuredNames = DefaultObserverAllowlist;
        }

        if (authorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy)
        {
            if (configuredNames.Length != 1 || !configuredNames[0].Equals("*", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "UnrestrictedAutonomy requires toolAllowlist to contain exactly the all-tools selector '*'.",
                    nameof(toolAllowlist));
            }

            configuredNames = DefinedNames;
            riskGates = StewardRiskGates.AllOpen;
        }
        else if (configuredNames.Contains("*", StringComparer.Ordinal))
        {
            if (configuredNames.Length != 1)
            {
                throw new ArgumentException(
                    "The all-tools selector '*' cannot be combined with exact tool names.",
                    nameof(toolAllowlist));
            }

            configuredNames = DefinedNames;
        }

        var unknownNames = configuredNames.Where(name => !Definitions.ContainsKey(name)).ToArray();
        if (unknownNames.Length > 0)
        {
            throw new ArgumentException(
                $"Tool allowlist contains unknown names: {string.Join(", ", unknownNames)}.",
                nameof(toolAllowlist));
        }

        var registeredNames = authorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy
            ? configuredNames
            : configuredNames
                .Where(name => IsGateOpen(Definitions[name].Risk, riskGates))
                .ToImmutableArray();

        var registered = registeredNames.ToImmutableHashSet(StringComparer.Ordinal);
        var warnings = ImmutableArray.CreateBuilder<string>();
        foreach (var name in registeredNames)
        {
            foreach (var companion in Definitions[name].CompanionReads)
            {
                if (!registered.Contains(companion))
                {
                    warnings.Add($"{name} is missing companion tool {companion}.");
                }
            }
        }

        return new StewardToolManifest(
            configuredNames,
            registeredNames,
            riskGates,
            authorizationMode,
            warnings.ToImmutable());
    }

    public bool IsRegistered(string toolName) => RegisteredNames.Contains(toolName, StringComparer.Ordinal);

    public static StewardToolRisk GetRisk(string toolName) => Definitions.TryGetValue(toolName, out var definition)
        ? definition.Risk
        : throw new ArgumentOutOfRangeException(nameof(toolName));

    public static ImmutableArray<string> GetRequiredDiscordPermissions(string toolName) =>
        Definitions.ContainsKey(toolName)
            ? GetRequiredPermissions(toolName).Order(StringComparer.Ordinal).ToImmutableArray()
            : throw new ArgumentOutOfRangeException(nameof(toolName));

    private static StewardToolDefinition Define(
        string name,
        StewardToolRisk risk,
        params string[] companionReads) => new(name, risk, [.. companionReads]);

    private static bool IsGateOpen(StewardToolRisk risk, StewardRiskGates gates) => risk switch
    {
        StewardToolRisk.R0 or StewardToolRisk.R1 => true,
        StewardToolRisk.R2 => gates.SensitiveReads,
        StewardToolRisk.R3 => gates.ReversibleWrites,
        StewardToolRisk.R4 => gates.PersonImpactingWrites,
        StewardToolRisk.R5 => gates.IrreversibleWrites,
        _ => false
    };

    private static IEnumerable<string> GetRequiredPermissions(string toolName) => toolName switch
    {
        "get_operation" or "list_operations" or "get_steward_capabilities" or
            "register_asset" or "list_assets" or "get_asset" or "delete_asset" or
            "simulate_automod_rule" or "list_voice_regions" or "list_sticker_packs" or
            "get_sticker_pack" or "list_default_soundboard_sounds" or "get_guild_preview" or
            "get_guild_widget_image" or "get_bot_guild_profile" or "update_bot_guild_profile" => [],
        "update_channel" => ["ManageChannels", "ViewChannel"],
        "list_bans" or "get_ban" => ["BanMembers"],
        "list_invites" or "get_invite" or "list_integrations" => ["ManageGuild"],
        "list_channel_invites" => ["ManageChannels"],
        "list_webhooks" or "get_webhook" => ["ManageWebhooks"],
        "list_audit_events" => ["ViewAuditLog"],
        "get_guild_widget" or "get_incident_status" or "get_guild_vanity_url" or
            "list_guild_templates" or "get_guild_template" or
            "list_invite_target_users" or "get_invite_target_user_job_status" => ["ManageGuild"],
        "list_archived_threads" => ["ReadMessageHistory", "ViewChannel"],
        "list_thread_members" => ["GuildMembersIntent", "ViewChannel"],
        "get_thread_member" => ["ViewChannel"],
        "list_scheduled_event_subscribers" => ["ViewChannel"],
        "get_message" or "list_messages" or "list_channel_pins" or
            "list_message_reactions" or "list_poll_voters" => ["ReadMessageHistory", "ViewChannel"],
        "search_messages" => ["MessageContentIntent", "ReadMessageHistory", "ViewChannel"],
        "create_role" or "update_role_appearance" or "set_role_permissions" or
            "reorder_roles" or "delete_role" => ["ManageRoles"],
        "set_member_role" => ["GuildMembersIntent", "ManageRoles"],
        "create_category" or "create_text_channel" or "create_announcement_channel" or
            "create_voice_channel" or "create_stage_channel" or "create_forum_channel" or
            "create_media_channel" => ["ManageChannels", "ManageRoles", "ViewChannel"],
        "move_channel" or "move_channel_and_sync_permissions" or "reorder_channels" or
            "archive_channel" or "restore_archived_channel" or "delete_channel" => ["ManageChannels", "ViewChannel"],
        "set_role_channel_permissions" or "remove_role_channel_permissions" or
            "set_member_channel_permissions" or "remove_member_channel_permissions" or
            "replace_channel_permission_overwrites" => ["ManageRoles", "ViewChannel"],
        "set_voice_channel_status" => ["ManageChannels", "SetVoiceChannelStatus", "ViewChannel"],
        "trigger_typing_indicator" => ["SendMessages", "ViewChannel"],
        "follow_announcement_channel" => ["ManageWebhooks", "ViewChannel"],
        "create_thread" => ["CreatePrivateThreads", "CreatePublicThreads", "SendMessages", "ViewChannel"],
        "create_forum_post" => ["AttachFiles", "EmbedLinks", "SendMessages", "ViewChannel"],
        "update_thread" or "set_thread_member" or "delete_thread" => ["ManageThreads", "ViewChannel"],
        "set_member_nickname" => ["GuildMembersIntent", "ManageNicknames"],
        "set_member_timeout" => ["GuildMembersIntent", "ModerateMembers"],
        "set_member_voice_channel" => ["GuildMembersIntent", "MoveMembers"],
        "update_member_voice_state" => ["DeafenMembers", "GuildMembersIntent", "MuteMembers"],
        "set_member_verification_bypass" => ["GuildMembersIntent", "ManageGuild"],
        "kick_member" => ["GuildMembersIntent", "KickMembers"],
        "ban_member" or "unban_member" => ["BanMembers"],
        "bulk_ban_members" => ["BanMembers", "ManageGuild"],
        "send_message" => ["AttachFiles", "EmbedLinks", "ReadMessageHistory", "SendMessages", "ViewChannel"],
        "edit_own_message" => ["AttachFiles", "EmbedLinks", "ReadMessageHistory", "SendMessages", "ViewChannel"],
        "create_poll" => ["SendMessages", "ViewChannel"],
        "delete_message" or "bulk_delete_messages" => ["ManageMessages", "ReadMessageHistory", "ViewChannel"],
        "crosspost_announcement_message" => ["ManageMessages", "SendMessages", "ViewChannel"],
        "set_message_pinned" => ["PinMessages", "ReadMessageHistory", "ViewChannel"],
        "set_own_reaction" => ["AddReactions", "ReadMessageHistory", "ViewChannel"],
        "remove_user_reaction" or "clear_message_reactions" => ["ManageMessages", "ReadMessageHistory", "ViewChannel"],
        "end_poll" => ["ReadMessageHistory", "SendMessages", "ViewChannel"],
        "create_automod_rule" or "update_automod_rule" or "set_automod_rule_enabled" or
            "delete_automod_rule" => ["ManageGuild", "ModerateMembers"],
        "update_guild_metadata" or "update_guild_system_routing" or "update_guild_safety" or "update_guild_access_controls" or
            "update_guild_widget" or "update_welcome_screen" or "set_incident_actions" => ["ManageGuild"],
        "update_onboarding" => ["ManageGuild", "ManageRoles"],
        "create_scheduled_event" => ["CreateEvents", "ViewChannel"],
        "update_scheduled_event" or "start_scheduled_event" or "complete_scheduled_event" or
            "cancel_scheduled_event" or "delete_scheduled_event" => ["ManageEvents", "ViewChannel"],
        "start_stage_instance" or "update_stage_instance" or "end_stage_instance" or
            "set_stage_speaker" => ["ManageChannels", "MoveMembers", "MuteMembers", "ViewChannel"],
        "request_stage_speaking" => ["RequestToSpeak", "ViewChannel"],
        "create_guild_expression" => ["CreateGuildExpressions"],
        "update_guild_expression" or "delete_guild_expression" => ["ManageEmojisAndStickers"],
        "create_invite" or "create_targeted_invite" => ["CreateInstantInvite", "ManageRoles", "ViewChannel"],
        "update_invite_target_users" => ["ManageGuild"],
        "delete_invite" => ["ManageGuild"],
        "create_webhook" or "update_webhook" or "delete_webhook" => ["ManageWebhooks", "ViewChannel"],
        "send_webhook_message" or "edit_webhook_message" or "delete_webhook_message" => [],
        "create_guild_template" or "sync_guild_template" or "update_guild_template" or
            "delete_guild_template" => ["ManageGuild"],
        "get_member" or "search_members" or "list_members" or "list_members_by_role" or "get_member_voice_state" => ["GuildMembersIntent"],
        "preview_member_prune" => ["KickMembers", "ManageGuild"],
        _ => ["ViewChannel"]
    };

    private string ComputeDigest()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", AuthorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy ? 2 : 1);
            writer.WriteString("authorizationMode", AuthorizationMode.ToString());
            writer.WriteStartArray("registeredTools");
            foreach (var name in RegisteredNames)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            writer.WriteStartObject("riskGates");
            writer.WriteBoolean("sensitiveReads", RiskGates.SensitiveReads);
            writer.WriteBoolean("reversibleWrites", RiskGates.ReversibleWrites);
            writer.WriteBoolean("personImpactingWrites", RiskGates.PersonImpactingWrites);
            writer.WriteBoolean("irreversibleWrites", RiskGates.IrreversibleWrites);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
