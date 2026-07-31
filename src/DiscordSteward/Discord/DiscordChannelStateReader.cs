using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Operations;
using global::Discord;

namespace DiscordSteward.Discord;

public sealed record ChannelMutationEvidence(
    string ChannelId,
    string StateDigest,
    string AvailableTagsDigest);

public sealed record ExactChannelSnapshot(
    CategorySnapshot? Category,
    ChannelSnapshot? Channel,
    string StateDigest,
    string AvailableTagsDigest,
    ChannelMutationSettingsView? MutationSettings = null);

public sealed record ForumTagMutationView(
    string Id,
    string Name,
    bool Moderated,
    string? EmojiId,
    string? EmojiName);

public sealed record ChannelMutationSettingsView(
    int? Bitrate,
    int? UserLimit,
    string? RtcRegion,
    int? VideoQualityMode,
    int? DefaultAutoArchiveMinutes,
    int? DefaultThreadSlowModeSeconds,
    ImmutableArray<ForumTagMutationView> AvailableTags,
    string? DefaultReactionEmojiId,
    string? DefaultReactionEmojiName,
    int? DefaultSortOrder,
    int? DefaultForumLayout,
    bool RequireTag,
    bool HideMediaDownloadOptions,
    bool IsSpoiler,
    string? VoiceStatus);

public interface IDiscordChannelStateReader
{
    Task<ChannelMutationEvidence> GetMutationEvidenceAsync(
        string channelId,
        CancellationToken cancellationToken);

    Task<ExactChannelSnapshot> GetExactChannelAsync(
        string channelId,
        CancellationToken cancellationToken);
}

internal sealed class DiscordChannelStateReader(
    DiscordApiTransport transport,
    StewardRuntimeSettings settings) : IDiscordChannelStateReader
{
    public async Task<ChannelMutationEvidence> GetMutationEvidenceAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(channelId, cancellationToken).ConfigureAwait(false);

        return new ChannelMutationEvidence(state.Id, state.ComputeDigest(), state.ComputeAvailableTagsDigest());
    }

    public async Task<ExactChannelSnapshot> GetExactChannelAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(channelId, cancellationToken).ConfigureAwait(false);
        return CreateExactSnapshot(state);
    }

    internal static ExactChannelSnapshot CreateExactSnapshot(ChannelApiState state)
    {
        var overwrites = state.PermissionOverwrites.Select(overwrite => PermissionOverwriteSnapshot.Create(
            overwrite.Type == 0 ? "role" : "member",
            ulong.Parse(overwrite.Id, CultureInfo.InvariantCulture),
            PermissionNames(overwrite.Allow),
            PermissionNames(overwrite.Deny)));
        CategorySnapshot? category = null;
        ChannelSnapshot? channel = null;
        if (state.Type == 4)
        {
            category = CategorySnapshot.Create(
                ulong.Parse(state.GuildId, CultureInfo.InvariantCulture),
                ulong.Parse(state.Id, CultureInfo.InvariantCulture),
                state.Name,
                state.Position,
                overwrites);
        }
        else
        {
            var parentId = state.ParentId is null ? (ulong?)null : ulong.Parse(state.ParentId, CultureInfo.InvariantCulture);
            channel = ChannelSnapshot.Create(
                ulong.Parse(state.GuildId, CultureInfo.InvariantCulture),
                ulong.Parse(state.Id, CultureInfo.InvariantCulture),
                state.Name,
                ChannelKind(state.Type),
                state.Position,
                state.Type is 10 or 11 or 12 ? null : parentId,
                state.Topic,
                state.Nsfw,
                state.RateLimitPerUser,
                overwrites,
                state.Type is 10 or 11 or 12 ? parentId : null);
        }

        return new ExactChannelSnapshot(
            category,
            channel,
            state.ComputeDigest(),
            state.ComputeAvailableTagsDigest(),
            state.Type == 4 ? null : new ChannelMutationSettingsView(
                state.Bitrate,
                state.UserLimit,
                state.RtcRegion,
                state.VideoQualityMode,
                state.DefaultAutoArchiveDuration,
                state.DefaultThreadRateLimitPerUser,
                state.AvailableTags.Select(tag => new ForumTagMutationView(
                    tag.Id,
                    tag.Name,
                    tag.Moderated,
                    tag.EmojiId,
                    tag.EmojiName)).ToImmutableArray(),
                state.DefaultReactionEmojiId,
                state.DefaultReactionEmojiName,
                state.DefaultSortOrder,
                state.DefaultForumLayout,
                (state.Flags & (1L << 4)) != 0,
                (state.Flags & (1L << 15)) != 0,
                (state.Flags & (1L << 21)) != 0,
                state.VoiceStatus));
    }

    private async Task<ChannelApiState> GetStateAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var normalizedId = ChannelApiState.NormalizeSnowflake(channelId);
        var root = await transport.GetJsonAsync($"channels/{normalizedId}", cancellationToken).ConfigureAwait(false);
        var state = ChannelApiState.Parse(root);
        var guildId = settings.GuildId?.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(state.GuildId, guildId, StringComparison.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.GuildMismatch,
                "The requested channel does not belong to the configured guild.");
        }

        return state;
    }

    private static IEnumerable<string> PermissionNames(string bits)
    {
        var value = ulong.Parse(bits, CultureInfo.InvariantCulture);
        return Enum.GetValues<GuildPermission>()
            .Select(permission => (Permission: permission, Value: Convert.ToUInt64(permission, CultureInfo.InvariantCulture)))
            .Where(item => item.Value != 0 && (item.Value & (item.Value - 1)) == 0 && (value & item.Value) != 0)
            .GroupBy(item => item.Value)
            .Select(group => group.First().Permission.ToString())
            .Order(StringComparer.Ordinal);
    }

    private static string ChannelKind(int type) => type switch
    {
        0 => "text",
        2 => "voice",
        5 => "announcement",
        10 or 11 or 12 => "thread",
        13 => "stage",
        15 => "forum",
        16 => "media",
        _ => "unknown"
    };
}

