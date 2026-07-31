using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class SendMessageTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "send_message", Title = "Send message", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sends, replies, or forwards with enforced nonce, explicit allowed mentions, source digest, and approved assets.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Send(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact destination channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete channel/source/mention/asset/nonce input.")] SendMessageInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("send_message", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class EditOwnMessageTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "edit_own_message", Title = "Edit Steward message", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Edits one Steward-authored message with an exact retained-attachment set and approved new assets.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Edit(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete digest-bound edit and attachment-retention input.")] EditOwnMessageInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "edit_own_message",
            request_id,
            MessageMutationHandler.MessageResourceId(channel_id, message_id),
            reason,
            input,
            cancellationToken);
}

[McpServerToolType]
public sealed class DeleteMessageTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_message", Title = "Delete message", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact message after validating its full state digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact message-state evidence.")] DeleteMessageInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "delete_message",
            request_id,
            MessageMutationHandler.MessageResourceId(channel_id, message_id),
            reason,
            input,
            cancellationToken);
}

[McpServerToolType]
public sealed class BulkDeleteMessagesTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "bulk_delete_messages", Title = "Bulk delete messages", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes 2 to 100 exact digest-bound messages, all younger than 14 days.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact common channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete exact message IDs and state digests.")] BulkDeleteMessagesInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("bulk_delete_messages", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CrosspostAnnouncementMessageTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "crosspost_announcement_message", Title = "Crosspost announcement", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Crossposts one existing exact announcement message to its followers.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Crosspost(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact announcement channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact message-state evidence.")] CrosspostAnnouncementMessageInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "crosspost_announcement_message",
            request_id,
            MessageMutationHandler.MessageResourceId(channel_id, message_id),
            reason,
            input,
            cancellationToken);
}

[McpServerToolType]
public sealed class SetMessagePinnedTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_message_pinned", Title = "Set message pinned", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pins or unpins one exact digest-bound message using an explicit pinned value.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact message-state and desired pin value.")] SetMessagePinnedInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "set_message_pinned",
            request_id,
            MessageMutationHandler.MessageResourceId(channel_id, message_id),
            reason,
            input,
            cancellationToken);
}

[McpServerToolType]
public sealed class SetOwnReactionTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_own_reaction", Title = "Set own reaction", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Adds or removes Steward's exact Unicode or custom emoji reaction using an explicit present value.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound exact emoji and desired presence.")] SetOwnReactionInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "set_own_reaction",
            request_id,
            MessageMutationHandler.MessageResourceId(channel_id, message_id),
            reason,
            input,
            cancellationToken);
}

[McpServerToolType]
public sealed class RemoveUserReactionTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "remove_user_reaction", Title = "Remove user reaction", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Removes one exact user's exact reaction after message and reaction-state validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Remove(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound exact user and emoji reaction.")] RemoveUserReactionInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "remove_user_reaction",
            request_id,
            MessageMutationHandler.MessageResourceId(channel_id, message_id),
            reason,
            input,
            cancellationToken);
}

[McpServerToolType]
public sealed class ClearMessageReactionsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "clear_message_reactions", Title = "Clear message reactions", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Clears every reaction or every use of one exact emoji from one exact message.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Clear(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound clear-all or exact-emoji input.")] ClearMessageReactionsInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "clear_message_reactions",
            request_id,
            MessageMutationHandler.MessageResourceId(channel_id, message_id),
            reason,
            input,
            cancellationToken);
}

[McpServerToolType]
public sealed class CreatePollTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_poll", Title = "Create poll", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates an explicit 2 to 10 answer poll with enforced nonce and allowed mentions.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact destination channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete channel-bound poll, nonce, and mention input.")] CreatePollInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("create_poll", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class EndPollTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "end_poll", Title = "End poll", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Immediately finalizes one exact Steward-authored poll after message and poll digest validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> End(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Exact poll message snowflake ID.")] string message_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact message and poll-state evidence.")] EndPollInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "end_poll",
            request_id,
            MessageMutationHandler.MessageResourceId(channel_id, message_id),
            reason,
            input,
            cancellationToken);
}
