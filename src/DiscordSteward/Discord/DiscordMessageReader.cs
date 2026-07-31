using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Discord;

internal sealed class DiscordMessageReader(
    DiscordOptions options,
    DiscordRestSession session) : IDiscordMessageReader, IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://discord.com/api/v10/", UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<MessageView> GetMessageAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken)
    {
        var normalizedChannelId = await EnsureGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        var normalizedMessageId = NormalizeSnowflake(messageId);
        return await ReadJsonAsync(
            $"channels/{normalizedChannelId}/messages/{normalizedMessageId}",
            ParseMessage,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MessagePage> ListMessagesAsync(
        string channelId,
        int limit,
        string? beforeMessageId,
        string? afterMessageId,
        string? aroundMessageId,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, 100);
        var suppliedCursors = new[] { beforeMessageId, afterMessageId, aroundMessageId }
            .Count(value => value is not null);
        if (suppliedCursors > 1)
        {
            throw InvalidArgument("Only one of before, after, or around may be supplied.");
        }

        var normalizedChannelId = await EnsureGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        var route = $"channels/{normalizedChannelId}/messages?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        route = AddOptionalSnowflakeQuery(route, "before", beforeMessageId);
        route = AddOptionalSnowflakeQuery(route, "after", afterMessageId);
        route = AddOptionalSnowflakeQuery(route, "around", aroundMessageId);
        return await ReadJsonAsync(
            route,
            root =>
            {
                var items = ParseArray(root, ParseMessage, 100);
                return new MessagePage(
                    items,
                    items.Length == limit && aroundMessageId is null ? items[^1].Id : null,
                    afterMessageId is not null && items.Length == limit ? items[0].Id : null);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MessageSearchResult> SearchMessagesAsync(
        MessageSearchFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateSearchFilter(filter);
        var query = new List<KeyValuePair<string, string>>
        {
            new("limit", filter.Limit.ToString(CultureInfo.InvariantCulture)),
            new("offset", filter.Offset.ToString(CultureInfo.InvariantCulture)),
            new("sort_by", filter.SortBy),
            new("sort_order", filter.SortOrder),
            new("include_nsfw", filter.IncludeNsfw ? "true" : "false")
        };
        AddOptional(query, "content", filter.Content);
        AddOptionalSnowflake(query, "min_id", filter.MinimumMessageId);
        AddOptionalSnowflake(query, "max_id", filter.MaximumMessageId);
        AddRepeatedSnowflakes(query, "channel_id", filter.ChannelIds, 500);
        AddRepeatedSnowflakes(query, "author_id", filter.AuthorIds, 100);
        AddRepeatedSnowflakes(query, "mentions", filter.MentionedUserIds, 100);
        AddRepeatedSnowflakes(query, "mentions_role_id", filter.MentionedRoleIds, 100);
        foreach (var value in filter.Has)
        {
            query.Add(new("has", NormalizeHas(value)));
        }

        if (filter.IsPinned.HasValue)
        {
            query.Add(new("pinned", filter.IsPinned.Value ? "true" : "false"));
        }

        var route = $"guilds/{GuildId()}/messages/search?{BuildQuery(query)}";
        using var response = await SendGetWithRateLimitRetryAsync(route, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            EnsureSuccess(response);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            session.ReportSuccessfulRead();
            return new MessageSearchResult(
                IsIndexing: true,
                OptionalNonNegativeInt32(root, "documents_indexed"),
                TotalResults: 0,
                Items: []);
        }

        if (!root.TryGetProperty("messages", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse("message search");
        }

        var items = ImmutableArray.CreateBuilder<MessageView>();
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Array)
            {
                throw InvalidResponse("message search group");
            }

            foreach (var message in group.EnumerateArray())
            {
                items.Add(ParseMessage(message));
                if (items.Count > filter.Limit)
                {
                    throw TooLarge("Discord returned more search messages than requested.");
                }
            }
        }

        session.ReportSuccessfulRead();
        return new MessageSearchResult(
            root.TryGetProperty("doing_deep_historical_index", out var indexing) && indexing.GetBoolean(),
            OptionalNonNegativeInt32(root, "documents_indexed"),
            RequiredNonNegativeInt32(root, "total_results"),
            items.ToImmutable());
    }

    public async Task<MessagePinPage> ListChannelPinsAsync(
        string channelId,
        int limit,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, 50);
        var normalizedChannelId = await EnsureGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        var route = $"channels/{normalizedChannelId}/messages/pins?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (before.HasValue)
        {
            route += $"&before={Uri.EscapeDataString(before.Value.ToString("O", CultureInfo.InvariantCulture))}";
        }

        return await ReadJsonAsync(
            route,
            root =>
            {
                if (!root.TryGetProperty("items", out var values) || values.ValueKind != JsonValueKind.Array)
                {
                    throw InvalidResponse("message pins");
                }

                var items = ParseArray(values, item => new MessagePinView(
                    RequiredTimestamp(item, "pinned_at"),
                    ParseMessage(RequiredObject(item, "message"))), 50);
                var hasMore = root.TryGetProperty("has_more", out var hasMoreValue) && hasMoreValue.GetBoolean();
                return new MessagePinPage(
                    items,
                    hasMore,
                    hasMore && !items.IsEmpty ? items[^1].PinnedAt : null);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MessageReactionUserPage> ListMessageReactionsAsync(
        string channelId,
        string messageId,
        string emoji,
        int limit,
        string? afterUserId,
        string reactionType,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, 100);
        var normalizedChannelId = await EnsureGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        var normalizedMessageId = NormalizeSnowflake(messageId);
        var encodedEmoji = NormalizeEmoji(emoji);
        var type = reactionType.Trim().ToLowerInvariant() switch
        {
            "normal" => 0,
            "burst" => 1,
            _ => throw InvalidArgument("Reaction type must be normal or burst.")
        };
        var route = $"channels/{normalizedChannelId}/messages/{normalizedMessageId}/reactions/{encodedEmoji}?limit={limit.ToString(CultureInfo.InvariantCulture)}&type={type.ToString(CultureInfo.InvariantCulture)}";
        route = AddOptionalSnowflakeQuery(route, "after", afterUserId);
        return await ReadJsonAsync(
            route,
            root =>
            {
                var items = ParseArray(root, user => new MessageReactionUserView(
                    RequiredSnowflake(user, "id"),
                    RequiredBoundedString(user, "username", 100)), 100);
                return new MessageReactionUserPage(
                    items,
                    items.Length == limit ? items[^1].UserId : null);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PollVoterPage> ListPollVotersAsync(
        string channelId,
        string messageId,
        int answerId,
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, 100);
        if (answerId < 1)
        {
            throw InvalidArgument("Poll answer ID must be positive.");
        }

        var normalizedChannelId = await EnsureGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        var normalizedMessageId = NormalizeSnowflake(messageId);
        var route = $"channels/{normalizedChannelId}/polls/{normalizedMessageId}/answers/{answerId.ToString(CultureInfo.InvariantCulture)}/voters?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        route = AddOptionalSnowflakeQuery(route, "after", afterUserId);
        try
        {
            return await ReadJsonAsync(
                route,
                root =>
                {
                    if (!root.TryGetProperty("users", out var users))
                    {
                        throw InvalidResponse("poll voters");
                    }

                    var items = ParseArray(users, user => new PollVoterView(
                        RequiredSnowflake(user, "id"),
                        RequiredBoundedString(user, "username", 100)), 100);
                    return new PollVoterPage(
                        items,
                        items.Length == limit ? items[^1].UserId : null);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
            var message = await GetMessageAsync(normalizedChannelId, normalizedMessageId, cancellationToken).ConfigureAwait(false);
            if (message.Poll?.Answers.Any(answer => answer.AnswerId == answerId && answer.VoteCount == 0) == true)
            {
                return new PollVoterPage([], null);
            }

            throw;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> EnsureGuildChannelAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var normalizedChannelId = NormalizeSnowflake(channelId);
        var observedGuildId = await ReadJsonAsync(
            $"channels/{normalizedChannelId}",
            root => RequiredSnowflake(root, "guild_id"),
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(observedGuildId, GuildId(), StringComparison.Ordinal))
        {
            throw NotFound();
        }

        return normalizedChannelId;
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
                delay <= TimeSpan.FromSeconds(15) ? delay : TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static MessageView ParseMessage(JsonElement root)
    {
        var author = RequiredObject(root, "author");
        var attachments = root.TryGetProperty("attachments", out var attachmentValues)
            ? ParseArray(attachmentValues, attachment => new MessageAttachmentView(
                RequiredSnowflake(attachment, "id"),
                RequiredBoundedString(attachment, "filename", 256),
                OptionalBoundedString(attachment, "description", 1_024),
                OptionalBoundedString(attachment, "content_type", 256),
                RequiredNonNegativeInt64(attachment, "size"),
                attachment.TryGetProperty("ephemeral", out var ephemeral) && ephemeral.GetBoolean(),
                ((OptionalNonNegativeInt64(attachment, "flags") ?? 0) & 1 << 3) != 0,
                OptionalNonNegativeDouble(attachment, "duration_secs"),
                OptionalBoundedString(attachment, "waveform", 2_048)), 10)
            : [];
        var reactionDigest = ResourceStateCanonicalizer.ComputeReactionDigest(
            root.TryGetProperty("reactions", out var reactions) ? reactions : default);
        var poll = ParsePoll(root);
        var message = new MessageView(
            RequiredSnowflake(root, "id"),
            RequiredSnowflake(root, "channel_id"),
            RequiredSnowflake(author, "id"),
            RequiredBoundedString(author, "username", 100),
            OptionalBoundedString(root, "content", 4_000) ?? string.Empty,
            RequiredTimestamp(root, "timestamp"),
            OptionalTimestamp(root, "edited_timestamp"),
            root.TryGetProperty("pinned", out var pinned) && pinned.GetBoolean(),
            RequiredNonNegativeInt32(root, "type"),
            OptionalNonNegativeInt64(root, "flags") ?? 0,
            ParseUserIds(root, "mentions"),
            ParseSnowflakeArray(root, "mention_roles", 100),
            attachments,
            StateDigest: string.Empty,
            reactionDigest,
            poll,
            IsTts: root.TryGetProperty("tts", out var tts) && tts.ValueKind == JsonValueKind.True,
            StickerIds: ParseStickerIds(root),
            EmbedsJson: RichMessagePayloadNormalizer.ParseObservedEmbeds(root),
            ComponentsJson: RichMessagePayloadNormalizer.ParseObservedComponents(root),
            SharedClientTheme: RichMessagePayloadNormalizer.ParseObservedTheme(root),
            AuthorAvatarHash: OptionalBoundedString(author, "avatar", 200));
        return message with { StateDigest = ResourceStateCanonicalizer.ComputeMessageDigest(message) };
    }

    private static ImmutableArray<string> ParseStickerIds(JsonElement root)
    {
        if (!root.TryGetProperty("sticker_items", out var values) || values.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        return ParseArray(values, item => RequiredSnowflake(item, "id"), 3);
    }

    private static MessagePollView? ParsePoll(JsonElement root)
    {
        if (!root.TryGetProperty("poll", out var poll) || poll.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var questionObject = RequiredObject(poll, "question");
        var question = RequiredBoundedString(questionObject, "text", 300);
        if (!poll.TryGetProperty("answers", out var answersElement))
        {
            throw InvalidResponse("poll answers");
        }

        var answers = ParseArray(answersElement, answer =>
        {
            var media = RequiredObject(answer, "poll_media");
            var emoji = media.TryGetProperty("emoji", out var emojiObject) && emojiObject.ValueKind == JsonValueKind.Object
                ? OptionalBoundedString(emojiObject, "name", 100) ??
                    (emojiObject.TryGetProperty("id", out var emojiId) && emojiId.ValueKind == JsonValueKind.String
                        ? NormalizeSnowflake(emojiId.GetString() ?? string.Empty)
                        : null)
                : null;
            return new MessagePollAnswerView(
                RequiredNonNegativeInt32(answer, "answer_id"),
                RequiredBoundedString(media, "text", 55),
                emoji,
                VoteCount: 0,
                DidSelfVote: false);
        }, 10).ToArray();
        if (poll.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Object &&
            results.TryGetProperty("answer_counts", out var answerCounts) && answerCounts.ValueKind == JsonValueKind.Array)
        {
            var counts = answerCounts.EnumerateArray().ToDictionary(
                item => RequiredNonNegativeInt32(item, "id"),
                item => (
                    Count: RequiredNonNegativeInt32(item, "count"),
                    Me: item.TryGetProperty("me_voted", out var me) && me.GetBoolean()));
            for (var index = 0; index < answers.Length; index++)
            {
                if (counts.TryGetValue(answers[index].AnswerId, out var count))
                {
                    answers[index] = answers[index] with { VoteCount = count.Count, DidSelfVote = count.Me };
                }
            }
        }

        DateTimeOffset? expiresAt = poll.TryGetProperty("expiry", out var expiry) && expiry.ValueKind == JsonValueKind.String &&
            expiry.TryGetDateTimeOffset(out var parsedExpiry) ? parsedExpiry : null;
        var allowsMultiselect = poll.TryGetProperty("allow_multiselect", out var multiselect) && multiselect.GetBoolean();
        var finalized = poll.TryGetProperty("results", out var pollResults) && pollResults.ValueKind == JsonValueKind.Object &&
            pollResults.TryGetProperty("is_finalized", out var isFinalized) && isFinalized.GetBoolean();
        var immutableAnswers = answers.ToImmutableArray();
        return new MessagePollView(
            question,
            immutableAnswers,
            expiresAt,
            allowsMultiselect,
            finalized,
            ResourceStateCanonicalizer.ComputePollDigest(
                question,
                immutableAnswers,
                expiresAt,
                allowsMultiselect,
                finalized));
    }

    private static ImmutableArray<string> ParseUserIds(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var values))
        {
            return [];
        }

        return ParseArray(values, user => RequiredSnowflake(user, "id"), 100);
    }

    private static ImmutableArray<string> ParseSnowflakeArray(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var values))
        {
            return [];
        }

        return ParseArray(values, value => value.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(value.GetString() ?? string.Empty)
            : throw InvalidResponse(name), maximum);
    }

    private static void ValidateSearchFilter(MessageSearchFilter filter)
    {
        ValidateLimit(filter.Limit, 25);
        if (filter.Offset is < 0 or > 9_975)
        {
            throw InvalidArgument("Message search offset must be between 0 and 9975.");
        }

        if (filter.Content is { Length: > 1_024 })
        {
            throw InvalidArgument("Message search content cannot exceed 1024 characters.");
        }

        if (filter.SortBy is not "timestamp" and not "relevance")
        {
            throw InvalidArgument("Message search sort must be timestamp or relevance.");
        }

        if (filter.SortOrder is not "asc" and not "desc")
        {
            throw InvalidArgument("Message search order must be asc or desc.");
        }

        if (filter.Content is null && filter.ChannelIds.IsEmpty && filter.AuthorIds.IsEmpty &&
            filter.MentionedUserIds.IsEmpty && filter.MentionedRoleIds.IsEmpty && filter.Has.IsEmpty &&
            !filter.IsPinned.HasValue && filter.MinimumMessageId is null && filter.MaximumMessageId is null)
        {
            throw InvalidArgument("Message search requires at least one explicit filter.");
        }
    }

    private static void AddRepeatedSnowflakes(
        List<KeyValuePair<string, string>> query,
        string name,
        ImmutableArray<string> values,
        int maximum)
    {
        if (values.Length > maximum)
        {
            throw InvalidArgument($"{name} exceeds its maximum number of values.");
        }

        foreach (var value in values)
        {
            query.Add(new(name, NormalizeSnowflake(value)));
        }
    }

    private static void AddOptionalSnowflake(
        List<KeyValuePair<string, string>> query,
        string name,
        string? value)
    {
        if (value is not null)
        {
            query.Add(new(name, NormalizeSnowflake(value)));
        }
    }

    private static void AddOptional(
        List<KeyValuePair<string, string>> query,
        string name,
        string? value)
    {
        if (value is not null)
        {
            query.Add(new(name, value));
        }
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> values) => string.Join(
        '&',
        values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string AddOptionalSnowflakeQuery(string route, string name, string? value) =>
        value is null ? route : $"{route}&{name}={NormalizeSnowflake(value)}";

    private static string NormalizeHas(string value) => value.Trim().ToLowerInvariant() switch
    {
        "image" or "sound" or "video" or "file" or "sticker" or "embed" or "link" or "poll" or "snapshot" => value.Trim().ToLowerInvariant(),
        _ => throw InvalidArgument("Message search has filters must use a documented value.")
    };

    private static string NormalizeEmoji(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value.Any(char.IsControl))
        {
            throw InvalidArgument("Emoji must be a bounded Unicode emoji or custom name:id value.");
        }

        return Uri.EscapeDataString(value);
    }

    private string GuildId() => options.GetRequiredGuildId().ToString(CultureInfo.InvariantCulture);

    private static string NormalizeSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : throw InvalidArgument("Discord ID must be a positive snowflake string.");

    private static JsonElement RequiredObject(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw InvalidResponse(name);

    private static string RequiredSnowflake(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(value.GetString() ?? string.Empty)
            : throw InvalidResponse(name);

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

    private static int RequiredNonNegativeInt32(JsonElement root, string name) =>
        OptionalNonNegativeInt32(root, name) ?? throw InvalidResponse(name);

    private static int? OptionalNonNegativeInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetInt32(out var parsed) && parsed >= 0 ? parsed : throw InvalidResponse(name);
    }

    private static double? OptionalNonNegativeDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetDouble(out var parsed) && double.IsFinite(parsed) && parsed >= 0
            ? parsed
            : throw InvalidResponse(name);
    }

    private static long RequiredNonNegativeInt64(JsonElement root, string name) =>
        OptionalNonNegativeInt64(root, name) ?? throw InvalidResponse(name);

    private static long? OptionalNonNegativeInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetInt64(out var parsed) && parsed >= 0 ? parsed : throw InvalidResponse(name);
    }

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

    private static ImmutableArray<T> ParseArray<T>(JsonElement root, Func<JsonElement, T> parse, int maximum)
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

    private static void ValidateLimit(int limit, int maximum)
    {
        if (limit is < 1 || limit > maximum)
        {
            throw InvalidArgument($"Limit must be between 1 and {maximum.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw DiscordErrorMapper.MapStatusCode(response.StatusCode, StewardErrorCodes.ResourceNotFound);
        }
    }

    private static string Bound(string? value, int maximum) =>
        (value ?? string.Empty).Length <= maximum ? value ?? string.Empty : (value ?? string.Empty)[..maximum];

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException InvalidResponse(string resource) =>
        new(StewardErrorCodes.DiscordUnavailable, $"Discord returned an invalid {resource} response.");

    private static StewardException NotFound() =>
        new(StewardErrorCodes.ResourceNotFound, "The requested channel was not found in the configured guild.");

    private static StewardException TooLarge(string message) => new(StewardErrorCodes.ResultTooLarge, message);
}
