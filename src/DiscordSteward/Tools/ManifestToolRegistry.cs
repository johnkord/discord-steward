using System.Collections.Immutable;
using System.ComponentModel;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Linting;
using DiscordSteward.Operations;
using DiscordSteward.Permissions;
using DiscordSteward.Snapshots;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

public static class ManifestToolRegistry
{
    private static readonly ImmutableDictionary<string, Type> ToolTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["get_steward_capabilities"] = typeof(GetStewardCapabilitiesTool),
            ["register_asset"] = typeof(RegisterAssetTool),
            ["list_assets"] = typeof(ListAssetsTool),
            ["get_asset"] = typeof(GetAssetTool),
            ["delete_asset"] = typeof(DeleteAssetTool),
            ["simulate_automod_rule"] = typeof(SimulateAutoModRuleTool),
            ["capture_guild_configuration_snapshot"] = typeof(CaptureGuildConfigurationSnapshotTool),
            ["compare_guild_configuration"] = typeof(CompareGuildConfigurationTool),
            ["get_bot_effective_permissions"] = typeof(GetBotEffectivePermissionsTool),
            ["get_bot_guild_profile"] = typeof(GetBotGuildProfileTool),
            ["get_operation"] = typeof(GetOperationTool),
            ["list_operations"] = typeof(ListOperationsTool),
            ["reconcile_operation"] = typeof(ReconcileOperationTool),
            ["get_guild"] = typeof(GetGuildTool),
            ["get_guild_configuration_snapshot"] = typeof(GetGuildConfigurationSnapshotTool),
            ["get_guild_preview"] = typeof(GetGuildPreviewTool),
            ["get_guild_widget"] = typeof(GetGuildWidgetTool),
            ["get_guild_widget_image"] = typeof(GetGuildWidgetImageTool),
            ["get_incident_status"] = typeof(GetIncidentStatusTool),
            ["get_guild_vanity_url"] = typeof(GetGuildVanityUrlTool),
            ["list_guild_templates"] = typeof(ListGuildTemplatesTool),
            ["get_guild_template"] = typeof(GetGuildTemplateTool),
            ["list_roles"] = typeof(ListRolesTool),
            ["get_role"] = typeof(GetRoleTool),
            ["list_channels"] = typeof(ListChannelsTool),
            ["get_channel"] = typeof(GetChannelTool),
            ["list_channel_member_overwrites"] = typeof(ListChannelMemberOverwritesTool),
            ["lint_guild_configuration"] = typeof(LintGuildConfigurationTool),
            ["explain_role_permissions"] = typeof(ExplainRolePermissionsTool),
            ["analyze_role_hierarchy"] = typeof(AnalyzeRoleHierarchyTool),
            ["analyze_channel_access"] = typeof(AnalyzeChannelAccessTool),
            ["analyze_privilege_escalation_paths"] = typeof(AnalyzePrivilegeEscalationPathsTool),
            ["analyze_moderation_configuration"] = typeof(AnalyzeModerationConfigurationTool),
            ["analyze_onboarding_configuration"] = typeof(AnalyzeOnboardingConfigurationTool),
            ["analyze_deletion_impact"] = typeof(AnalyzeDeletionImpactTool),
            ["explain_member_permissions"] = typeof(ExplainMemberPermissionsTool),
            ["list_automod_rules"] = typeof(ListAutoModRulesTool),
            ["get_automod_rule"] = typeof(GetAutoModRuleTool),
            ["get_onboarding"] = typeof(GetOnboardingTool),
            ["get_welcome_screen"] = typeof(GetWelcomeScreenTool),
            ["list_scheduled_events"] = typeof(ListScheduledEventsTool),
            ["get_scheduled_event"] = typeof(GetScheduledEventTool),
            ["list_bans"] = typeof(ListBansTool),
            ["get_ban"] = typeof(GetBanTool),
            ["list_invites"] = typeof(ListInvitesTool),
            ["get_invite"] = typeof(GetInviteTool),
            ["list_integrations"] = typeof(ListIntegrationsTool),
            ["list_webhooks"] = typeof(ListWebhooksTool),
            ["get_webhook"] = typeof(GetWebhookTool),
            ["list_audit_events"] = typeof(ListAuditEventsTool),
            ["list_guild_expressions"] = typeof(ListGuildExpressionsTool),
            ["get_guild_expression"] = typeof(GetGuildExpressionTool),
            ["list_voice_regions"] = typeof(ListVoiceRegionsTool),
            ["list_sticker_packs"] = typeof(ListStickerPacksTool),
            ["get_sticker_pack"] = typeof(GetStickerPackTool),
            ["list_default_soundboard_sounds"] = typeof(ListDefaultSoundboardSoundsTool),
            ["list_active_threads"] = typeof(ListActiveThreadsTool),
            ["list_archived_threads"] = typeof(ListArchivedThreadsTool),
            ["get_thread"] = typeof(GetThreadTool),
            ["list_thread_members"] = typeof(ListThreadMembersTool),
            ["get_thread_member"] = typeof(GetThreadMemberTool),
            ["list_channel_invites"] = typeof(ListChannelInvitesTool),
            ["get_member"] = typeof(GetMemberTool),
            ["search_members"] = typeof(SearchMembersTool),
            ["list_members"] = typeof(ListMembersTool),
            ["list_members_by_role"] = typeof(ListMembersByRoleTool),
            ["get_member_voice_state"] = typeof(GetMemberVoiceStateTool),
            ["get_role_member_counts"] = typeof(GetRoleMemberCountsTool),
            ["preview_member_prune"] = typeof(PreviewMemberPruneTool),
            ["list_scheduled_event_subscribers"] = typeof(ListScheduledEventSubscribersTool),
            ["get_stage_instance"] = typeof(GetStageInstanceTool),
            ["list_invite_target_users"] = typeof(ListInviteTargetUsersTool),
            ["get_invite_target_user_job_status"] = typeof(GetInviteTargetUserJobStatusTool),
            ["get_message"] = typeof(GetMessageTool),
            ["list_messages"] = typeof(ListMessagesTool),
            ["search_messages"] = typeof(SearchMessagesTool),
            ["list_channel_pins"] = typeof(ListChannelPinsTool),
            ["list_message_reactions"] = typeof(ListMessageReactionsTool),
            ["list_poll_voters"] = typeof(ListPollVotersTool),
            ["create_role"] = typeof(CreateRoleTool),
            ["update_role_appearance"] = typeof(UpdateRoleAppearanceTool),
            ["set_role_permissions"] = typeof(SetRolePermissionsTool),
            ["reorder_roles"] = typeof(ReorderRolesTool),
            ["set_member_role"] = typeof(SetMemberRoleTool),
            ["delete_role"] = typeof(DeleteRoleTool),
            ["create_category"] = typeof(CreateCategoryTool),
            ["create_text_channel"] = typeof(CreateTextChannelTool),
            ["create_announcement_channel"] = typeof(CreateAnnouncementChannelTool),
            ["create_voice_channel"] = typeof(CreateVoiceChannelTool),
            ["create_stage_channel"] = typeof(CreateStageChannelTool),
            ["create_forum_channel"] = typeof(CreateForumChannelTool),
            ["create_media_channel"] = typeof(CreateMediaChannelTool),
            ["move_channel"] = typeof(MoveChannelTool),
            ["move_channel_and_sync_permissions"] = typeof(MoveChannelAndSyncPermissionsTool),
            ["reorder_channels"] = typeof(ReorderChannelsTool),
            ["set_role_channel_permissions"] = typeof(SetRoleChannelPermissionsTool),
            ["remove_role_channel_permissions"] = typeof(RemoveRoleChannelPermissionsTool),
            ["set_member_channel_permissions"] = typeof(SetMemberChannelPermissionsTool),
            ["remove_member_channel_permissions"] = typeof(RemoveMemberChannelPermissionsTool),
            ["replace_channel_permission_overwrites"] = typeof(ReplaceChannelPermissionOverwritesTool),
            ["archive_channel"] = typeof(ArchiveChannelTool),
            ["restore_archived_channel"] = typeof(RestoreArchivedChannelTool),
            ["set_voice_channel_status"] = typeof(SetVoiceChannelStatusTool),
            ["trigger_typing_indicator"] = typeof(TriggerTypingIndicatorTool),
            ["follow_announcement_channel"] = typeof(FollowAnnouncementChannelTool),
            ["create_thread"] = typeof(CreateThreadTool),
            ["create_forum_post"] = typeof(CreateForumPostTool),
            ["update_thread"] = typeof(UpdateThreadTool),
            ["set_thread_member"] = typeof(SetThreadMemberTool),
            ["delete_thread"] = typeof(DeleteThreadTool),
            ["delete_channel"] = typeof(DeleteChannelTool),
            ["set_member_nickname"] = typeof(SetMemberNicknameTool),
            ["set_member_timeout"] = typeof(SetMemberTimeoutTool),
            ["set_member_voice_channel"] = typeof(SetMemberVoiceChannelTool),
            ["update_member_voice_state"] = typeof(UpdateMemberVoiceStateTool),
            ["set_member_verification_bypass"] = typeof(SetMemberVerificationBypassTool),
            ["kick_member"] = typeof(KickMemberTool),
            ["ban_member"] = typeof(BanMemberTool),
            ["bulk_ban_members"] = typeof(BulkBanMembersTool),
            ["unban_member"] = typeof(UnbanMemberTool),
            ["send_message"] = typeof(SendMessageTool),
            ["edit_own_message"] = typeof(EditOwnMessageTool),
            ["delete_message"] = typeof(DeleteMessageTool),
            ["bulk_delete_messages"] = typeof(BulkDeleteMessagesTool),
            ["crosspost_announcement_message"] = typeof(CrosspostAnnouncementMessageTool),
            ["set_message_pinned"] = typeof(SetMessagePinnedTool),
            ["set_own_reaction"] = typeof(SetOwnReactionTool),
            ["remove_user_reaction"] = typeof(RemoveUserReactionTool),
            ["clear_message_reactions"] = typeof(ClearMessageReactionsTool),
            ["create_poll"] = typeof(CreatePollTool),
            ["end_poll"] = typeof(EndPollTool),
            ["create_automod_rule"] = typeof(CreateAutoModRuleTool),
            ["update_automod_rule"] = typeof(UpdateAutoModRuleTool),
            ["set_automod_rule_enabled"] = typeof(SetAutoModRuleEnabledTool),
            ["delete_automod_rule"] = typeof(DeleteAutoModRuleTool),
            ["update_guild_metadata"] = typeof(UpdateGuildMetadataTool),
            ["update_guild_system_routing"] = typeof(UpdateGuildSystemRoutingTool),
            ["update_guild_safety"] = typeof(UpdateGuildSafetyTool),
            ["update_guild_access_controls"] = typeof(UpdateGuildAccessControlsTool),
            ["update_guild_widget"] = typeof(UpdateGuildWidgetTool),
            ["update_welcome_screen"] = typeof(UpdateWelcomeScreenTool),
            ["update_onboarding"] = typeof(UpdateOnboardingTool),
            ["set_incident_actions"] = typeof(SetIncidentActionsTool),
            ["create_scheduled_event"] = typeof(CreateScheduledEventTool),
            ["update_scheduled_event"] = typeof(UpdateScheduledEventTool),
            ["start_scheduled_event"] = typeof(StartScheduledEventTool),
            ["complete_scheduled_event"] = typeof(CompleteScheduledEventTool),
            ["cancel_scheduled_event"] = typeof(CancelScheduledEventTool),
            ["delete_scheduled_event"] = typeof(DeleteScheduledEventTool),
            ["start_stage_instance"] = typeof(StartStageInstanceTool),
            ["update_stage_instance"] = typeof(UpdateStageInstanceTool),
            ["end_stage_instance"] = typeof(EndStageInstanceTool),
            ["set_stage_speaker"] = typeof(SetStageSpeakerTool),
            ["request_stage_speaking"] = typeof(RequestStageSpeakingTool),
            ["create_guild_expression"] = typeof(CreateGuildExpressionTool),
            ["update_guild_expression"] = typeof(UpdateGuildExpressionTool),
            ["delete_guild_expression"] = typeof(DeleteGuildExpressionTool),
            ["create_invite"] = typeof(CreateInviteTool),
            ["create_targeted_invite"] = typeof(CreateTargetedInviteTool),
            ["update_invite_target_users"] = typeof(UpdateInviteTargetUsersTool),
            ["delete_invite"] = typeof(DeleteInviteTool),
            ["create_webhook"] = typeof(CreateWebhookTool),
            ["update_webhook"] = typeof(UpdateWebhookTool),
            ["delete_webhook"] = typeof(DeleteWebhookTool),
            ["send_webhook_message"] = typeof(SendWebhookMessageTool),
            ["edit_webhook_message"] = typeof(EditWebhookMessageTool),
            ["delete_webhook_message"] = typeof(DeleteWebhookMessageTool),
            ["create_guild_template"] = typeof(CreateGuildTemplateTool),
            ["sync_guild_template"] = typeof(SyncGuildTemplateTool),
            ["update_guild_template"] = typeof(UpdateGuildTemplateTool),
            ["delete_guild_template"] = typeof(DeleteGuildTemplateTool),
            ["update_bot_guild_profile"] = typeof(UpdateBotGuildProfileTool),
            ["update_channel"] = typeof(UpdateChannelTool)
        }.ToImmutableDictionary(StringComparer.Ordinal);

    internal static ImmutableArray<string> KnownToolNames => ToolTypes.Keys.Order(StringComparer.Ordinal).ToImmutableArray();

    public static IReadOnlyList<Type> Resolve(StewardToolManifest manifest) =>
        manifest.RegisteredNames.Select(name => ToolTypes.TryGetValue(name, out var type)
            ? type
            : throw new InvalidOperationException($"No MCP tool type is registered for {name}.")).ToArray();
}

