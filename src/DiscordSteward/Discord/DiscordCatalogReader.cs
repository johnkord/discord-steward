using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Errors;

namespace DiscordSteward.Discord;

internal sealed class DiscordCatalogReader(
    DiscordOptions options,
    DiscordRestSession session) : IGuildCatalogReader, IDisposable
{
    private const int MaximumTargetUsersBytes = 1_048_576;
    private const int MaximumTargetUsers = 10_000;
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://discord.com/api/v10/", UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public Task<GuildMutationEvidence> GetGuildMutationEvidenceAsync(CancellationToken cancellationToken) =>
        ReadJsonAsync(
            $"guilds/{GuildId()}",
            root =>
            {
                var state = GuildApiState.Parse(root);
                return new GuildMutationEvidence(
                    state.ComputeDigest(),
                    state.PreferredLocale,
                    state.PremiumProgressBarEnabled,
                    state.IconHash,
                    state.BannerHash,
                    state.SplashHash,
                    state.DiscoverySplashHash,
                    state.AfkChannelId,
                    state.AfkTimeout,
                    state.SystemChannelId,
                    state.SystemChannelFlags,
                    state.RulesChannelId,
                    state.PublicUpdatesChannelId,
                    state.SafetyAlertsChannelId,
                    state.VerificationLevel,
                    state.DefaultMessageNotifications,
                    state.ExplicitContentFilter,
                    state.Features);
            },
            cancellationToken);

    public Task<GuildWidgetView> GetGuildWidgetAsync(CancellationToken cancellationToken) =>
        ReadJsonAsync(
            $"guilds/{GuildId()}/widget",
            root =>
            {
                var result = new GuildWidgetView(
                    RequiredBoolean(root, "enabled"),
                    OptionalSnowflake(root, "channel_id"));
                return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
            },
            cancellationToken);

    public Task<IncidentStatusView> GetIncidentStatusAsync(CancellationToken cancellationToken) =>
        ReadJsonAsync(
            $"guilds/{GuildId()}",
            root =>
            {
                if (!root.TryGetProperty("incidents_data", out var incidents) ||
                    incidents.ValueKind == JsonValueKind.Null)
                {
                    var empty = new IncidentStatusView(null, null, null, null);
                    return empty with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(empty) };
                }

                if (incidents.ValueKind != JsonValueKind.Object)
                {
                    throw InvalidResponse("incident status");
                }

                var result = new IncidentStatusView(
                    OptionalTimestamp(incidents, "invites_disabled_until"),
                    OptionalTimestamp(incidents, "dms_disabled_until"),
                    OptionalTimestamp(incidents, "dm_spam_detected_at"),
                    OptionalTimestamp(incidents, "raid_detected_at"));
                return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
            },
            cancellationToken);

    public Task<GuildVanityUrlView> GetGuildVanityUrlAsync(CancellationToken cancellationToken) =>
        ReadJsonAsync(
            $"guilds/{GuildId()}/vanity-url",
            root => new GuildVanityUrlView(
                OptionalBoundedString(root, "code", 100),
                RequiredNonNegativeInt32(root, "uses")),
            cancellationToken);

    public Task<ImmutableArray<GuildTemplateView>> ListGuildTemplatesAsync(
        CancellationToken cancellationToken) => ReadJsonAsync(
            $"guilds/{GuildId()}/templates",
            root => ParseArray(root, ParseTemplate, 100)
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .ToImmutableArray(),
            cancellationToken);

    public async Task<GuildTemplateView> GetGuildTemplateAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeCode(code);
        var template = await ReadJsonAsync(
            $"guilds/templates/{Uri.EscapeDataString(normalizedCode)}",
            ParseTemplate,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(template.SourceGuildId, GuildId(), StringComparison.Ordinal))
        {
            throw NotFound();
        }

        return template;
    }

    public Task<ThreadMemberPage> ListThreadMembersAsync(
        string threadId,
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, 100);
        var normalizedThreadId = NormalizeSnowflake(threadId);
        var route = $"channels/{normalizedThreadId}/thread-members?with_member=true&limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (afterUserId is not null)
        {
            route += $"&after={NormalizeSnowflake(afterUserId)}";
        }

        return ReadJsonAsync(
            route,
            root =>
            {
                var items = ParseArray(root, item => new ThreadMemberView(
                    OptionalSnowflake(item, "id") ?? normalizedThreadId,
                    RequiredSnowflake(item, "user_id"),
                    RequiredTimestamp(item, "join_timestamp"),
                    RequiredNonNegativeInt32(item, "flags")), 100);
                return new ThreadMemberPage(
                    items,
                    items.Length == limit ? items[^1].UserId : null);
            },
            cancellationToken);
    }

    public Task<ScheduledEventSubscriberPage> ListScheduledEventSubscribersAsync(
        string eventId,
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, 100);
        var normalizedEventId = NormalizeSnowflake(eventId);
        var route = $"guilds/{GuildId()}/scheduled-events/{normalizedEventId}/users?with_member=false&limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (afterUserId is not null)
        {
            route += $"&after={NormalizeSnowflake(afterUserId)}";
        }

        return ReadJsonAsync(
            route,
            root =>
            {
                var items = ParseArray(root, item =>
                {
                    var user = RequiredObject(item, "user");
                    return new ScheduledEventSubscriberView(
                        RequiredSnowflake(item, "guild_scheduled_event_id"),
                        RequiredSnowflake(user, "id"),
                        RequiredBoundedString(user, "username", 100));
                }, 100);
                return new ScheduledEventSubscriberPage(
                    items,
                    items.Length == limit ? items[^1].UserId : null);
            },
            cancellationToken);
    }

    public async Task<StageInstanceView> GetStageInstanceAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var normalizedChannelId = NormalizeSnowflake(channelId);
        var result = await ReadJsonAsync(
            $"stage-instances/{normalizedChannelId}",
            root =>
            {
                var result = new StageInstanceView(
                    RequiredSnowflake(root, "channel_id"),
                    RequiredSnowflake(root, "guild_id"),
                    RequiredBoundedString(root, "topic", 120),
                    PrivacyLevel(root),
                    root.TryGetProperty("discoverable_disabled", out var discoverable) && discoverable.GetBoolean(),
                    OptionalSnowflake(root, "guild_scheduled_event_id"));
                return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
            },
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(result.GuildId, GuildId(), StringComparison.Ordinal) ||
            !string.Equals(result.ChannelId, normalizedChannelId, StringComparison.Ordinal))
        {
            throw NotFound();
        }

        return result;
    }

    public async Task<InviteTargetUsersView> ListInviteTargetUsersAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeCode(code);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"invites/{Uri.EscapeDataString(normalizedCode)}/target-users");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        if (response.Content.Headers.ContentLength > MaximumTargetUsersBytes)
        {
            throw TooLarge("Target-user CSV exceeds the local byte limit.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaximumTargetUsersBytes)
        {
            throw TooLarge("Target-user CSV exceeds the local byte limit.");
        }

        var userIds = ParseTargetUsersCsv(bytes);
        session.ReportSuccessfulRead();
        var canonical = string.Join('\n', userIds);
        return new InviteTargetUsersView(
            normalizedCode,
            userIds.Length,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))),
            userIds);
    }

    public Task<InviteTargetUserJobView> GetInviteTargetUserJobStatusAsync(
        string code,
        string requestId,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedRequestId = Guid.TryParse(requestId, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D", CultureInfo.InvariantCulture)
            : throw InvalidArgument("Request ID must be a non-empty UUID.");
        return ReadJsonAsync(
            $"invites/{Uri.EscapeDataString(normalizedCode)}/target-users/job-status",
            root => new InviteTargetUserJobView(
                normalizedCode,
                normalizedRequestId,
                JobStatus(root),
                RequiredNonNegativeInt32(root, "total_users"),
                RequiredNonNegativeInt32(root, "processed_users"),
                RequiredTimestamp(root, "created_at"),
                OptionalTimestamp(root, "completed_at"),
                OptionalBoundedString(root, "error_message", 1_024)),
            cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<T> ReadJsonAsync<T>(
        string route,
        Func<JsonElement, T> parse,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendGetWithRateLimitRetryAsync(route, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var result = parse(document.RootElement);
            session.ReportSuccessfulRead();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException exception)
        {
            session.ReportFailure(exception.Code);
            throw;
        }
        catch (Exception exception)
        {
            var mapped = DiscordErrorMapper.Map(exception, StewardErrorCodes.ResourceNotFound);
            session.ReportFailure(mapped.Code);
            throw mapped;
        }
    }

    private async Task<HttpResponseMessage> SendGetWithRateLimitRetryAsync(
        string route,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = CreateRequest(HttpMethod.Get, route);
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 2)
            {
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            response.Dispose();
            await Task.Delay(
                delay <= TimeSpan.FromSeconds(30) ? delay : TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string route)
    {
        if (!options.IsConfigured)
        {
            throw DiscordErrorMapper.NotConfigured();
        }

        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", options.GetRequiredBotToken().Value);
        request.Headers.UserAgent.ParseAdd("DiscordSteward/1.0");
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw DiscordErrorMapper.MapStatusCode(response.StatusCode, StewardErrorCodes.ResourceNotFound);
        }
    }

    private string GuildId() => options.GetRequiredGuildId().ToString(CultureInfo.InvariantCulture);

    private GuildTemplateView ParseTemplate(JsonElement root)
    {
        var sourceGuildId = RequiredSnowflake(root, "source_guild_id");
        if (!string.Equals(sourceGuildId, GuildId(), StringComparison.Ordinal))
        {
            throw NotFound();
        }

        var result = new GuildTemplateView(
            RequiredBoundedString(root, "code", 100),
            RequiredBoundedString(root, "name", 100),
            OptionalBoundedString(root, "description", 120),
            RequiredNonNegativeInt32(root, "usage_count"),
            RequiredTimestamp(root, "created_at"),
            RequiredTimestamp(root, "updated_at"),
            sourceGuildId,
            root.TryGetProperty("is_dirty", out var dirty) && dirty.ValueKind != JsonValueKind.Null
                ? dirty.GetBoolean()
                : null);
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }

    private static ImmutableArray<string> ParseTargetUsersCsv(byte[] bytes)
    {
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw InvalidResponse("target-user CSV");
        }

        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine()?.TrimStart('\uFEFF').Trim(), "user_id", StringComparison.Ordinal))
        {
            throw InvalidResponse("target-user CSV header");
        }

        var values = ImmutableArray.CreateBuilder<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            values.Add(NormalizeSnowflake(line.Trim()));
            if (values.Count > MaximumTargetUsers)
            {
                throw TooLarge("Target-user CSV exceeds the local user limit.");
            }
        }

        return values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableArray<T> ParseArray<T>(
        JsonElement root,
        Func<JsonElement, T> parse,
        int maximum)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse("array");
        }

        var values = ImmutableArray.CreateBuilder<T>();
        foreach (var item in root.EnumerateArray())
        {
            values.Add(parse(item));
            if (values.Count > maximum)
            {
                throw TooLarge("Discord returned more records than the local safety limit.");
            }
        }

        return values.ToImmutable();
    }

    private static JsonElement RequiredObject(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw InvalidResponse(name);

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

    private static string RequiredBoundedString(JsonElement root, string name, int maximum) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? Bound(value.GetString(), maximum)
            : throw InvalidResponse(name);

    private static string? OptionalBoundedString(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? Bound(value.GetString(), maximum)
            : throw InvalidResponse(name);
    }

    private static bool RequiredBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw InvalidResponse(name);

    private static int RequiredNonNegativeInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) && parsed >= 0
            ? parsed
            : throw InvalidResponse(name);

    private static DateTimeOffset RequiredTimestamp(JsonElement root, string name) =>
        OptionalTimestamp(root, name) ?? throw InvalidResponse(name);

    private static DateTimeOffset? OptionalTimestamp(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : throw InvalidResponse(name);
    }

    private static string PrivacyLevel(JsonElement root) => RequiredNonNegativeInt32(root, "privacy_level") switch
    {
        1 => "Public",
        2 => "GuildOnly",
        _ => "Unknown"
    };

    private static string JobStatus(JsonElement root) => RequiredNonNegativeInt32(root, "status") switch
    {
        0 => "unspecified",
        1 => "processing",
        2 => "completed",
        3 => "failed",
        _ => "unknown"
    };

    private static string NormalizeSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : throw InvalidArgument("Discord ID must be a positive snowflake string.");

    private static string NormalizeCode(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? value
            : throw InvalidArgument("Invite or template code is invalid.");

    private static void ValidateLimit(int limit, int maximum)
    {
        if (limit is < 1 || limit > maximum)
        {
            throw InvalidArgument($"Limit must be between 1 and {maximum.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static string Bound(string? value, int maximum) =>
        (value ?? string.Empty).Length <= maximum
            ? value ?? string.Empty
            : (value ?? string.Empty)[..maximum];

    private static StewardException InvalidArgument(string message) =>
        new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException InvalidResponse(string resource) =>
        new(StewardErrorCodes.DiscordUnavailable, $"Discord returned an invalid {resource} response.");

    private static StewardException NotFound() =>
        new(StewardErrorCodes.ResourceNotFound, "The requested resource was not found in the configured guild.");

    private static StewardException TooLarge(string message) =>
        new(StewardErrorCodes.ResultTooLarge, message);
}
