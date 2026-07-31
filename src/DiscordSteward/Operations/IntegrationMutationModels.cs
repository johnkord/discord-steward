using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record CreateInviteInput(
    string ExpectedChannelStateDigest,
    string ChannelId,
    int MaxAgeSeconds,
    int MaxUses,
    bool Temporary,
    bool Unique,
    string? TargetType,
    string? TargetUserId,
    string? TargetApplicationId,
    ImmutableArray<string> RoleIds);

public sealed record CreateTargetedInviteInput(
    CreateInviteInput Invite,
    ImmutableArray<string> UserIds);

public sealed record UpdateInviteTargetUsersInput(
    string ExpectedTargetUsersDigest,
    ImmutableArray<string> UserIds);

public sealed record DeleteInviteInput(string ExpectedStateDigest);

public sealed record CreateWebhookInput(
    string ExpectedChannelStateDigest,
    string ChannelId,
    string Name,
    string? AvatarAssetId);

public sealed record UpdateWebhookInput(
    string ExpectedStateDigest,
    string? StoredWebhookId,
    string? Name,
    string? AvatarAssetId,
    string? ChannelId,
    ImmutableArray<string> Clear);

public sealed record DeleteWebhookInput(
    string ExpectedStateDigest,
    string? StoredWebhookId);

public sealed record WebhookPollInput(
    string Question,
    ImmutableArray<PollAnswerInput> Answers,
    int DurationHours,
    bool AllowMultiselect);

public sealed record SendWebhookMessageInput(
    string StoredWebhookId,
    string ExpectedWebhookStateDigest,
    string? ThreadId,
    string? Content,
    AllowedMentionsInput AllowedMentions,
    ImmutableArray<string> AssetIds,
    string? UsernameOverride = null,
    string? AvatarUrlOverride = null,
    bool Tts = false,
    ImmutableArray<RichEmbedInput>? Embeds = null,
    WebhookPollInput? Poll = null,
    bool SuppressNotifications = false,
    bool SuppressEmbeds = false,
    ImmutableArray<StaticComponentInput>? Components = null,
    string? ThreadName = null,
    string? ExpectedAvailableTagsDigest = null,
    ImmutableArray<string>? AppliedTagIds = null);

public sealed record EditWebhookMessageInput(
    string StoredWebhookId,
    string ExpectedWebhookStateDigest,
    string MessageId,
    string ExpectedMessageStateDigest,
    string? ThreadId,
    string? Content,
    AllowedMentionsInput AllowedMentions,
    ImmutableArray<string> RetainedAttachmentIds,
    ImmutableArray<string> NewAssetIds,
    ImmutableArray<RichEmbedInput>? Embeds = null,
    bool? SuppressEmbeds = null,
    ImmutableArray<StaticComponentInput>? Components = null,
    ImmutableArray<string>? Clear = null);

public sealed record DeleteWebhookMessageInput(
    string StoredWebhookId,
    string ExpectedWebhookStateDigest,
    string MessageId,
    string ExpectedMessageStateDigest,
    string? ThreadId);

public sealed record CreateGuildTemplateInput(
    string ExpectedConfigurationStateDigest,
    string Name,
    string? Description);

public sealed record SyncGuildTemplateInput(
    string ExpectedConfigurationStateDigest,
    string ExpectedTemplateStateDigest);

public sealed record UpdateGuildTemplateInput(
    string ExpectedTemplateStateDigest,
    string? Name,
    string? Description,
    ImmutableArray<string> Clear);

public sealed record DeleteGuildTemplateInput(string ExpectedTemplateStateDigest);
