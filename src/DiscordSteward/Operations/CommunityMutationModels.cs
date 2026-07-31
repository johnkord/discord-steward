using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record GuildMetadataSet(
    string? Name,
    string? Description,
    string? PreferredLocale,
    bool? PremiumProgressBarEnabled,
    string? IconAssetId,
    string? BannerAssetId,
    string? SplashAssetId,
    string? DiscoverySplashAssetId);

public sealed record UpdateGuildMetadataInput(
    string ExpectedStateDigest,
    GuildMetadataSet Set,
    ImmutableArray<string> Clear);

public sealed record UpdateGuildSystemRoutingInput(
    string ExpectedStateDigest,
    string? AfkChannelId,
    int AfkTimeoutSeconds,
    string? SystemChannelId,
    int SystemChannelFlags,
    string? RulesChannelId,
    string? PublicUpdatesChannelId,
    string? SafetyAlertsChannelId);

public sealed record UpdateGuildSafetyInput(
    string ExpectedStateDigest,
    int VerificationLevel,
    int DefaultMessageNotifications,
    int ExplicitContentFilter);

public sealed record UpdateGuildAccessControlsInput(
    string ExpectedFeaturesStateDigest,
    bool InvitesDisabled,
    bool RaidAlertsDisabled);

public sealed record UpdateGuildWidgetInput(
    string ExpectedStateDigest,
    bool Enabled,
    string? ChannelId);

public sealed record WelcomeChannelInput(
    string ChannelId,
    string Description,
    string? EmojiId,
    string? EmojiName);

public sealed record UpdateWelcomeScreenInput(
    string ExpectedStateDigest,
    bool Enabled,
    string? Description,
    ImmutableArray<WelcomeChannelInput> Channels);

public sealed record OnboardingOptionInput(
    string? Id,
    string Title,
    string? Description,
    string? EmojiId,
    string? EmojiName,
    bool? EmojiAnimated,
    ImmutableArray<string> RoleIds,
    ImmutableArray<string> ChannelIds);

public sealed record OnboardingPromptInput(
    string? Id,
    string Title,
    string Type,
    bool SingleSelect,
    bool Required,
    bool InOnboarding,
    ImmutableArray<OnboardingOptionInput> Options);

public sealed record UpdateOnboardingInput(
    string ExpectedStateDigest,
    ImmutableArray<OnboardingPromptInput> Prompts,
    ImmutableArray<string> DefaultChannelIds,
    bool Enabled,
    string Mode);

public sealed record SetIncidentActionsInput(
    string ExpectedStateDigest,
    DateTimeOffset? InvitesDisabledUntil,
    DateTimeOffset? DirectMessagesDisabledUntil);
