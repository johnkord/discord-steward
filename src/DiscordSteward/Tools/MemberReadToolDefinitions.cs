using System.ComponentModel;
using DiscordSteward.Discord;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class GetMemberTool(MemberReadTools tools)
{
    [McpServerTool(Name = "get_member", Title = "Get guild member", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one minimized guild member record by exact user ID, including roles, timeout, flags, and current voice state.")]
    public Task<ToolResultEnvelope<MemberView>> Get(
        [Description("Exact Discord user snowflake ID.")] string user_id,
        CancellationToken cancellationToken) => tools.GetMemberAsync(user_id, cancellationToken);
}

[McpServerToolType]
public sealed class SearchMembersTool(MemberReadTools tools)
{
    [McpServerTool(Name = "search_members", Title = "Search guild members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Prefix-searches bounded guild members by username or nickname. Returned IDs, not names, are identity.")]
    public Task<ToolResultEnvelope<MemberPage>> Search(
        [Description("Printable username or nickname prefix.")] string query,
        CancellationToken cancellationToken,
        [Description("Maximum results from 1 to 1000.")] int limit = 100) =>
        tools.SearchMembersAsync(query, limit, cancellationToken);
}

[McpServerToolType]
public sealed class ListMembersTool(MemberReadTools tools)
{
    [McpServerTool(Name = "list_members", Title = "List guild members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages minimized guild-member records in exact user-ID order.")]
    public Task<ToolResultEnvelope<MemberPage>> List(
        CancellationToken cancellationToken,
        [Description("Maximum results from 1 to 1000.")] int limit = 100,
        [Description("Optional exact user snowflake cursor.")] string? after_user_id = null) =>
        tools.ListMembersAsync(limit, after_user_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListMembersByRoleTool(MemberReadTools tools)
{
    [McpServerTool(Name = "list_members_by_role", Title = "List members by role", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages minimized members assigned one exact role.")]
    public Task<ToolResultEnvelope<MemberPage>> List(
        [Description("Exact Discord role snowflake ID.")] string role_id,
        CancellationToken cancellationToken,
        [Description("Maximum results from 1 to 1000.")] int limit = 100,
        [Description("Optional exact user snowflake cursor.")] string? after_user_id = null) =>
        tools.ListMembersByRoleAsync(role_id, limit, after_user_id, cancellationToken);
}

[McpServerToolType]
public sealed class GetMemberVoiceStateTool(MemberReadTools tools)
{
    [McpServerTool(Name = "get_member_voice_state", Title = "Get member voice state", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one exact member's current guild voice-state metadata.")]
    public Task<ToolResultEnvelope<MemberVoiceStateView>> Get(
        [Description("Exact Discord user snowflake ID.")] string user_id,
        CancellationToken cancellationToken) => tools.GetMemberVoiceStateAsync(user_id, cancellationToken);
}

[McpServerToolType]
public sealed class GetRoleMemberCountsTool(MemberReadTools tools)
{
    [McpServerTool(Name = "get_role_member_counts", Title = "Get role member counts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns Discord's aggregate role member counts without listing member identities.")]
    public Task<ToolResultEnvelope<RoleMemberCountView[]>> Get(CancellationToken cancellationToken) =>
        tools.GetRoleMemberCountsAsync(cancellationToken);
}

[McpServerToolType]
public sealed class PreviewMemberPruneTool(MemberReadTools tools)
{
    [McpServerTool(Name = "preview_member_prune", Title = "Preview member prune", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns only Discord's current prune count for explicit inactivity days and included roles. Steward does not expose the dynamic-target prune write.")]
    public Task<ToolResultEnvelope<MemberPrunePreview>> Preview(
        [Description("Inactivity threshold from 1 to 30 days.")] int inactivity_days,
        CancellationToken cancellationToken,
        [Description("Optional exact role IDs to include, at most 100.")] string[]? role_ids = null) =>
        tools.PreviewMemberPruneAsync(inactivity_days, role_ids ?? [], cancellationToken);
}
