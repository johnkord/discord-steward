using System.ComponentModel;
using DiscordSteward.Discord;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class ListAutoModRulesTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_automod_rules", Title = "List AutoMod rules", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists normalized AutoMod rules and exemptions while omitting creator identity.")]
    public Task<ToolResultEnvelope<AutoModRuleListView>> List(CancellationToken cancellationToken) => tools.ListAutoModRulesAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetAutoModRuleTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_automod_rule", Title = "Get AutoMod rule", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one normalized AutoMod rule by exact snowflake ID while omitting creator identity.")]
    public Task<ToolResultEnvelope<AutoModRuleView>> Get(
        [Description("Exact AutoMod rule snowflake ID.")] string rule_id,
        CancellationToken cancellationToken) => tools.GetAutoModRuleAsync(rule_id, cancellationToken);
}

[McpServerToolType]
public sealed class GetOnboardingTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_onboarding", Title = "Get guild onboarding", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns onboarding prompts, options, exact role/channel grants, mode, and enablement.")]
    public Task<ToolResultEnvelope<OnboardingView>> Get(CancellationToken cancellationToken) => tools.GetOnboardingAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetWelcomeScreenTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_welcome_screen", Title = "Get welcome screen", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns the configured welcome-screen description and exact channel entries.")]
    public Task<ToolResultEnvelope<WelcomeScreenView>> Get(CancellationToken cancellationToken) => tools.GetWelcomeScreenAsync(cancellationToken);
}

[McpServerToolType]
public sealed class ListScheduledEventsTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_scheduled_events", Title = "List scheduled events", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists scheduled and active event configuration while omitting creator identities and subscriber counts.")]
    public Task<ToolResultEnvelope<ScheduledEventListView>> List(CancellationToken cancellationToken) => tools.ListScheduledEventsAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetScheduledEventTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_scheduled_event", Title = "Get scheduled event", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one scheduled event's configuration by exact snowflake ID without creator identity or subscriber count.")]
    public Task<ToolResultEnvelope<ScheduledEventView>> Get(
        [Description("Exact scheduled-event snowflake ID.")] string event_id,
        CancellationToken cancellationToken) => tools.GetScheduledEventAsync(event_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListBansTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_bans", Title = "List guild bans", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages bounded ban records and safe reasons without unrelated user profile data.")]
    public Task<ToolResultEnvelope<BanView[]>> List(
        CancellationToken cancellationToken,
        [Description("Maximum records from 1 to 1000.")] int limit = 100,
        [Description("Optional exact user snowflake cursor.")] string? after_user_id = null) =>
        tools.ListBansAsync(limit, after_user_id, cancellationToken);
}

[McpServerToolType]
public sealed class GetBanTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_ban", Title = "Get guild ban", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns ban state for one exact user ID.")]
    public Task<ToolResultEnvelope<BanView>> Get(
        [Description("Exact Discord user snowflake ID.")] string user_id,
        CancellationToken cancellationToken) => tools.GetBanAsync(user_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListInvitesTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_invites", Title = "List guild invites", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists bounded invite metadata and exact invite codes for the configured guild.")]
    public Task<ToolResultEnvelope<InviteView[]>> List(CancellationToken cancellationToken) => tools.ListInvitesAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetInviteTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_invite", Title = "Get guild invite", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one configured-guild invite by exact shareable code.")]
    public Task<ToolResultEnvelope<InviteView>> Get(
        [Description("Exact invite code.")] string code,
        CancellationToken cancellationToken) => tools.GetInviteAsync(code, cancellationToken);
}

[McpServerToolType]
public sealed class ListIntegrationsTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_integrations", Title = "List guild integrations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists sanitized integration metadata. Tokens and executable URLs are never returned.")]
    public Task<ToolResultEnvelope<IntegrationView[]>> List(CancellationToken cancellationToken) => tools.ListIntegrationsAsync(cancellationToken);
}

[McpServerToolType]
public sealed class ListWebhooksTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_webhooks", Title = "List guild webhooks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists sanitized webhook metadata. Tokens, creators, and executable URLs are omitted.")]
    public Task<ToolResultEnvelope<WebhookView[]>> List(CancellationToken cancellationToken) => tools.ListWebhooksAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetWebhookTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_webhook", Title = "Get guild webhook", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one sanitized webhook by exact snowflake ID without token or creator identity.")]
    public Task<ToolResultEnvelope<WebhookView>> Get(
        [Description("Exact webhook snowflake ID.")] string webhook_id,
        CancellationToken cancellationToken) => tools.GetWebhookAsync(webhook_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListAuditEventsTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_audit_events", Title = "List audit events", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages up to 100 audit events with optional exact actor, action, and cursor filters.")]
    public Task<ToolResultEnvelope<AuditEventView[]>> List(
        CancellationToken cancellationToken,
        [Description("Maximum records from 1 to 100.")] int limit = 100,
        [Description("Optional exact audit-entry snowflake cursor.")] string? before_id = null,
        [Description("Optional exact actor user snowflake ID.")] string? actor_user_id = null,
        [Description("Optional Discord ActionType enum name.")] string? action = null) =>
        tools.ListAuditEventsAsync(limit, before_id, actor_user_id, action, cancellationToken);
}

[McpServerToolType]
public sealed class ListGuildExpressionsTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_guild_expressions", Title = "List guild expressions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists sanitized emoji, sticker, or soundboard metadata by explicit expression type.")]
    public Task<ToolResultEnvelope<GuildExpressionListView>> List(
        [Description("Expression type: emoji, sticker, or soundboard.")] string expression_type,
        CancellationToken cancellationToken) => tools.ListGuildExpressionsAsync(expression_type, cancellationToken);
}