[McpServerToolType]
public sealed class GetStewardCapabilitiesTool(StewardInfoTools tools)
{
    [McpServerTool(Name = "get_steward_capabilities", Title = "Get Discord Steward capabilities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reports the immutable startup manifest, local risk gates, required Discord permissions, and safety limits without contacting Discord or returning credentials.")]
    public StewardCapabilities GetCapabilities() => tools.GetCapabilities();
}

[McpServerToolType]
public sealed class CaptureGuildConfigurationSnapshotTool(ConfigurationSnapshotTools tools)
{
    [McpServerTool(Name = "capture_guild_configuration_snapshot", Title = "Capture guild configuration", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Reads current redacted guild configuration and persists an immutable local baseline with a content digest.")]
    public Task<ToolResultEnvelope<ConfigurationSnapshotCapture>> Capture(CancellationToken cancellationToken) =>
        tools.CaptureAsync(cancellationToken);
}

[McpServerToolType]
public sealed class CompareGuildConfigurationTool(ConfigurationSnapshotTools tools)
{
    [McpServerTool(Name = "compare_guild_configuration", Title = "Compare guild configuration", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Compares fresh redacted guild configuration with one immutable local snapshot by exact resource IDs.")]
    public Task<ToolResultEnvelope<ConfigurationComparison>> Compare(
        [Description("Exact configuration snapshot UUID.")] string snapshot_id,
        CancellationToken cancellationToken) => tools.CompareAsync(snapshot_id, cancellationToken);
}

[McpServerToolType]
public sealed class GetBotEffectivePermissionsTool(
    IDiscordBotInspector inspector,
    StewardToolManifest manifest,
    StewardRuntimeSettings settings)
{
    [McpServerTool(Name = "get_bot_effective_permissions", Title = "Get Steward bot permissions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Reads the Steward bot's current guild permissions and role ceiling, then compares them with the exact startup manifest requirements.")]
    public async Task<ToolResultEnvelope<BotPermissionComparison>> GetPermissions(
        CancellationToken cancellationToken)
    {
        var operationId = Guid.CreateVersion7().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        GuildReference? guild = settings.GuildId.HasValue
            ? new GuildReference(settings.GuildId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), Name: null)
            : null;
        try
        {
            if (!settings.IsConfigured)
            {
                throw DiscordErrorMapper.NotConfigured();
            }

            var observed = await inspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
            var actual = observed.EffectivePermissions.ToImmutableHashSet(StringComparer.Ordinal);
            var required = manifest.RequiredDiscordPermissions;
            return new ToolResultEnvelope<BotPermissionComparison>(
                1,
                operationId,
                "ok",
                new GuildReference(observed.GuildId, Name: null),
                new BotPermissionComparison(
                    observed,
                    required,
                    required.Where(permission => !actual.Contains(permission)).ToImmutableArray(),
                    observed.EffectivePermissions.Where(permission => !required.Contains(permission, StringComparer.Ordinal)).ToImmutableArray()),
                Warnings: [],
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DiscordSteward.Errors.StewardException exception)
        {
            return new ToolResultEnvelope<BotPermissionComparison>(
                1,
                operationId,
                "error",
                guild,
                Data: null,
                Warnings: [],
                new ToolError(exception.Code, exception.Message, operationId));
        }
    }
}

[McpServerToolType]
public sealed class GetGuildConfigurationSnapshotTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "get_guild_configuration_snapshot", Title = "Get guild configuration snapshot", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns a deterministic configuration snapshot for the one configured guild without reading messages.")]
    public Task<ToolResultEnvelope<GuildConfigurationSnapshotView>> GetSnapshot(CancellationToken cancellationToken) =>
        tools.GetGuildConfigurationSnapshot(cancellationToken);
}

[McpServerToolType]
public sealed class GetGuildTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "get_guild", Title = "Get guild metadata", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns bounded metadata and resource counts for the configured guild without messages or member data.")]
    public Task<ToolResultEnvelope<GuildMetadataView>> GetGuild(CancellationToken cancellationToken) =>
        tools.GetGuild(cancellationToken);
}

[McpServerToolType]
public sealed class ListRolesTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "list_roles", Title = "List guild roles", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists normalized guild roles, hierarchy, named permissions, and Steward protection without member data.")]
    public Task<ToolResultEnvelope<RoleSnapshot[]>> ListRoles(CancellationToken cancellationToken) =>
        tools.ListRoles(cancellationToken);
}

[McpServerToolType]
public sealed class ListChannelsTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "list_channels", Title = "List guild channels", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists normalized channel and category configuration with role overwrites and redacted member-overwrite counts and digests.")]
    public Task<ToolResultEnvelope<ChannelConfigurationView[]>> ListChannels(CancellationToken cancellationToken) =>
        tools.ListChannels(cancellationToken);
}

