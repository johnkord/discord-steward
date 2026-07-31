using System.Collections.Immutable;

namespace DiscordSteward.Discord;

public sealed record GuildWidgetView(
    bool Enabled,
    string? ChannelId,
    string StateDigest = "");

public sealed record IncidentStatusView(
    DateTimeOffset? InvitesDisabledUntil,
    DateTimeOffset? DirectMessagesDisabledUntil,
    DateTimeOffset? DirectMessageSpamDetectedAt,
    DateTimeOffset? RaidDetectedAt,
    string StateDigest = "");

public sealed record GuildMutationEvidence(
    string StateDigest,
    string? PreferredLocale = null,
    bool PremiumProgressBarEnabled = false,
    string? IconHash = null,
    string? BannerHash = null,
    string? SplashHash = null,
    string? DiscoverySplashHash = null,
    string? AfkChannelId = null,
    int AfkTimeoutSeconds = 300,
    string? SystemChannelId = null,
    int SystemChannelFlags = 0,
    string? RulesChannelId = null,
    string? PublicUpdatesChannelId = null,
    string? SafetyAlertsChannelId = null,
    int VerificationLevel = 0,
    int DefaultMessageNotifications = 0,
    int ExplicitContentFilter = 0,
    ImmutableArray<string> Features = default);

public interface IDiscordGuildMutationStateReader
{
    Task<GuildMutationEvidence> GetGuildMutationEvidenceAsync(CancellationToken cancellationToken);
}

public sealed record GuildVanityUrlView(
    string? Code,
    int Uses);

public sealed record GuildTemplateView(
    string Code,
    string Name,
    string? Description,
    int UsageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string SourceGuildId,
    bool? IsDirty,
    string StateDigest = "");

public sealed record ThreadMemberView(
    string ThreadId,
    string UserId,
    DateTimeOffset JoinedAt,
    int Flags);

public sealed record ThreadMemberPage(
    ImmutableArray<ThreadMemberView> Items,
    string? NextAfterUserId);

public sealed record ScheduledEventSubscriberView(
    string EventId,
    string UserId,
    string Username);

public sealed record ScheduledEventSubscriberPage(
    ImmutableArray<ScheduledEventSubscriberView> Items,
    string? NextAfterUserId);

public sealed record StageInstanceView(
    string ChannelId,
    string GuildId,
    string Topic,
    string PrivacyLevel,
    bool DiscoverableDisabled,
    string? ScheduledEventId,
    string StateDigest = "");

public sealed record InviteTargetUsersView(
    string Code,
    int UserCount,
    string StateDigest,
    ImmutableArray<string> UserIds);

public sealed record InviteTargetUserJobView(
    string Code,
    string RequestId,
    string Status,
    int TotalUsers,
    int ProcessedUsers,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);

public interface IGuildCatalogReader : IDiscordGuildMutationStateReader
{
    Task<GuildWidgetView> GetGuildWidgetAsync(CancellationToken cancellationToken);

    Task<IncidentStatusView> GetIncidentStatusAsync(CancellationToken cancellationToken);

    Task<GuildVanityUrlView> GetGuildVanityUrlAsync(CancellationToken cancellationToken);

    Task<ImmutableArray<GuildTemplateView>> ListGuildTemplatesAsync(CancellationToken cancellationToken);

    Task<GuildTemplateView> GetGuildTemplateAsync(string code, CancellationToken cancellationToken);

    Task<ThreadMemberPage> ListThreadMembersAsync(
        string threadId,
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken);

    Task<ScheduledEventSubscriberPage> ListScheduledEventSubscribersAsync(
        string eventId,
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken);

    Task<StageInstanceView> GetStageInstanceAsync(
        string channelId,
        CancellationToken cancellationToken);

    Task<InviteTargetUsersView> ListInviteTargetUsersAsync(
        string code,
        CancellationToken cancellationToken);

    Task<InviteTargetUserJobView> GetInviteTargetUserJobStatusAsync(
        string code,
        string requestId,
        CancellationToken cancellationToken);
}
