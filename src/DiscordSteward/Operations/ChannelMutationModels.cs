using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record ChannelOverwriteInput(
    string TargetId,
    string TargetType,
    ImmutableArray<string> Allow,
    ImmutableArray<string> Deny);

public sealed record CreateChannelInput(
    string ExpectedChannelsStateDigest,
    string? ExpectedParentStateDigest,
    string Name,
    int? Position,
    string? ParentId,
    string? Topic,
    bool? IsNsfw,
    int? SlowModeSeconds,
    int? Bitrate,
    int? UserLimit,
    string? RtcRegion,
    int? VideoQualityMode,
    int? DefaultAutoArchiveMinutes,
    int? DefaultThreadSlowModeSeconds,
    int? DefaultSortOrder,
    int? DefaultForumLayout,
    ImmutableArray<ChannelOverwriteInput> RoleOverwrites,
    ImmutableArray<ForumTagInput>? AvailableTags = null,
    DefaultReactionInput? DefaultReaction = null);

public sealed record ForumTagInput(
    string? Id,
    string Name,
    bool Moderated,
    string? EmojiId,
    string? EmojiName);

public sealed record DefaultReactionInput(
    string? EmojiId,
    string? EmojiName);

public sealed record UpdateChannelSetInput(
    string? Name,
    string? ChannelType,
    string? Topic,
    bool? IsNsfw,
    int? SlowModeSeconds,
    int? Bitrate,
    int? UserLimit,
    string? RtcRegion,
    int? VideoQualityMode,
    int? DefaultAutoArchiveMinutes,
    int? DefaultThreadSlowModeSeconds,
    ImmutableArray<ForumTagInput>? AvailableTags,
    DefaultReactionInput? DefaultReaction,
    int? DefaultSortOrder,
    int? DefaultForumLayout,
    bool? RequireTag,
    bool? HideMediaDownloadOptions,
    bool? IsSpoiler);

public sealed record UpdateChannelInput(
    string ExpectedStateDigest,
    UpdateChannelSetInput Set,
    ImmutableArray<string> Clear);

public sealed record MoveChannelInput(
    string ExpectedStateDigest,
    string ExpectedChannelsStateDigest,
    string? DestinationParentId,
    string? ExpectedDestinationParentStateDigest,
    int Position);

public sealed record ChannelPositionInput(
    string ChannelId,
    int Position);

public sealed record ReorderChannelsInput(
    string ExpectedChannelsStateDigest,
    string? ParentId,
    ImmutableArray<ChannelPositionInput> Positions);

public sealed record SetChannelPermissionsInput(
    string ExpectedStateDigest,
    string TargetId,
    ImmutableArray<string> Allow,
    ImmutableArray<string> Deny,
    ImmutableArray<string> Inherit);

public sealed record RemoveChannelPermissionsInput(
    string ExpectedStateDigest,
    string TargetId);

public sealed record ResourceStateDigestInput(
    string ResourceId,
    string StateDigest);

public sealed record ReplaceChannelOverwritesInput(
    string ExpectedStateDigest,
    string? ExpectedParentStateDigest,
    ImmutableArray<ResourceStateDigestInput> ExpectedSyncedChildren,
    ImmutableArray<ChannelOverwriteInput> Overwrites);

public sealed record ArchiveChannelInput(
    string ExpectedStateDigest,
    string DestinationParentId,
    string ExpectedDestinationParentStateDigest,
    string Name,
    string Visibility,
    ImmutableArray<ChannelOverwriteInput> Overwrites);

public sealed record RestoreArchivedChannelInput(
    string ExpectedStateDigest,
    string? ParentId,
    string? ExpectedParentStateDigest,
    string Name,
    string Visibility,
    ImmutableArray<ChannelOverwriteInput> Overwrites);

public sealed record SetVoiceChannelStatusInput(
    string ExpectedStateDigest,
    string? Status);

public sealed record TriggerTypingIndicatorInput(
    string ExpectedChannelStateDigest);

public sealed record FollowAnnouncementChannelInput(
    string SourceChannelId,
    string ExpectedSourceStateDigest,
    string DestinationChannelId,
    string ExpectedDestinationStateDigest);

public sealed record CreateThreadInput(
    string ParentChannelId,
    string ExpectedParentStateDigest,
    string Name,
    string ThreadType,
    string? SourceMessageId,
    int AutoArchiveMinutes,
    int SlowModeSeconds,
    bool? IsInvitable);

public sealed record CreateForumPostInput(
    string ParentChannelId,
    string ExpectedParentStateDigest,
    string ExpectedAvailableTagsDigest,
    string Name,
    string? Content,
    ImmutableArray<string> AppliedTagIds,
    int AutoArchiveMinutes,
    int SlowModeSeconds,
    AllowedMentionsInput? AllowedMentions = null,
    ImmutableArray<string>? AssetIds = null,
    ImmutableArray<RichEmbedInput>? Embeds = null,
    ImmutableArray<string>? StickerIds = null,
    bool SuppressNotifications = false,
    bool SuppressEmbeds = false,
    ImmutableArray<StaticComponentInput>? Components = null);

public sealed record UpdateThreadSet(
    string? Name,
    bool? IsArchived,
    bool? IsLocked,
    int? AutoArchiveMinutes,
    int? SlowModeSeconds,
    ImmutableArray<string>? AppliedTagIds,
    bool? IsInvitable,
    bool? IsPinned);

public sealed record UpdateThreadInput(
    string ExpectedStateDigest,
    UpdateThreadSet Set);

public sealed record SetThreadMemberInput(
    string ThreadId,
    string ExpectedThreadStateDigest,
    string UserId,
    bool Present);

public sealed record DeleteThreadInput(
    string ExpectedStateDigest);

public sealed record ExpectedCascadeReferenceInput(
    string ResourceType,
    string ResourceId,
    string Relationship);

public sealed record DeleteChannelInput(
    string ExpectedStateDigest,
    string ExpectedCascadeDigest,
    ImmutableArray<string> ExpectedBlockers,
    ImmutableArray<ExpectedCascadeReferenceInput> ExpectedCascade);