[McpServerToolType]
public sealed class GetRoleTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "get_role", Title = "Get a guild role", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one normalized role by exact Discord snowflake ID.")]
    public Task<ToolResultEnvelope<RoleSnapshot>> GetRole(
        [Description("Exact Discord role snowflake ID.")] string role_id,
        CancellationToken cancellationToken) => tools.GetRole(role_id, cancellationToken);
}

[McpServerToolType]
public sealed class GetChannelTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "get_channel", Title = "Get a guild channel", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one channel by exact ID, including the state digest required by channel mutations, without reading messages.")]
    public Task<ToolResultEnvelope<ChannelDetailSnapshot>> GetChannel(
        [Description("Exact Discord channel or category snowflake ID.")] string channel_id,
        CancellationToken cancellationToken) => tools.GetChannel(channel_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListChannelMemberOverwritesTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "list_channel_member_overwrites", Title = "List channel member overwrites", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages exact member-target permission overwrites for one channel. This sensitive read is absent unless the R2 gate is enabled.")]
    public Task<ToolResultEnvelope<MemberPermissionOverwritePage>> ListMemberOverwrites(
    [Description("Exact Discord channel or category snowflake ID.")] string channel_id,
    CancellationToken cancellationToken,
    [Description("Optional exact member snowflake cursor.")] string? after_member_id = null,
    [Description("Maximum results from 1 to 100.")] int limit = 100) =>
    tools.ListChannelMemberOverwrites(channel_id, limit, after_member_id, cancellationToken);
}

