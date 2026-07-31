using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Discord;

internal sealed record GuildApiState(
    string Id,
    string Name,
    string? Description,
    string? PreferredLocale,
    bool PremiumProgressBarEnabled,
    string? IconHash,
    string? BannerHash,
    string? SplashHash,
    string? DiscoverySplashHash,
    string? AfkChannelId,
    int AfkTimeout,
    string? SystemChannelId,
    int SystemChannelFlags,
    string? RulesChannelId,
    string? PublicUpdatesChannelId,
    string? SafetyAlertsChannelId,
    int VerificationLevel,
    int DefaultMessageNotifications,
    int ExplicitContentFilter,
    ImmutableArray<string> Features)
{
    internal static GuildApiState Parse(JsonElement root) => new(
        RequiredSnowflake(root, "id"),
        RequiredString(root, "name", 100),
        OptionalString(root, "description", 1_024),
        OptionalString(root, "preferred_locale", 32),
        OptionalBoolean(root, "premium_progress_bar_enabled") ?? false,
        OptionalString(root, "icon", 256),
        OptionalString(root, "banner", 256),
        OptionalString(root, "splash", 256),
        OptionalString(root, "discovery_splash", 256),
        OptionalSnowflake(root, "afk_channel_id"),
        OptionalInt32(root, "afk_timeout") ?? 300,
        OptionalSnowflake(root, "system_channel_id"),
        OptionalInt32(root, "system_channel_flags") ?? 0,
        OptionalSnowflake(root, "rules_channel_id"),
        OptionalSnowflake(root, "public_updates_channel_id"),
        OptionalSnowflake(root, "safety_alerts_channel_id"),
        OptionalInt32(root, "verification_level") ?? 0,
        OptionalInt32(root, "default_message_notifications") ?? 0,
        OptionalInt32(root, "explicit_content_filter") ?? 0,
        ParseStrings(root, "features", 100));

    internal string ComputeDigest() => ResourceStateCanonicalizer.ComputeObjectDigest(this);

    private static string RequiredSnowflake(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? ChannelApiState.NormalizeSnowflake(value.GetString() ?? string.Empty)
            : throw Invalid(name);

    private static string? OptionalSnowflake(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String
            ? ChannelApiState.NormalizeSnowflake(value.GetString() ?? string.Empty)
            : throw Invalid(name);
    }

    private static string RequiredString(JsonElement root, string name, int maximum) =>
        OptionalString(root, name, maximum) ?? throw Invalid(name);

    private static string? OptionalString(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw Invalid(name);
        var text = value.GetString() ?? string.Empty;
        return text.Length <= maximum ? text : text[..maximum];
    }

    private static int? OptionalInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetInt32(out var parsed) ? parsed : throw Invalid(name);
    }

    private static bool? OptionalBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Invalid(name);
    }

    private static ImmutableArray<string> ParseStrings(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array) return [];
        var result = values.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : throw Invalid(name))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        return result.Length <= maximum ? result : throw Invalid(name);
    }

    private static StewardException Invalid(string name) => new(
        StewardErrorCodes.DiscordUnavailable,
        $"Discord returned invalid guild state for {name}.");
}
