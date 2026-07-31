using System.Collections.Immutable;

namespace DiscordSteward.Discord;

public sealed record AutoModActionView(
    string Type,
    string? ChannelId,
    string? CustomMessage,
    int? TimeoutSeconds);

public sealed record AutoModRuleView(
    string Id,
    string GuildId,
    string Name,
    string EventType,
    string TriggerType,
    ImmutableArray<string> KeywordFilter,
    ImmutableArray<string> RegexPatterns,
    ImmutableArray<string> AllowList,
    ImmutableArray<string> Presets,
    int? MentionTotalLimit,
    bool? MentionRaidProtectionEnabled,
    ImmutableArray<AutoModActionView> Actions,
    bool Enabled,
    ImmutableArray<string> ExemptRoleIds,
    ImmutableArray<string> ExemptChannelIds,
    string StateDigest = "");

public sealed record AutoModRuleListView(
    ImmutableArray<AutoModRuleView> Items,
    string StateDigest);

public sealed record OnboardingOptionView(
    string Id,
    string Title,
    string? Description,
    string? Emoji,
    ImmutableArray<string> RoleIds,
    ImmutableArray<string> ChannelIds);

public sealed record OnboardingPromptView(
    string Id,
    string Title,
    string Type,
    bool IsSingleSelect,
    bool IsRequired,
    bool IsInOnboarding,
    ImmutableArray<OnboardingOptionView> Options);

public sealed record OnboardingView(
    string GuildId,
    bool IsEnabled,
    string Mode,
    bool IsBelowRequirements,
    ImmutableArray<string> DefaultChannelIds,
    ImmutableArray<OnboardingPromptView> Prompts,
    string StateDigest = "");

public sealed record WelcomeScreenChannelView(
    string ChannelId,
    string Description,
    string? Emoji,
    string? EmojiId = null,
    string? EmojiName = null);

public sealed record WelcomeScreenView(
    string GuildId,
    bool IsEnabled,
    string? Description,
    ImmutableArray<WelcomeScreenChannelView> Channels,
    string StateDigest = "");

public sealed record ScheduledEventView(
    string Id,
    string GuildId,
    string? ChannelId,
    string Name,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    string PrivacyLevel,
    string Status,
    string Type,
    string? EntityId,
    string? Location,
    string? CoverImageId,
    string? RecurrenceRule,
    string StateDigest = "");

public sealed record ScheduledEventListView(
    ImmutableArray<ScheduledEventView> Items,
    string StateDigest);

public sealed record BanView(
    string UserId,
    string Username,
    string? Reason,
    string StateDigest);

public sealed record InviteView(
    string Code,
    string GuildId,
    string ChannelId,
    string ChannelName,
    string ChannelType,
    DateTimeOffset? ExpiresAt,
    bool IsTemporary,
    int? MaxAgeSeconds,
    int? MaxUses,
    int? Uses,
    DateTimeOffset? CreatedAt,
    ImmutableArray<string> GrantedRoleIds,
    string StateDigest = "");

public sealed record IntegrationView(
    string Id,
    string GuildId,
    string Name,
    string Type,
    bool IsEnabled,
    bool? IsSyncing,
    string? RoleId,
    DateTimeOffset? SyncedAt,
    int? SubscriberCount,
    bool? IsRevoked,
    string? ApplicationId);

public sealed record WebhookView(
    string Id,
    string GuildId,
    string? ChannelId,
    string Name,
    string Type,
    string? ApplicationId,
    string? FollowedChannelId,
    string? FollowedChannelName,
    DateTimeOffset CreatedAt,
    string? AvatarHash = null,
    string StateDigest = "");

public sealed record AuditEventView(
    string Id,
    DateTimeOffset CreatedAt,
    string Action,
    string? ActorUserId,
    string? Reason,
    string DataType);

public sealed record GuildExpressionView(
    string ExpressionType,
    string Id,
    string Name,
    bool IsAvailable,
    bool IsManaged,
    bool IsAnimated,
    string? Description,
    ImmutableArray<string> Tags,
    ImmutableArray<string> RoleIds,
    double? Volume = null,
    string? Emoji = null,
    string StateDigest = "");

public sealed record GuildExpressionListView(
    ImmutableArray<GuildExpressionView> Items,
    string StateDigest);

public sealed record ThreadView(
    string Id,
    string GuildId,
    string ParentChannelId,
    string Name,
    bool IsArchived,
    bool IsLocked,
    bool? IsInvitable,
    int AutoArchiveMinutes,
    DateTimeOffset ArchiveTimestamp,
    int MemberCount,
    int MessageCount,
    string OwnerId,
    ImmutableArray<string> AppliedTagIds,
    string StateDigest = "");

public sealed record VoiceRegionView(
    string Id,
    string Name,
    bool Optimal,
    bool Deprecated,
    bool Custom,
    string Scope);