[McpServerToolType]
public sealed class LintGuildConfigurationTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "lint_guild_configuration", Title = "Lint guild configuration", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Runs deterministic safety checks over a fresh guild configuration snapshot without changing Discord.")]
    public Task<ToolResultEnvelope<GuildLintReport>> Lint(CancellationToken cancellationToken) =>
        tools.LintGuildConfiguration(cancellationToken);
}

[McpServerToolType]
public sealed class ExplainRolePermissionsTool(DiscordReadTools tools)
{
    [McpServerTool(Name = "explain_role_permissions", Title = "Explain role permissions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Explains guild and optional channel permissions for one exact role without reading members or messages.")]
    public Task<ToolResultEnvelope<RolePermissionExplanation>> Explain(
        [Description("Exact Discord role snowflake ID.")] string role_id,
        CancellationToken cancellationToken,
        [Description("Optional exact channel or category snowflake ID.")] string? channel_id = null) =>
        tools.ExplainRolePermissions(role_id, cancellationToken, channel_id);
}

[McpServerToolType]
public sealed class AnalyzeRoleHierarchyTool(AnalysisTools tools)
{
    [McpServerTool(Name = "analyze_role_hierarchy", Title = "Analyze role hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Explains which exact roles the Steward bot could manage from current permissions, hierarchy, managed state, and local protection without reading members.")]
    public Task<ToolResultEnvelope<RoleHierarchyReport>> Analyze(CancellationToken cancellationToken) =>
        tools.AnalyzeRoleHierarchyAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetOperationTool(DirectOperationTools tools)
{
    [McpServerTool(Name = "get_operation", Title = "Get operation evidence", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns one durable direct-operation record by globally unique request ID.")]
    public Task<ToolResultEnvelope<OperationRecord>> GetOperation(
        [Description("Globally unique operation UUID.")] string request_id,
        CancellationToken cancellationToken) => tools.GetOperationAsync(request_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListOperationsTool(DirectOperationTools tools)
{
    [McpServerTool(Name = "list_operations", Title = "List operation evidence", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns bounded redacted summaries of recent direct operations.")]
    public Task<ToolResultEnvelope<OperationSummary[]>> ListOperations(
        CancellationToken cancellationToken,
        [Description("Maximum results from 1 to 100.")] int limit = 20) =>
        tools.ListOperationsAsync(limit, cancellationToken);
}

[McpServerToolType]
public sealed class ReconcileOperationTool(DirectOperationTools tools)
{
    [McpServerTool(Name = "reconcile_operation", Title = "Reconcile uncertain operation", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Reads current Discord state to resolve one stale or uncertain operation. It never repeats the write.")]
    public Task<OperationToolResultEnvelope<OperationReconciliationResult>> Reconcile(
        [Description("Globally unique operation UUID.")] string request_id,
        CancellationToken cancellationToken) => tools.ReconcileOperationAsync(request_id, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_channel", Title = "Update one channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates typed fields valid for one exact guild channel after validating its prior state digest. The complete approved call is reserved, written at most once, and verified.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> UpdateChannel(
        [Description("Globally unique caller-generated UUID for idempotency and correlation.")] string request_id,
        [Description("Exact Discord guild-channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent from 1 to 500 characters.")] string reason,
        [Description("Lowercase SHA-256 mutationStateDigest returned by get_channel.")] string expected_state_digest,
        [Description("Typed channel fields to set. Omitted fields remain unchanged.")] ChannelUpdateSet set,
        CancellationToken cancellationToken,
        [Description("Optional nullable field names to clear explicitly.")] string[]? clear = null) =>
        tools.ExecuteAsync(
            "update_channel",
            request_id,
            channel_id,
            reason,
            new UpdateChannelInput(
                expected_state_digest,
                new UpdateChannelSetInput(
                    set.Name,
                    set.ChannelType,
                    set.Topic,
                    set.IsNsfw,
                    set.SlowModeSeconds,
                    set.Bitrate,
                    set.UserLimit,
                    set.RtcRegion,
                    set.VideoQualityMode,
                    set.DefaultAutoArchiveMinutes,
                    set.DefaultThreadSlowModeSeconds,
                    set.AvailableTags,
                    set.DefaultReaction,
                    set.DefaultSortOrder,
                    set.DefaultForumLayout,
                    set.RequireTag,
                    set.HideMediaDownloadOptions,
                    set.IsSpoiler),
                clear?.ToImmutableArray() ?? []),
            cancellationToken);
}
