using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class SetMemberNicknameTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_member_nickname", Title = "Set member nickname", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sets or clears one exact member nickname after exact state and hierarchy checks.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact member user snowflake ID.")] string user_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound nickname input; null clears the nickname.")] SetMemberNicknameInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_member_nickname", request_id, user_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetMemberTimeoutTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_member_timeout", Title = "Set member timeout", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sets an exact timeout up to 28 days or clears it explicitly after hierarchy and Administrator checks.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact member user snowflake ID.")] string user_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound timeout input; null clears timeout.")] SetMemberTimeoutInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_member_timeout", request_id, user_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetMemberVoiceChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_member_voice_channel", Title = "Set member voice channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Moves one connected member to one exact guild voice/stage channel or disconnects them explicitly.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact member user snowflake ID.")] string user_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound voice destination; null disconnects.")] SetMemberVoiceChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_member_voice_channel", request_id, user_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateMemberVoiceStateTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_member_voice_state", Title = "Update member voice state", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Explicitly sets server mute and/or deafen for one exact connected member.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact member user snowflake ID.")] string user_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound explicit server mute/deafen input.")] UpdateMemberVoiceStateInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_member_voice_state", request_id, user_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetMemberVerificationBypassTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_member_verification_bypass", Title = "Set verification bypass", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sets the membership-screening bypass bit while preserving every unrelated member flag.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact member user snowflake ID.")] string user_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound explicit verification-bypass input.")] SetMemberVerificationBypassInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_member_verification_bypass", request_id, user_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class KickMemberTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "kick_member", Title = "Kick member", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Removes one exact member after current-state, owner, self, protection, and hierarchy checks.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Kick(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact member user snowflake ID.")] string user_id,
        [Description("Mandatory printable audit reason.")] string reason,
        [Description("Exact member-state evidence.")] KickMemberInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("kick_member", request_id, user_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class BanMemberTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "ban_member", Title = "Ban member", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Bans one exact user with zero history deletion; current members require an exact state digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Ban(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact user snowflake ID.")] string user_id,
        [Description("Mandatory printable audit reason.")] string reason,
        [Description("Optional current-member state digest; null only when the user is not a guild member.")] BanMemberInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("ban_member", request_id, user_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class BulkBanMembersTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "bulk_ban_members", Title = "Bulk ban exact members", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Bans 2 to 25 explicit unique user IDs in one request with zero history deletion and exact state checks for current members.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Ban(
    [Description("Globally unique operation UUID.")] string request_id,
    [Description("Mandatory printable audit reason.")] string reason,
    [Description("Complete bounded target list with state evidence for current members.")] BulkBanMembersInput input,
    CancellationToken cancellationToken) =>
    tools.ExecuteForGuildAsync("bulk_ban_members", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UnbanMemberTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "unban_member", Title = "Unban member", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Removes one exact current ban after validating the digest returned by get_ban.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Unban(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact banned user snowflake ID.")] string user_id,
        [Description("Mandatory printable audit reason.")] string reason,
        [Description("Exact current ban-state evidence.")] UnbanMemberInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("unban_member", request_id, user_id, reason, input, cancellationToken);
}