[McpServerToolType]
public sealed class GetGuildExpressionTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_guild_expression", Title = "Get guild expression", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one sanitized guild expression by exact type and snowflake ID.")]
    public Task<ToolResultEnvelope<GuildExpressionView>> Get(
        [Description("Expression type: emoji, sticker, or soundboard.")] string expression_type,
        [Description("Exact expression snowflake ID.")] string expression_id,
        CancellationToken cancellationToken) => tools.GetGuildExpressionAsync(expression_type, expression_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListActiveThreadsTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_active_threads", Title = "List active threads", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists active public, announcement, private, forum, and media thread metadata visible to the bot.")]
    public Task<ToolResultEnvelope<ThreadView[]>> List(CancellationToken cancellationToken) => tools.ListActiveThreadsAsync(cancellationToken);
}

[McpServerToolType]
public sealed class ListArchivedThreadsTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_archived_threads", Title = "List archived threads", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages archived public, private, or joined-private threads for one exact parent channel.")]
    public Task<ToolResultEnvelope<ThreadView[]>> List(
        [Description("Exact parent channel snowflake ID.")] string parent_channel_id,
        [Description("Archive type: public, private, or joined_private.")] string archive_type,
        CancellationToken cancellationToken,
        [Description("Maximum records from 1 to 100.")] int limit = 100,
        [Description("Optional archive timestamp cursor.")] DateTimeOffset? before = null) =>
        tools.ListArchivedThreadsAsync(parent_channel_id, archive_type, limit, before, cancellationToken);
}

[McpServerToolType]
public sealed class GetThreadTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_thread", Title = "Get thread", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one exact thread and content-adjacent metadata without message bodies.")]
    public Task<ToolResultEnvelope<ThreadView>> Get(
        [Description("Exact thread snowflake ID.")] string thread_id,
        CancellationToken cancellationToken) => tools.GetThreadAsync(thread_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListVoiceRegionsTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_voice_regions", Title = "List voice regions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists global or configured-guild voice regions that can be used as an explicit channel RTC region.")]
    public Task<ToolResultEnvelope<VoiceRegionView[]>> List(
        [Description("Region scope: global or guild.")] string scope,
        CancellationToken cancellationToken) => tools.ListVoiceRegionsAsync(scope, cancellationToken);
}

[McpServerToolType]
public sealed class GetThreadMemberTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_thread_member", Title = "Get thread member", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns exact membership metadata for one exact user in one exact thread without unrelated member profile data.")]
    public Task<ToolResultEnvelope<ThreadMemberView>> Get(
        [Description("Exact thread snowflake ID.")] string thread_id,
        [Description("Exact user snowflake ID.")] string user_id,
        CancellationToken cancellationToken) => tools.GetThreadMemberAsync(thread_id, user_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListChannelInvitesTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_channel_invites", Title = "List channel invites", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists sanitized invite metadata and exact codes for one exact guild channel while omitting creator identity.")]
    public Task<ToolResultEnvelope<InviteView[]>> List(
        [Description("Exact guild-channel snowflake ID.")] string channel_id,
        CancellationToken cancellationToken) => tools.ListChannelInvitesAsync(channel_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListStickerPacksTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_sticker_packs", Title = "List standard sticker packs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists Discord's public standard sticker packs and bounded sticker metadata.")]
    public Task<ToolResultEnvelope<StickerPackView[]>> List(CancellationToken cancellationToken) =>
        tools.ListStickerPacksAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetStickerPackTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_sticker_pack", Title = "Get standard sticker pack", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one public standard sticker pack by exact snowflake ID.")]
    public Task<ToolResultEnvelope<StickerPackView>> Get(
        [Description("Exact standard sticker-pack snowflake ID.")] string pack_id,
        CancellationToken cancellationToken) => tools.GetStickerPackAsync(pack_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListDefaultSoundboardSoundsTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_default_soundboard_sounds", Title = "List default soundboard sounds", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists Discord's public default soundboard sound metadata without downloading audio.")]
    public Task<ToolResultEnvelope<DefaultSoundboardSoundView[]>> List(CancellationToken cancellationToken) =>
        tools.ListDefaultSoundboardSoundsAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetGuildPreviewTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_guild_preview", Title = "Get guild preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns the configured guild's public preview metadata and aggregate expression counts.")]
    public Task<ToolResultEnvelope<GuildPreviewView>> Get(CancellationToken cancellationToken) =>
        tools.GetGuildPreviewAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetGuildWidgetImageTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_guild_widget_image", Title = "Get guild widget image", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns a bounded Discord-rendered widget PNG with media type, digest, byte length, and base64 content.")]
    public Task<ToolResultEnvelope<GuildWidgetImageView>> Get(
        CancellationToken cancellationToken,
        [Description("Widget style: shield or banner1 through banner4.")] string style = "shield") =>
        tools.GetGuildWidgetImageAsync(style, cancellationToken);
}

[McpServerToolType]
public sealed class GetBotGuildProfileTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_bot_guild_profile", Title = "Get bot guild profile", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns Steward's guild-specific nickname, avatar hash, banner hash, bio, and exact state digest.")]
    public Task<ToolResultEnvelope<BotGuildProfileView>> Get(CancellationToken cancellationToken) =>
    tools.GetBotGuildProfileAsync(cancellationToken);
}
