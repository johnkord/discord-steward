using System.ComponentModel;
using DiscordSteward.Discord;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class GetMessageTool(MessageReadTools tools)
{
    [McpServerTool(Name = "get_message", Title = "Get message", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns one exact guild-channel message with bounded content and attachment metadata without download URLs.")]
    public Task<ToolResultEnvelope<MessageView>> Get(
        [Description("Exact guild channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        CancellationToken cancellationToken) => tools.GetMessageAsync(channel_id, message_id, cancellationToken);
}

[McpServerToolType]
public sealed class ListMessagesTool(MessageReadTools tools)
{
    [McpServerTool(Name = "list_messages", Title = "List messages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages a bounded message window in one exact guild channel.")]
    public Task<ToolResultEnvelope<MessagePage>> List(
        [Description("Exact guild channel snowflake ID.")] string channel_id,
        CancellationToken cancellationToken,
        [Description("Maximum messages from 1 to 100.")] int limit = 50,
        [Description("Optional message ID cursor; mutually exclusive with after and around.")] string? before_message_id = null,
        [Description("Optional message ID cursor; mutually exclusive with before and around.")] string? after_message_id = null,
        [Description("Optional message ID cursor; mutually exclusive with before and after.")] string? around_message_id = null) =>
        tools.ListMessagesAsync(
            channel_id,
            limit,
            before_message_id,
            after_message_id,
            around_message_id,
            cancellationToken);
}

[McpServerToolType]
public sealed class SearchMessagesTool(MessageReadTools tools)
{
    [McpServerTool(Name = "search_messages", Title = "Search messages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Searches the configured guild with explicit typed filters and never retries Discord indexing jobs automatically.")]
    public Task<ToolResultEnvelope<MessageSearchResult>> Search(
        CancellationToken cancellationToken,
        [Description("Optional content query, maximum 1024 characters.")] string? content = null,
        [Description("Optional exact channel IDs, maximum 500.")] string[]? channel_ids = null,
        [Description("Optional exact author user IDs, maximum 100.")] string[]? author_ids = null,
        [Description("Optional exact mentioned user IDs, maximum 100.")] string[]? mentioned_user_ids = null,
        [Description("Optional exact mentioned role IDs, maximum 100.")] string[]? mentioned_role_ids = null,
        [Description("Optional documented has filters such as image, file, link, poll, or snapshot.")] string[]? has = null,
        [Description("Optional pinned-state filter.")] bool? pinned = null,
        [Description("Optional lower message ID bound.")] string? minimum_message_id = null,
        [Description("Optional upper message ID bound.")] string? maximum_message_id = null,
        [Description("Sort mode: timestamp or relevance.")] string sort_by = "timestamp",
        [Description("Sort order: asc or desc.")] string sort_order = "desc",
        [Description("Whether to include age-restricted channels.")] bool include_nsfw = false,
        [Description("Maximum results from 1 to 25.")] int limit = 25,
        [Description("Result offset from 0 to 9975.")] int offset = 0) =>
        tools.SearchMessagesAsync(
            content,
            channel_ids,
            author_ids,
            mentioned_user_ids,
            mentioned_role_ids,
            has,
            pinned,
            minimum_message_id,
            maximum_message_id,
            sort_by,
            sort_order,
            include_nsfw,
            limit,
            offset,
            cancellationToken);
}

[McpServerToolType]
public sealed class ListChannelPinsTool(MessageReadTools tools)
{
    [McpServerTool(Name = "list_channel_pins", Title = "List channel pins", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages current pins in one exact guild channel using Discord's timestamp cursor.")]
    public Task<ToolResultEnvelope<MessagePinPage>> List(
        [Description("Exact guild channel snowflake ID.")] string channel_id,
        CancellationToken cancellationToken,
        [Description("Maximum pins from 1 to 50.")] int limit = 50,
        [Description("Optional pinned-at timestamp cursor.")] DateTimeOffset? before = null) =>
        tools.ListChannelPinsAsync(channel_id, limit, before, cancellationToken);
}

[McpServerToolType]
public sealed class ListMessageReactionsTool(MessageReadTools tools)
{
    [McpServerTool(Name = "list_message_reactions", Title = "List message reactions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages exact users for one Unicode or custom emoji reaction on one exact message.")]
    public Task<ToolResultEnvelope<MessageReactionUserPage>> List(
        [Description("Exact guild channel snowflake ID.")] string channel_id,
        [Description("Exact message snowflake ID.")] string message_id,
        [Description("Unicode emoji or custom name:id value.")] string emoji,
        CancellationToken cancellationToken,
        [Description("Maximum users from 1 to 100.")] int limit = 25,
        [Description("Optional exact user snowflake cursor.")] string? after_user_id = null,
        [Description("Reaction type: normal or burst.")] string reaction_type = "normal") =>
        tools.ListMessageReactionsAsync(
            channel_id,
            message_id,
            emoji,
            limit,
            after_user_id,
            reaction_type,
            cancellationToken);
}

[McpServerToolType]
public sealed class ListPollVotersTool(MessageReadTools tools)
{
    [McpServerTool(Name = "list_poll_voters", Title = "List poll voters", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Pages exact voters for one answer of one exact poll message.")]
    public Task<ToolResultEnvelope<PollVoterPage>> List(
        [Description("Exact guild channel snowflake ID.")] string channel_id,
        [Description("Exact poll message snowflake ID.")] string message_id,
        [Description("Positive poll answer ID.")] int answer_id,
        CancellationToken cancellationToken,
        [Description("Maximum users from 1 to 100.")] int limit = 25,
        [Description("Optional exact user snowflake cursor.")] string? after_user_id = null) =>
        tools.ListPollVotersAsync(
            channel_id,
            message_id,
            answer_id,
            limit,
            after_user_id,
            cancellationToken);
}