public sealed record StickerPackStickerView(
    string Id,
    string PackId,
    string Name,
    string? Description,
    ImmutableArray<string> Tags,
    string Format,
    int? SortValue);

public sealed record StickerPackView(
    string Id,
    string Name,
    string Description,
    string SkuId,
    string? CoverStickerId,
    string? BannerAssetId,
    ImmutableArray<StickerPackStickerView> Stickers);

public sealed record DefaultSoundboardSoundView(
    string Id,
    string Name,
    double Volume,
    string? EmojiId,
    string? EmojiName);

public sealed record GuildPreviewView(
    string GuildId,
    string Name,
    string? Description,
    string? IconHash,
    string? SplashHash,
    string? DiscoverySplashHash,
    ImmutableArray<string> Features,
    int ApproximateMemberCount,
    int ApproximatePresenceCount,
    int EmojiCount,
    int StickerCount);

public sealed record GuildWidgetImageView(
    string Style,
    string MediaType,
    int ByteLength,
    string Sha256,
    string Base64Png);

public sealed record BotGuildProfileView(
    string BotUserId,
    string? Nickname,
    string? AvatarHash,
    string? BannerHash,
    string? Bio,
    string StateDigest = "");

public interface IDiscordExtendedReader
{
    Task<ImmutableArray<AutoModRuleView>> ListAutoModRulesAsync(CancellationToken cancellationToken);

    Task<AutoModRuleView> GetAutoModRuleAsync(string ruleId, CancellationToken cancellationToken);

    Task<OnboardingView> GetOnboardingAsync(CancellationToken cancellationToken);

    Task<WelcomeScreenView> GetWelcomeScreenAsync(CancellationToken cancellationToken);

    Task<ImmutableArray<ScheduledEventView>> ListScheduledEventsAsync(CancellationToken cancellationToken);

    Task<ScheduledEventView> GetScheduledEventAsync(string eventId, CancellationToken cancellationToken);

    Task<ImmutableArray<BanView>> ListBansAsync(int limit, string? afterUserId, CancellationToken cancellationToken);

    Task<BanView> GetBanAsync(string userId, CancellationToken cancellationToken);

    Task<ImmutableArray<InviteView>> ListInvitesAsync(CancellationToken cancellationToken);

    Task<InviteView> GetInviteAsync(string code, CancellationToken cancellationToken);

    Task<ImmutableArray<IntegrationView>> ListIntegrationsAsync(CancellationToken cancellationToken);

    Task<ImmutableArray<WebhookView>> ListWebhooksAsync(CancellationToken cancellationToken);

    Task<WebhookView> GetWebhookAsync(string webhookId, CancellationToken cancellationToken);

    Task<ImmutableArray<AuditEventView>> ListAuditEventsAsync(
        int limit,
        string? beforeId,
        string? actorUserId,
        string? action,
        CancellationToken cancellationToken);

    Task<ImmutableArray<GuildExpressionView>> ListGuildExpressionsAsync(
        string expressionType,
        CancellationToken cancellationToken);

    Task<GuildExpressionView> GetGuildExpressionAsync(
        string expressionType,
        string expressionId,
        CancellationToken cancellationToken);

    Task<ImmutableArray<ThreadView>> ListActiveThreadsAsync(CancellationToken cancellationToken);

    Task<ImmutableArray<ThreadView>> ListArchivedThreadsAsync(
        string parentChannelId,
        string archiveType,
        int limit,
        DateTimeOffset? before,
        CancellationToken cancellationToken);

    Task<ThreadView> GetThreadAsync(string threadId, CancellationToken cancellationToken);

    Task<ImmutableArray<VoiceRegionView>> ListVoiceRegionsAsync(
        string scope,
        CancellationToken cancellationToken);

    Task<ThreadMemberView> GetThreadMemberAsync(
        string threadId,
        string userId,
        CancellationToken cancellationToken);

    Task<ImmutableArray<InviteView>> ListChannelInvitesAsync(
        string channelId,
        CancellationToken cancellationToken);

    Task<ImmutableArray<StickerPackView>> ListStickerPacksAsync(CancellationToken cancellationToken);

    Task<StickerPackView> GetStickerPackAsync(string packId, CancellationToken cancellationToken);

    Task<ImmutableArray<DefaultSoundboardSoundView>> ListDefaultSoundboardSoundsAsync(
        CancellationToken cancellationToken);

    Task<GuildPreviewView> GetGuildPreviewAsync(CancellationToken cancellationToken);

    Task<GuildWidgetImageView> GetGuildWidgetImageAsync(
        string style,
        CancellationToken cancellationToken);

    Task<BotGuildProfileView> GetBotGuildProfileAsync(CancellationToken cancellationToken);
}
