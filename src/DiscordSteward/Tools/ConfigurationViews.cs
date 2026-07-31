using System.Collections.Immutable;
using DiscordSteward.Domain;

namespace DiscordSteward.Tools;

public sealed record GuildConfigurationSnapshotView(
    string Id,
    string Name,
    string OwnerId,
    string? Description,
    DateTimeOffset CreatedAt,
    ImmutableArray<string> Features,
    ImmutableArray<RoleConfigurationView> Roles,
    ImmutableArray<CategoryConfigurationView> Categories,
    ImmutableArray<ChannelConfigurationView> Channels,
    string RolesStateDigest = "",
    string ChannelsStateDigest = "",
    string StateDigest = "");

public sealed record GuildMetadataView(
    string Id,
    string Name,
    string OwnerId,
    string? Description,
    DateTimeOffset CreatedAt,
    ImmutableArray<string> Features,
    int RoleCount,
    int CategoryCount,
    int ChannelCount,
    string MutationStateDigest = "",
    GuildMutationSettingsView? MutationSettings = null,
    string FeaturesStateDigest = "");

public sealed record GuildMutationSettingsView(
    string? PreferredLocale,
    bool PremiumProgressBarEnabled,
    string? IconHash,
    string? BannerHash,
    string? SplashHash,
    string? DiscoverySplashHash,
    string? AfkChannelId,
    int AfkTimeoutSeconds,
    string? SystemChannelId,
    int SystemChannelFlags,
    string? RulesChannelId,
    string? PublicUpdatesChannelId,
    string? SafetyAlertsChannelId,
    int VerificationLevel,
    int DefaultMessageNotifications,
    int ExplicitContentFilter);

public sealed record RoleConfigurationView(
    string GuildId,
    string Id,
    string Name,
    int Position,
    bool IsManaged,
    bool IsEveryone,
    bool IsProtected,
    ImmutableArray<string> ProtectionReasons,
    ImmutableArray<string> Permissions,
    string? ColorHex,
    string? SecondaryColorHex,
    string? TertiaryColorHex,
    bool IsHoisted,
    bool IsMentionable,
    string? IconHash,
    string? UnicodeEmoji);

public sealed record MemberPermissionOverwriteView(
    string MemberId,
    ImmutableArray<string> AllowedPermissions,
    ImmutableArray<string> DeniedPermissions);

public sealed record MemberPermissionOverwritePage(
    string ChannelId,
    int TotalCount,
    ImmutableArray<MemberPermissionOverwriteView> Items,
    string? NextAfterMemberId);

public sealed record CategoryConfigurationView(
    string GuildId,
    string Id,
    string Name,
    int Position,
    PermissionOverwriteSummary PermissionOverwrites);

public sealed record ChannelConfigurationView(
    string GuildId,
    string Id,
    string Name,
    string Kind,
    int Position,
    string? CategoryId,
    string? ParentChannelId,
    string? Topic,
    bool? IsNsfw,
    int? SlowModeSeconds,
    string? StateDigest,
    PermissionOverwriteSummary PermissionOverwrites);

internal static class ConfigurationViewFactory
{
    internal static GuildConfigurationSnapshotView CreateSnapshot(GuildSnapshot snapshot)
    {
        var result = new GuildConfigurationSnapshotView(
            snapshot.Id,
            snapshot.Name,
            snapshot.OwnerId,
            snapshot.Description,
            snapshot.CreatedAt,
            snapshot.Features,
            snapshot.Roles.Select(role => new RoleConfigurationView(
            role.GuildId,
            role.Id,
            role.Name,
            role.Position,
            role.IsManaged,
            role.IsEveryone,
            role.IsProtected,
            role.ProtectionReasons,
            role.Permissions,
            role.ColorHex,
            role.SecondaryColorHex,
            role.TertiaryColorHex,
            role.IsHoisted,
            role.IsMentionable,
            role.IconHash,
            role.UnicodeEmoji)).ToImmutableArray(),
        snapshot.Categories.Select(category => new CategoryConfigurationView(
            category.GuildId,
            category.Id,
            category.Name,
            category.Position,
            PermissionOverwriteSummary.Create(category.PermissionOverwrites))).ToImmutableArray(),
        snapshot.Channels.Select(CreateChannel).ToImmutableArray(),
            Operations.ResourceStateCanonicalizer.ComputeRolesDigest(snapshot.Roles),
            Operations.ResourceStateCanonicalizer.ComputeChannelsDigest(snapshot));
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }

    internal static ChannelConfigurationView CreateCategory(CategorySnapshot category) => new(
        category.GuildId,
        category.Id,
        category.Name,
        Kind: "category",
        category.Position,
        CategoryId: null,
        ParentChannelId: null,
        Topic: null,
        IsNsfw: null,
        SlowModeSeconds: null,
        StateDigest: null,
        PermissionOverwriteSummary.Create(category.PermissionOverwrites));

    internal static ChannelConfigurationView CreateChannel(ChannelSnapshot channel) => new(
        channel.GuildId,
        channel.Id,
        channel.Name,
        channel.Kind,
        channel.Position,
        channel.CategoryId,
        channel.ParentChannelId,
        channel.Topic,
        channel.IsNsfw,
        channel.SlowModeSeconds,
        Operations.ChannelStateCanonicalizer.ComputeDigest(channel),
        PermissionOverwriteSummary.Create(channel.PermissionOverwrites));
}
