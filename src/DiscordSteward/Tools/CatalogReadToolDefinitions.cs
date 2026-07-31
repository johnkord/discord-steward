using System.ComponentModel;
using DiscordSteward.Discord;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class GetGuildWidgetTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_guild_widget", Title = "Get guild widget", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns widget enablement and its exact target channel for the configured guild.")]
    public Task<ToolResultEnvelope<GuildWidgetView>> Get(CancellationToken cancellationToken) =>
        tools.GetGuildWidgetAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetIncidentStatusTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_incident_status", Title = "Get incident status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns raid detection and temporary invite or direct-message disablement timestamps.")]
    public Task<ToolResultEnvelope<IncidentStatusView>> Get(CancellationToken cancellationToken) =>
        tools.GetIncidentStatusAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetGuildVanityUrlTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_guild_vanity_url", Title = "Get guild vanity URL", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns the configured guild's vanity code and usage count when available.")]
    public Task<ToolResultEnvelope<GuildVanityUrlView>> Get(CancellationToken cancellationToken) =>
        tools.GetGuildVanityUrlAsync(cancellationToken);
}

[McpServerToolType]
public sealed class ListGuildTemplatesTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_guild_templates", Title = "List guild templates", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Lists sanitized templates sourced from the configured guild without creator identity.")]
    public Task<ToolResultEnvelope<GuildTemplateView[]>> List(CancellationToken cancellationToken) =>
        tools.ListGuildTemplatesAsync(cancellationToken);
}

[McpServerToolType]
public sealed class GetGuildTemplateTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_guild_template", Title = "Get guild template", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one sanitized configured-guild template by exact shareable code.")]
    public Task<ToolResultEnvelope<GuildTemplateView>> Get(
        [Description("Exact guild template code.")] string code,
        CancellationToken cancellationToken) => tools.GetGuildTemplateAsync(code, cancellationToken);
}

[McpServerToolType]
public sealed class ListThreadMembersTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_thread_members", Title = "List thread members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages exact member IDs for one exact thread under the sensitive-read gate.")]
    public Task<ToolResultEnvelope<ThreadMemberPage>> List(
        [Description("Exact thread snowflake ID.")] string thread_id,
        CancellationToken cancellationToken,
        [Description("Maximum results from 1 to 100.")] int limit = 100,
        [Description("Optional exact user snowflake cursor.")] string? after_user_id = null) =>
        tools.ListThreadMembersAsync(thread_id, limit, after_user_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListScheduledEventSubscribersTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_scheduled_event_subscribers", Title = "List scheduled event subscribers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages exact users subscribed to one exact scheduled event.")]
    public Task<ToolResultEnvelope<ScheduledEventSubscriberPage>> List(
        [Description("Exact scheduled-event snowflake ID.")] string event_id,
        CancellationToken cancellationToken,
        [Description("Maximum results from 1 to 100.")] int limit = 100,
        [Description("Optional exact user snowflake cursor.")] string? after_user_id = null) =>
        tools.ListScheduledEventSubscribersAsync(event_id, limit, after_user_id, cancellationToken);
}

[McpServerToolType]
public sealed class GetStageInstanceTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_stage_instance", Title = "Get stage instance", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns the live stage instance for one exact stage channel.")]
    public Task<ToolResultEnvelope<StageInstanceView>> Get(
        [Description("Exact stage channel snowflake ID.")] string channel_id,
        CancellationToken cancellationToken) => tools.GetStageInstanceAsync(channel_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListInviteTargetUsersTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "list_invite_target_users", Title = "List invite target users", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Parses a bounded targeted-invite CSV into exact user IDs and a canonical state digest.")]
    public Task<ToolResultEnvelope<InviteTargetUsersView>> List(
        [Description("Exact invite code.")] string code,
        CancellationToken cancellationToken) => tools.ListInviteTargetUsersAsync(code, cancellationToken);
}

[McpServerToolType]
public sealed class GetInviteTargetUserJobStatusTool(ExtendedReadTools tools)
{
    [McpServerTool(Name = "get_invite_target_user_job_status", Title = "Get invite target-user job", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Reads one invite's asynchronous target-user job only when bound to its originating request ID.")]
    public Task<ToolResultEnvelope<InviteTargetUserJobView>> Get(
        [Description("Exact invite code.")] string code,
        [Description("Originating create or update operation UUID.")] string request_id,
        CancellationToken cancellationToken) =>
        tools.GetInviteTargetUserJobStatusAsync(code, request_id, cancellationToken);
}
