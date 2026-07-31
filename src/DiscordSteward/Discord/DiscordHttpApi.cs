using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Errors;

namespace DiscordSteward.Discord;

internal sealed class DiscordHttpApi(DiscordOptions options) : IDiscordHttpApi, IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://discord.com/api/v10/", UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<ImmutableArray<ThreadView>> ListActiveThreadsAsync(
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = CreateRequest(
            HttpMethod.Get,
            $"guilds/{options.GetRequiredGuildId().ToString(CultureInfo.InvariantCulture)}/threads/active");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw DiscordErrorMapper.MapStatusCode(
                response.StatusCode,
                StewardErrorCodes.ResourceNotFound);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("threads", out var threads) ||
            threads.ValueKind != JsonValueKind.Array)
        {
            throw new StewardException(
                StewardErrorCodes.DiscordUnavailable,
                "Discord returned an invalid active-thread response.");
        }

        var results = ImmutableArray.CreateBuilder<ThreadView>();
        foreach (var thread in threads.EnumerateArray())
        {
            results.Add(ParseThread(thread));
            if (results.Count > 1_000)
            {
                throw new StewardException(
                    StewardErrorCodes.ResultTooLarge,
                    "Discord returned more active threads than the configured safety limit.");
            }
        }

        return results
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public async Task<ImmutableArray<GuildExpressionView>> ListGuildExpressionsAsync(
        string expressionType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var normalizedType = expressionType?.Trim().ToLowerInvariant();
        var route = normalizedType switch
        {
            "emoji" => $"guilds/{options.GetRequiredGuildId().ToString(CultureInfo.InvariantCulture)}/emojis",
            "sticker" => $"guilds/{options.GetRequiredGuildId().ToString(CultureInfo.InvariantCulture)}/stickers",
            "soundboard" => $"guilds/{options.GetRequiredGuildId().ToString(CultureInfo.InvariantCulture)}/soundboard-sounds",
            _ => throw new StewardException(
                StewardErrorCodes.InvalidArgument,
                "Expression type must be emoji, sticker, or soundboard for this endpoint.")
        };
        using var request = CreateRequest(HttpMethod.Get, route);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw DiscordErrorMapper.MapStatusCode(response.StatusCode, StewardErrorCodes.ResourceNotFound);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var items = normalizedType == "soundboard" &&
            document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("items", out var soundboardItems)
                ? soundboardItems
                : document.RootElement;
        if (items.ValueKind != JsonValueKind.Array)
        {
            throw new StewardException(
                StewardErrorCodes.DiscordUnavailable,
                "Discord returned an invalid guild-expression response.");
        }

        var results = ImmutableArray.CreateBuilder<GuildExpressionView>();
        foreach (var item in items.EnumerateArray())
        {
            results.Add(normalizedType switch
            {
                "emoji" => ParseEmoji(item),
                "sticker" => ParseSticker(item),
                "soundboard" => ParseSoundboardSound(item),
                _ => throw InvalidResponse("guild expression")
            });
            if (results.Count > 1_000)
            {
                throw new StewardException(
                    StewardErrorCodes.ResultTooLarge,
                    "Discord returned more expressions than the configured safety limit.");
            }
        }

        return results
            .Select(item => item with
            {
                StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(item)
            })
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public async Task<ImmutableDictionary<string, int>> GetRoleMemberCountsAsync(
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = CreateRequest(
            HttpMethod.Get,
            $"guilds/{options.GetRequiredGuildId().ToString(CultureInfo.InvariantCulture)}/roles/member-counts");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw DiscordErrorMapper.MapStatusCode(response.StatusCode, StewardErrorCodes.ResourceNotFound);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw InvalidResponse("role member count");
        }

        var result = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!ulong.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                id == 0 ||
                !property.Value.TryGetInt32(out var count) ||
                count < 0)
            {
                throw InvalidResponse("role member count");
            }

            result[id.ToString(CultureInfo.InvariantCulture)] = count;
        }

        return result.ToImmutable();
    }

    public async Task<int?> PreviewMemberPruneAsync(
        int inactivityDays,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (inactivityDays is < 1 or > 30)
        {
            throw new StewardException(
                StewardErrorCodes.InvalidArgument,
                "Prune inactivity days must be between 1 and 30.");
        }

        var normalizedRoleIds = roleIds.Select(ParseSnowflake).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();
        var route = $"guilds/{options.GetRequiredGuildId().ToString(CultureInfo.InvariantCulture)}/prune?days={inactivityDays.ToString(CultureInfo.InvariantCulture)}";
        if (normalizedRoleIds.Length > 0)
        {
            route += $"&include_roles={Uri.EscapeDataString(string.Join(',', normalizedRoleIds))}";
        }

        using var request = CreateRequest(HttpMethod.Get, route);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw DiscordErrorMapper.MapStatusCode(response.StatusCode, StewardErrorCodes.ResourceNotFound);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("pruned", out var pruned))
        {
            throw InvalidResponse("prune preview");
        }

        return pruned.ValueKind == JsonValueKind.Null ? null : pruned.GetInt32();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bot",
            options.GetRequiredBotToken().Value);
        request.Headers.UserAgent.ParseAdd("DiscordSteward/1.0");
        return request;
    }

    private void EnsureConfigured()
    {
        if (!options.IsConfigured)
        {
            throw DiscordErrorMapper.NotConfigured();
        }
    }

    private static ThreadView ParseThread(JsonElement thread)
    {
        var metadata = thread.GetProperty("thread_metadata");
        return new ThreadView(
            RequiredSnowflake(thread, "id"),
            RequiredSnowflake(thread, "guild_id"),
            RequiredSnowflake(thread, "parent_id"),
            Bound(thread.GetProperty("name").GetString(), 100),
            metadata.GetProperty("archived").GetBoolean(),
            metadata.GetProperty("locked").GetBoolean(),
            metadata.TryGetProperty("invitable", out var invitable) ? invitable.GetBoolean() : null,
            metadata.GetProperty("auto_archive_duration").GetInt32(),
            metadata.GetProperty("archive_timestamp").GetDateTimeOffset(),
            thread.TryGetProperty("member_count", out var memberCount) ? memberCount.GetInt32() : 0,
            thread.TryGetProperty("message_count", out var messageCount) ? messageCount.GetInt32() : 0,
            RequiredSnowflake(thread, "owner_id"),
            thread.TryGetProperty("applied_tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                ? tags.EnumerateArray()
                    .Select(value => value.GetString() ?? string.Empty)
                    .Where(value => value.Length > 0)
                    .Order(StringComparer.Ordinal)
                    .ToImmutableArray()
                : []);
    }

    private static GuildExpressionView ParseEmoji(JsonElement emoji) => new(
        "emoji",
        RequiredSnowflake(emoji, "id"),
        Bound(emoji.TryGetProperty("name", out var name) ? name.GetString() : null, 100),
        !emoji.TryGetProperty("available", out var available) || available.GetBoolean(),
        emoji.TryGetProperty("managed", out var managed) && managed.GetBoolean(),
        emoji.TryGetProperty("animated", out var animated) && animated.GetBoolean(),
        Description: null,
        Tags: [],
        emoji.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
            ? roles.EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray()
            : []);

    private static GuildExpressionView ParseSticker(JsonElement sticker)
    {
        var rawTags = sticker.TryGetProperty("tags", out var tags) ? tags.GetString() : null;
        var format = sticker.TryGetProperty("format_type", out var formatType) ? formatType.GetInt32() : 1;
        return new GuildExpressionView(
            "sticker",
            RequiredSnowflake(sticker, "id"),
            Bound(sticker.TryGetProperty("name", out var name) ? name.GetString() : null, 100),
            !sticker.TryGetProperty("available", out var available) || available.GetBoolean(),
            IsManaged: false,
            IsAnimated: format != 1,
            BoundOptional(sticker.TryGetProperty("description", out var description) ? description.GetString() : null, 100),
            string.IsNullOrWhiteSpace(rawTags)
                ? []
                : rawTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => Bound(value, 100))
                    .ToImmutableArray(),
            RoleIds: []);
    }

    private static GuildExpressionView ParseSoundboardSound(JsonElement sound)
    {
        if (!sound.TryGetProperty("volume", out var volumeElement) ||
            !volumeElement.TryGetDouble(out var volume) ||
            volume is < 0 or > 1)
        {
            throw InvalidResponse("soundboard sound");
        }

        var emoji = sound.TryGetProperty("emoji_id", out var emojiId) && emojiId.ValueKind == JsonValueKind.String
            ? RequiredSnowflake(sound, "emoji_id")
            : BoundOptional(sound.TryGetProperty("emoji_name", out var emojiName) ? emojiName.GetString() : null, 100);
        return new GuildExpressionView(
            "soundboard",
            RequiredSnowflake(sound, "sound_id"),
            Bound(sound.TryGetProperty("name", out var name) ? name.GetString() : null, 32),
            sound.TryGetProperty("available", out var available) && available.GetBoolean(),
            IsManaged: false,
            IsAnimated: false,
            Description: null,
            Tags: [],
            RoleIds: [],
            volume,
            emoji);
    }

    private static string RequiredSnowflake(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !ulong.TryParse(property.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            parsed == 0)
        {
            throw new StewardException(
                StewardErrorCodes.DiscordUnavailable,
                "Discord returned an invalid active-thread identifier.");
        }

        return parsed.ToString(CultureInfo.InvariantCulture);
    }

    private static ulong ParseSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new StewardException(
                StewardErrorCodes.InvalidArgument,
                "Discord ID must be a positive snowflake string.");

    private static StewardException InvalidResponse(string resource) => new(
        StewardErrorCodes.DiscordUnavailable,
        $"Discord returned an invalid {resource} response.");

    private static string Bound(string? value, int maximum) =>
        (value ?? string.Empty).Length <= maximum
            ? value ?? string.Empty
            : (value ?? string.Empty)[..maximum];

    private static string? BoundOptional(string? value, int maximum) =>
        value is null ? null : Bound(value, maximum);
}
