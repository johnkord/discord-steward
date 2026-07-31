using System.Collections.Immutable;
using DiscordSteward.Operations;

namespace DiscordSteward.Discord;

public sealed record MessageAttachmentView(
    string Id,
    string Filename,
    string? Description,
    string? ContentType,
    long SizeBytes,
    bool IsEphemeral,
    bool IsSpoiler = false,
    double? DurationSeconds = null,
    string? Waveform = null);

public sealed record MessagePollAnswerView(
    int AnswerId,
    string Text,
    string? Emoji,
    int VoteCount,
    bool DidSelfVote);

public sealed record MessagePollView(
    string Question,
    ImmutableArray<MessagePollAnswerView> Answers,
    DateTimeOffset? ExpiresAt,
    bool AllowsMultiselect,
    bool IsFinalized,
    string StateDigest);

public sealed record MessageView(
    string Id,
    string ChannelId,
    string AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool IsPinned,
    int Type,
    long Flags,
    ImmutableArray<string> MentionedUserIds,
    ImmutableArray<string> MentionedRoleIds,
    ImmutableArray<MessageAttachmentView> Attachments,
    string StateDigest,
    string ReactionStateDigest,
    MessagePollView? Poll,
    bool IsTts,
    ImmutableArray<string> StickerIds,
    string EmbedsJson,
    string ComponentsJson,
    SharedClientThemeInput? SharedClientTheme,
    string? AuthorAvatarHash)
{
    public MessageView(
        string id,
        string channelId,
        string authorId,
        string authorName,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset? editedAt,
        bool isPinned,
        int type,
        long flags,
        ImmutableArray<string> mentionedUserIds,
        ImmutableArray<string> mentionedRoleIds,
        ImmutableArray<MessageAttachmentView> attachments,
        string stateDigest,
        string reactionStateDigest,
        MessagePollView? poll)
        : this(
            id,
            channelId,
            authorId,
            authorName,
            content,
            createdAt,
            editedAt,
            isPinned,
            type,
            flags,
            mentionedUserIds,
            mentionedRoleIds,
            attachments,
            stateDigest,
            reactionStateDigest,
            poll,
            IsTts: false,
            StickerIds: [],
            EmbedsJson: "[]",
            ComponentsJson: "[]",
            SharedClientTheme: null,
            AuthorAvatarHash: null)
    {
    }
}

public sealed record MessagePage(
    ImmutableArray<MessageView> Items,
    string? NextBeforeMessageId,
    string? NextAfterMessageId);

public sealed record MessageSearchFilter(
    string? Content,
    ImmutableArray<string> ChannelIds,
    ImmutableArray<string> AuthorIds,
    ImmutableArray<string> MentionedUserIds,
    ImmutableArray<string> MentionedRoleIds,
    ImmutableArray<string> Has,
    bool? IsPinned,
    string? MinimumMessageId,
    string? MaximumMessageId,
    string SortBy,
    string SortOrder,
    bool IncludeNsfw,
    int Limit,
    int Offset);

public sealed record MessageSearchResult(
    bool IsIndexing,
    int? DocumentsIndexed,
    int TotalResults,
    ImmutableArray<MessageView> Items);

public sealed record MessagePinView(
    DateTimeOffset PinnedAt,
    MessageView Message);

public sealed record MessagePinPage(
    ImmutableArray<MessagePinView> Items,
    bool HasMore,
    DateTimeOffset? NextBefore);

public sealed record MessageReactionUserView(
    string UserId,
    string Username);

public sealed record MessageReactionUserPage(
    ImmutableArray<MessageReactionUserView> Items,
    string? NextAfterUserId);

public sealed record PollVoterView(
    string UserId,
    string Username);

public sealed record PollVoterPage(
    ImmutableArray<PollVoterView> Items,
    string? NextAfterUserId);

public interface IDiscordMessageReader
{
    Task<MessageView> GetMessageAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken);

    Task<MessagePage> ListMessagesAsync(
        string channelId,
        int limit,
        string? beforeMessageId,
        string? afterMessageId,
        string? aroundMessageId,
        CancellationToken cancellationToken);

    Task<MessageSearchResult> SearchMessagesAsync(
        MessageSearchFilter filter,
        CancellationToken cancellationToken);

    Task<MessagePinPage> ListChannelPinsAsync(
        string channelId,
        int limit,
        DateTimeOffset? before,
        CancellationToken cancellationToken);

    Task<MessageReactionUserPage> ListMessageReactionsAsync(
        string channelId,
        string messageId,
        string emoji,
        int limit,
        string? afterUserId,
        string reactionType,
        CancellationToken cancellationToken);

    Task<PollVoterPage> ListPollVotersAsync(
        string channelId,
        string messageId,
        int answerId,
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken);
}
