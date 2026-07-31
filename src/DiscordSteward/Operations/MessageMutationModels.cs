using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record AllowedMentionsInput(
    ImmutableArray<string> UserIds,
    ImmutableArray<string> RoleIds,
    bool Everyone,
    bool RepliedUser);

public sealed record SendMessageInput(
    string ExpectedChannelStateDigest,
    string Mode,
    string? Content,
    string? SourceChannelId,
    string? SourceMessageId,
    string? ExpectedSourceMessageStateDigest,
    AllowedMentionsInput AllowedMentions,
    ImmutableArray<string> AssetIds,
    string Nonce,
    bool SuppressNotifications,
    ImmutableArray<RichEmbedInput>? Embeds = null,
    ImmutableArray<string>? StickerIds = null,
    bool Tts = false,
    bool SuppressEmbeds = false,
    SharedClientThemeInput? SharedClientTheme = null,
    VoiceMessageInput? VoiceMessage = null,
    ImmutableArray<StaticComponentInput>? Components = null);

public sealed record EditOwnMessageInput(
    string ExpectedMessageStateDigest,
    string? Content,
    ImmutableArray<string> RetainedAttachmentIds,
    ImmutableArray<string> NewAssetIds,
    AllowedMentionsInput AllowedMentions,
    ImmutableArray<RichEmbedInput>? Embeds = null,
    bool? SuppressEmbeds = null,
    ImmutableArray<StaticComponentInput>? Components = null,
    ImmutableArray<string>? Clear = null);

public sealed record DeleteMessageInput(
    string ExpectedMessageStateDigest);

public sealed record ExpectedMessageStateInput(
    string MessageId,
    string StateDigest);

public sealed record BulkDeleteMessagesInput(
    ImmutableArray<ExpectedMessageStateInput> Messages);

public sealed record CrosspostAnnouncementMessageInput(
    string ExpectedMessageStateDigest);

public sealed record SetMessagePinnedInput(
    string ExpectedMessageStateDigest,
    bool Pinned);

public sealed record SetOwnReactionInput(
    string ExpectedMessageStateDigest,
    string ExpectedReactionStateDigest,
    string Emoji,
    bool Present);

public sealed record RemoveUserReactionInput(
    string ExpectedMessageStateDigest,
    string ExpectedReactionStateDigest,
    string Emoji,
    string UserId);

public sealed record ClearMessageReactionsInput(
    string ExpectedMessageStateDigest,
    string ExpectedReactionStateDigest,
    string? Emoji);

public sealed record PollAnswerInput(
    string Text,
    string? EmojiId,
    string? EmojiName);

public sealed record CreatePollInput(
    string ExpectedChannelStateDigest,
    string? Content,
    string Question,
    ImmutableArray<PollAnswerInput> Answers,
    int DurationHours,
    bool AllowMultiselect,
    AllowedMentionsInput AllowedMentions,
    string Nonce,
    bool SuppressNotifications);

public sealed record EndPollInput(
    string ExpectedMessageStateDigest,
    string ExpectedPollStateDigest);