internal sealed record ChannelOverwriteApiState(
    string Id,
    int Type,
    string Allow,
    string Deny);

internal sealed record ForumTagApiState(
    string Id,
    string Name,
    bool Moderated,
    string? EmojiId,
    string? EmojiName);

internal sealed record ChannelApiState(
    string Id,
    string GuildId,
    int Type,
    string Name,
    int Position,
    string? ParentId,
    string? Topic,
    bool? Nsfw,
    int? RateLimitPerUser,
    int? Bitrate,
    int? UserLimit,
    string? RtcRegion,
    int? VideoQualityMode,
    int? DefaultAutoArchiveDuration,
    long Flags,
    string? VoiceStatus,
    ImmutableArray<ChannelOverwriteApiState> PermissionOverwrites,
    ImmutableArray<ForumTagApiState> AvailableTags,
    string? DefaultReactionEmojiId,
    string? DefaultReactionEmojiName,
    int? DefaultThreadRateLimitPerUser,
    int? DefaultSortOrder,
    int? DefaultForumLayout,
    bool? Archived,
    bool? Locked,
    bool? Invitable,
    int? AutoArchiveDuration,
    ImmutableArray<string> AppliedTags)
{
    internal static ChannelApiState Parse(JsonElement root)
    {
        var metadata = root.TryGetProperty("thread_metadata", out var threadMetadata) &&
            threadMetadata.ValueKind == JsonValueKind.Object
                ? threadMetadata
                : default;
        var reaction = root.TryGetProperty("default_reaction_emoji", out var defaultReaction) &&
            defaultReaction.ValueKind == JsonValueKind.Object
                ? defaultReaction
                : default;
        return new ChannelApiState(
            RequiredSnowflake(root, "id"),
            RequiredSnowflake(root, "guild_id"),
            RequiredInt32(root, "type"),
            OptionalString(root, "name", 100) ?? string.Empty,
            OptionalInt32(root, "position") ?? 0,
            OptionalSnowflake(root, "parent_id"),
            OptionalString(root, "topic", 4_096),
            OptionalBoolean(root, "nsfw"),
            OptionalInt32(root, "rate_limit_per_user"),
            OptionalInt32(root, "bitrate"),
            OptionalInt32(root, "user_limit"),
            OptionalString(root, "rtc_region", 100),
            OptionalInt32(root, "video_quality_mode"),
            OptionalInt32(root, "default_auto_archive_duration"),
            OptionalInt64(root, "flags") ?? 0,
            OptionalString(root, "status", 500),
            ParseOverwrites(root),
            ParseTags(root),
            reaction.ValueKind == JsonValueKind.Object ? OptionalSnowflake(reaction, "emoji_id") : null,
            reaction.ValueKind == JsonValueKind.Object ? OptionalString(reaction, "emoji_name", 100) : null,
            OptionalInt32(root, "default_thread_rate_limit_per_user"),
            OptionalInt32(root, "default_sort_order"),
            OptionalInt32(root, "default_forum_layout"),
            metadata.ValueKind == JsonValueKind.Object ? OptionalBoolean(metadata, "archived") : null,
            metadata.ValueKind == JsonValueKind.Object ? OptionalBoolean(metadata, "locked") : null,
            metadata.ValueKind == JsonValueKind.Object ? OptionalBoolean(metadata, "invitable") : null,
            metadata.ValueKind == JsonValueKind.Object ? OptionalInt32(metadata, "auto_archive_duration") : null,
            ParseSnowflakeArray(root, "applied_tags", 5));
    }

    internal string ComputeDigest() => ComputeDigest(this);

    internal string ComputeAvailableTagsDigest() => ComputeDigest(AvailableTags);

    internal static string NormalizeSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : throw new StewardException(
                StewardErrorCodes.InvalidArgument,
                "Discord ID must be a positive snowflake string.");

    private static ImmutableArray<ChannelOverwriteApiState> ParseOverwrites(JsonElement root)
    {
        if (!root.TryGetProperty("permission_overwrites", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return values.EnumerateArray().Select(item => new ChannelOverwriteApiState(
                RequiredSnowflake(item, "id"),
                RequiredInt32(item, "type"),
                RequiredPermissionBits(item, "allow"),
                RequiredPermissionBits(item, "deny")))
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<ForumTagApiState> ParseTags(JsonElement root)
    {
        if (!root.TryGetProperty("available_tags", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tags = values.EnumerateArray().Select(item => new ForumTagApiState(
                RequiredSnowflake(item, "id"),
                OptionalString(item, "name", 20) ?? string.Empty,
                OptionalBoolean(item, "moderated") ?? false,
                OptionalSnowflake(item, "emoji_id"),
                OptionalString(item, "emoji_name", 100)))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        return tags.Length <= 20
            ? tags
            : throw new StewardException(
                StewardErrorCodes.ResultTooLarge,
                "Discord returned more than 20 forum tags.");
    }

    private static ImmutableArray<string> ParseSnowflakeArray(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = values.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String
                ? NormalizeSnowflake(value.GetString() ?? string.Empty)
                : throw InvalidResponse(name))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        return result.Length <= maximum ? result : throw InvalidResponse(name);
    }

    private static string RequiredSnowflake(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(value.GetString() ?? string.Empty)
            : throw InvalidResponse(name);

    private static string? OptionalSnowflake(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(value.GetString() ?? string.Empty)
            : throw InvalidResponse(name);
    }

    private static string RequiredPermissionBits(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            !ulong.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw InvalidResponse(name);
        }

        return parsed.ToString(CultureInfo.InvariantCulture);
    }

    private static int RequiredInt32(JsonElement root, string name) =>
        OptionalInt32(root, name) ?? throw InvalidResponse(name);

    private static int? OptionalInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetInt32(out var parsed) ? parsed : throw InvalidResponse(name);
    }

    private static long? OptionalInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetInt64(out var parsed) ? parsed : throw InvalidResponse(name);
    }

    private static bool? OptionalBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw InvalidResponse(name);
    }

    private static string? OptionalString(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw InvalidResponse(name);
        }

        var text = value.GetString() ?? string.Empty;
        return text.Length <= maximum ? text : text[..maximum];
    }

    private static string ComputeDigest<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, CatalogJson.Options);
        var canonical = CanonicalJson(element);
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    private static string CanonicalJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, element);
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
            {
                WriteCanonical(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }

    private static StewardException InvalidResponse(string field) => new(
        StewardErrorCodes.DiscordUnavailable,
        $"Discord returned invalid channel state for {field}.");
}
