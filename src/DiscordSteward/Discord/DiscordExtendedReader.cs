using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Errors;
using global::Discord;
using global::Discord.Rest;

namespace DiscordSteward.Discord;

internal sealed class DiscordExtendedReader(
    DiscordRestSession session,
    IDiscordHttpApi httpApi,
    IDiscordChannelStateReader channelStateReader,
    IDiscordBotInspector botInspector,
    DiscordApiTransport transport,
    StewardRuntimeSettings settings) : IDiscordExtendedReader
{
    public async Task<ImmutableArray<AutoModRuleView>> ListAutoModRulesAsync(
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var rules = await guild.GetAutoModRulesAsync(options).ConfigureAwait(false);
        return rules.Select(NormalizeAutoModRule).OrderBy(rule => rule.Id, StringComparer.Ordinal).ToImmutableArray();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<AutoModRuleView> GetAutoModRuleAsync(
        string ruleId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var rule = await guild.GetAutoModRuleAsync(ParseSnowflake(ruleId), options).ConfigureAwait(false)
            ?? throw NotFound();
        return NormalizeAutoModRule(rule);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<OnboardingView> GetOnboardingAsync(
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var onboarding = await guild.GetOnboardingAsync(options).ConfigureAwait(false);
        var result = new OnboardingView(
            FormatId(onboarding.GuildId),
            onboarding.IsEnabled,
            onboarding.Mode.ToString(),
            onboarding.IsBelowRequirements,
            FormatIds(onboarding.DefaultChannelIds),
            onboarding.Prompts.Select(prompt => new OnboardingPromptView(
                FormatId(prompt.Id),
                Bound(prompt.Title, 100),
                prompt.Type.ToString(),
                prompt.IsSingleSelect,
                prompt.IsRequired,
                prompt.IsInOnboarding,
                prompt.Options.Select(option => new OnboardingOptionView(
                    FormatId(option.Id),
                    Bound(option.Title, 100),
                    BoundOptional(option.Description, 1_024),
                    option.Emoji?.ToString(),
                    FormatIds(option.RoleIds),
                    FormatIds(option.ChannelIds))).ToImmutableArray())).ToImmutableArray());
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }, cancellationToken).ConfigureAwait(false);

    public async Task<WelcomeScreenView> GetWelcomeScreenAsync(
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        _ = options;
        var guildId = FormatId(guild.Id);
        var guildRoot = await transport.GetJsonAsync($"guilds/{guildId}", cancellationToken).ConfigureAwait(false);
        JsonElement screen;
        try
        {
            screen = await transport.GetJsonAsync($"guilds/{guildId}/welcome-screen", cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
            screen = default;
        }

        var enabled = guildRoot.TryGetProperty("features", out var features) &&
            features.ValueKind == JsonValueKind.Array &&
            features.EnumerateArray().Any(feature =>
                feature.ValueKind == JsonValueKind.String &&
                string.Equals(feature.GetString(), "WELCOME_SCREEN_ENABLED", StringComparison.OrdinalIgnoreCase));
        var channels = screen.ValueKind == JsonValueKind.Object &&
            screen.TryGetProperty("welcome_channels", out var channelValues) &&
            channelValues.ValueKind == JsonValueKind.Array
                ? channelValues.EnumerateArray().Select(channel =>
                {
                    var emojiId = channel.TryGetProperty("emoji_id", out var id) && id.ValueKind == JsonValueKind.String
                        ? ChannelApiState.NormalizeSnowflake(id.GetString() ?? string.Empty)
                        : null;
                    var emojiName = channel.TryGetProperty("emoji_name", out var name) && name.ValueKind == JsonValueKind.String
                        ? Bound(name.GetString() ?? string.Empty, 100)
                        : null;
                    return new WelcomeScreenChannelView(
                        ChannelApiState.NormalizeSnowflake(channel.GetProperty("channel_id").GetString() ?? string.Empty),
                        Bound(channel.GetProperty("description").GetString() ?? string.Empty, 1_024),
                        emojiId is null ? emojiName : $"{emojiName}:{emojiId}",
                        emojiId,
                        emojiName);
                }).ToImmutableArray()
                : [];
        var result = new WelcomeScreenView(
            guildId,
            enabled,
            screen.ValueKind == JsonValueKind.Object &&
                screen.TryGetProperty("description", out var description) &&
                description.ValueKind == JsonValueKind.String
                    ? Bound(description.GetString() ?? string.Empty, 1_024)
                    : null,
            channels);
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<ScheduledEventView>> ListScheduledEventsAsync(
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var events = await guild.GetEventsAsync(options).ConfigureAwait(false);
        return events.Select(NormalizeEvent).OrderBy(item => item.StartTime).ThenBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ScheduledEventView> GetScheduledEventAsync(
        string eventId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var guildEvent = await guild.GetEventAsync(ParseSnowflake(eventId), options).ConfigureAwait(false)
            ?? throw NotFound();
        return NormalizeEvent(guildEvent);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<BanView>> ListBansAsync(
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        ValidateLimit(limit, 1_000);
        var pages = afterUserId is null
            ? guild.GetBansAsync(limit, options)
            : guild.GetBansAsync(ParseSnowflake(afterUserId), Direction.After, limit, options);
        var results = ImmutableArray.CreateBuilder<BanView>();
        await foreach (var page in pages.ConfigureAwait(false))
        {
            foreach (var ban in page)
            {
                results.Add(NormalizeBan(ban));
                if (results.Count == limit)
                {
                    return results.ToImmutable();
                }
            }
        }

        return results.ToImmutable();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<BanView> GetBanAsync(
        string userId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var ban = await guild.GetBanAsync(ParseSnowflake(userId), options).ConfigureAwait(false)
            ?? throw NotFound();
        return NormalizeBan(ban);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<InviteView>> ListInvitesAsync(
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var invites = await guild.GetInvitesAsync(options).ConfigureAwait(false);
        return invites.Select(NormalizeInvite).OrderBy(invite => invite.Code, StringComparer.Ordinal).ToImmutableArray();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<InviteView> GetInviteAsync(
        string code,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var normalizedCode = NormalizeCode(code);
        var invites = await guild.GetInvitesAsync(options).ConfigureAwait(false);
        var invite = invites.SingleOrDefault(candidate => string.Equals(candidate.Code, normalizedCode, StringComparison.Ordinal))
            ?? throw NotFound();
        return NormalizeInvite(invite);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<IntegrationView>> ListIntegrationsAsync(
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var integrations = await guild.GetIntegrationsAsync(options).ConfigureAwait(false);
        return integrations.Select(NormalizeIntegration).OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<WebhookView>> ListWebhooksAsync(
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var webhooks = await guild.GetWebhooksAsync(options).ConfigureAwait(false);
        return webhooks.Select(NormalizeWebhook).OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<WebhookView> GetWebhookAsync(
        string webhookId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var id = ParseSnowflake(webhookId);
        var webhooks = await guild.GetWebhooksAsync(options).ConfigureAwait(false);
        return webhooks.Where(webhook => webhook.Id == id).Select(NormalizeWebhook).SingleOrDefault()
            ?? throw NotFound();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<AuditEventView>> ListAuditEventsAsync(
        int limit,
        string? beforeId,
        string? actorUserId,
        string? action,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        ValidateLimit(limit, 100);
        ActionType? actionType = null;
        if (action is not null)
        {
            if (!Enum.TryParse<ActionType>(action, ignoreCase: true, out var parsedAction))
            {
                throw InvalidArgument("Audit action is not a known Discord action type.");
            }

            actionType = parsedAction;
        }

        var pages = guild.GetAuditLogsAsync(
            limit,
            options,
            beforeId is null ? null : ParseSnowflake(beforeId),
            actorUserId is null ? null : ParseSnowflake(actorUserId),
            actionType);
        var results = ImmutableArray.CreateBuilder<AuditEventView>();
        await foreach (var page in pages.ConfigureAwait(false))
        {
            foreach (var entry in page)
            {
                results.Add(new AuditEventView(
                    FormatId(entry.Id),
                    entry.CreatedAt,
                    entry.Action.ToString(),
                    entry.User is null ? null : FormatId(entry.User.Id),
                    BoundOptional(entry.Reason, 512),
                    entry.Data?.GetType().Name ?? "none"));
                if (results.Count == limit)
                {
                    return results.ToImmutable();
                }
            }
        }

        return results.ToImmutable();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<GuildExpressionView>> ListGuildExpressionsAsync(
        string expressionType,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var normalizedType = NormalizeExpressionType(expressionType);
        return await httpApi.ListGuildExpressionsAsync(normalizedType, options.CancelToken).ConfigureAwait(false);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<GuildExpressionView> GetGuildExpressionAsync(
        string expressionType,
        string expressionId,
        CancellationToken cancellationToken)
    {
        var id = ParseSnowflake(expressionId);
        var all = await ListGuildExpressionsAsync(expressionType, cancellationToken).ConfigureAwait(false);
        return all.SingleOrDefault(expression => string.Equals(expression.Id, FormatId(id), StringComparison.Ordinal))
            ?? throw NotFound();
    }

    public async Task<ImmutableArray<ThreadView>> ListActiveThreadsAsync(
        CancellationToken cancellationToken) =>
        await httpApi.ListActiveThreadsAsync(cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<ThreadView>> ListArchivedThreadsAsync(
        string parentChannelId,
        string archiveType,
        int limit,
        DateTimeOffset? before,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        ValidateLimit(limit, 100);
        var channel = await guild.GetChannelAsync(ParseSnowflake(parentChannelId), options).ConfigureAwait(false);
        if (channel is not IThreadContainerChannel container)
        {
            throw NotFound();
        }

        var threads = archiveType.Trim().ToLowerInvariant() switch
        {
            "public" => await container.GetPublicArchivedThreadsAsync(limit, before, options).ConfigureAwait(false),
            "private" => await container.GetPrivateArchivedThreadsAsync(limit, before, options).ConfigureAwait(false),
            "joined_private" => await container.GetJoinedPrivateArchivedThreadsAsync(limit, before, options).ConfigureAwait(false),
            _ => throw InvalidArgument("Archive type must be public, private, or joined_private.")
        };
        return threads.OfType<RestThreadChannel>().Select(NormalizeThread).OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ThreadView> GetThreadAsync(
        string threadId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var channel = await guild.GetChannelAsync(ParseSnowflake(threadId), options).ConfigureAwait(false);
        if (channel is not RestThreadChannel thread)
        {
            throw NotFound();
        }

        var result = NormalizeThread(thread);
        var evidence = await channelStateReader.GetMutationEvidenceAsync(result.Id, cancellationToken).ConfigureAwait(false);
        return result with { StateDigest = evidence.StateDigest };
    }, cancellationToken).ConfigureAwait(false);

    public Task<ImmutableArray<VoiceRegionView>> ListVoiceRegionsAsync(
        string scope,
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var normalizedScope = scope?.Trim().ToLowerInvariant();
        var route = normalizedScope switch
        {
            "global" => "voice/regions",
            "guild" => $"guilds/{GuildId()}/regions",
            _ => throw InvalidArgument("Voice-region scope must be global or guild.")
        };
        var root = await transport.GetJsonAsync(route, cancellationToken).ConfigureAwait(false);
        return ParseJsonArray(root, item => new VoiceRegionView(
                RequiredJsonString(item, "id", 100),
                RequiredJsonString(item, "name", 100),
                OptionalJsonBoolean(item, "optimal") ?? false,
                OptionalJsonBoolean(item, "deprecated") ?? false,
                OptionalJsonBoolean(item, "custom") ?? false,
                normalizedScope), 100)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }, cancellationToken);

    public Task<ThreadMemberView> GetThreadMemberAsync(
        string threadId,
        string userId,
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var normalizedThreadId = ChannelApiState.NormalizeSnowflake(threadId);
        var normalizedUserId = ChannelApiState.NormalizeSnowflake(userId);
        var root = await transport.GetJsonAsync(
            $"channels/{normalizedThreadId}/thread-members/{normalizedUserId}?with_member=false",
            cancellationToken).ConfigureAwait(false);
        var result = new ThreadMemberView(
            OptionalJsonSnowflake(root, "id") ?? normalizedThreadId,
            RequiredJsonSnowflake(root, "user_id"),
            RequiredJsonTimestamp(root, "join_timestamp"),
            RequiredJsonNonNegativeInt32(root, "flags"));
        return result.ThreadId == normalizedThreadId && result.UserId == normalizedUserId
            ? result
            : throw NotFound();
    }, cancellationToken);

    public Task<ImmutableArray<InviteView>> ListChannelInvitesAsync(
        string channelId,
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var normalizedChannelId = ChannelApiState.NormalizeSnowflake(channelId);
        var root = await transport.GetJsonAsync(
            $"channels/{normalizedChannelId}/invites",
            cancellationToken).ConfigureAwait(false);
        return ParseJsonArray(root, item => ParseChannelInvite(item, normalizedChannelId), 1_000)
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ToImmutableArray();
    }, cancellationToken);

    public Task<ImmutableArray<StickerPackView>> ListStickerPacksAsync(
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var root = await transport.GetJsonAsync("sticker-packs", cancellationToken).ConfigureAwait(false);
        var packs = RequiredJsonArray(root, "sticker_packs");
        return ParseJsonArray(packs, ParseStickerPack, 100)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }, cancellationToken);

    public Task<StickerPackView> GetStickerPackAsync(
        string packId,
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var normalizedPackId = ChannelApiState.NormalizeSnowflake(packId);
        var root = await transport.GetJsonAsync(
            $"sticker-packs/{normalizedPackId}",
            cancellationToken).ConfigureAwait(false);
        var pack = ParseStickerPack(root);
        return pack.Id == normalizedPackId ? pack : throw NotFound();
    }, cancellationToken);

    public Task<ImmutableArray<DefaultSoundboardSoundView>> ListDefaultSoundboardSoundsAsync(
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var root = await transport.GetJsonAsync(
            "soundboard-default-sounds",
            cancellationToken).ConfigureAwait(false);
        return ParseJsonArray(root, item =>
            {
                var volume = RequiredJsonDouble(item, "volume");
                if (volume is < 0 or > 1)
                {
                    throw InvalidResponse("default sound volume");
                }

                return new DefaultSoundboardSoundView(
                    RequiredJsonSnowflake(item, "sound_id"),
                    RequiredJsonString(item, "name", 100),
                    volume,
                    OptionalJsonSnowflake(item, "emoji_id"),
                    OptionalJsonString(item, "emoji_name", 100));
            }, 100)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }, cancellationToken);

    public Task<GuildPreviewView> GetGuildPreviewAsync(
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var root = await transport.GetJsonAsync(
            $"guilds/{GuildId()}/preview",
            cancellationToken).ConfigureAwait(false);
        var guildId = RequiredJsonSnowflake(root, "id");
        if (guildId != GuildId())
        {
            throw NotFound();
        }

        return new GuildPreviewView(
            guildId,
            RequiredJsonString(root, "name", 100),
            OptionalJsonString(root, "description", 1_024),
            OptionalJsonString(root, "icon", 200),
            OptionalJsonString(root, "splash", 200),
            OptionalJsonString(root, "discovery_splash", 200),
            ParseStringArray(root, "features", 100),
            RequiredJsonNonNegativeInt32(root, "approximate_member_count"),
            RequiredJsonNonNegativeInt32(root, "approximate_presence_count"),
            RequiredJsonArray(root, "emojis").GetArrayLength(),
            RequiredJsonArray(root, "stickers").GetArrayLength());
    }, cancellationToken);

    public Task<GuildWidgetImageView> GetGuildWidgetImageAsync(
        string style,
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var normalizedStyle = style?.Trim().ToLowerInvariant();
        if (normalizedStyle is not "shield" and not "banner1" and not "banner2" and not "banner3" and not "banner4")
        {
            throw InvalidArgument("Widget image style must be shield or banner1 through banner4.");
        }

        var payload = await transport.GetBytesAsync(
            $"guilds/{GuildId()}/widget.png?style={normalizedStyle}",
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(payload.MediaType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidResponse("widget image media type");
        }

        return new GuildWidgetImageView(
            normalizedStyle,
            "image/png",
            payload.Content.Length,
            Convert.ToHexStringLower(SHA256.HashData(payload.Content)),
            Convert.ToBase64String(payload.Content));
    }, cancellationToken);

    public Task<BotGuildProfileView> GetBotGuildProfileAsync(
        CancellationToken cancellationToken) => ExecuteReferenceAsync(async () =>
    {
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        if (bot.GuildId != GuildId())
        {
            throw NotFound();
        }

        var root = await transport.GetJsonAsync(
            $"guilds/{GuildId()}/members/{ChannelApiState.NormalizeSnowflake(bot.BotUserId)}",
            cancellationToken).ConfigureAwait(false);

        if (root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object &&
            RequiredJsonSnowflake(user, "id") != bot.BotUserId)
        {
            throw NotFound();
        }

        var result = new BotGuildProfileView(
            bot.BotUserId,
            OptionalJsonString(root, "nick", 32),
            OptionalJsonString(root, "avatar", 200),
            OptionalJsonString(root, "banner", 200),
            OptionalJsonString(root, "bio", 190));
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }, cancellationToken);

    private static async Task<T> ExecuteReferenceAsync<T>(
        Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw DiscordErrorMapper.Map(exception, StewardErrorCodes.DiscordUnavailable);
        }
    }

    private async Task<T> ExecuteAsync<T>(
        Func<RestGuild, RequestOptions, Task<T>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            var guild = await session.GetGuildAsync(cancellationToken).ConfigureAwait(false);
            var result = await read(guild, new RequestOptions { CancelToken = cancellationToken }).ConfigureAwait(false);
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

    private static AutoModRuleView NormalizeAutoModRule(IAutoModRule rule)
    {
        var result = new AutoModRuleView(
            FormatId(rule.Id),
            FormatId(rule.GuildId),
            Bound(rule.Name, 100),
            rule.EventType.ToString(),
            rule.TriggerType.ToString(),
            rule.KeywordFilter.Select(value => Bound(value, 60)).ToImmutableArray(),
            rule.RegexPatterns.Select(value => Bound(value, 260)).ToImmutableArray(),
            rule.AllowList.Select(value => Bound(value, 60)).ToImmutableArray(),
            rule.Presets.Select(value => value.ToString()).ToImmutableArray(),
            rule.MentionTotalLimit,
            rule.MentionRaidProtectionEnabled,
            rule.Actions.Select(action => new AutoModActionView(
                action.Type.ToString(),
                action.ChannelId.HasValue ? FormatId(action.ChannelId.Value) : null,
                BoundOptional(action.CustomMessage.IsSpecified ? action.CustomMessage.Value : null, 150),
                action.TimeoutDuration.HasValue ? checked((int)action.TimeoutDuration.Value.TotalSeconds) : null)).ToImmutableArray(),
            rule.Enabled,
            FormatIds(rule.ExemptRoles),
            FormatIds(rule.ExemptChannels));
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }

    private static ScheduledEventView NormalizeEvent(IGuildScheduledEvent guildEvent)
    {
        var result = new ScheduledEventView(
            FormatId(guildEvent.Id),
            FormatId(guildEvent.GuildId),
            guildEvent.ChannelId.HasValue ? FormatId(guildEvent.ChannelId.Value) : null,
            Bound(guildEvent.Name, 100),
            BoundOptional(guildEvent.Description, 1_000),
            guildEvent.StartTime,
            guildEvent.EndTime,
            guildEvent.PrivacyLevel.ToString(),
            guildEvent.Status.ToString(),
            guildEvent.Type.ToString(),
            guildEvent.EntityId.HasValue ? FormatId(guildEvent.EntityId.Value) : null,
            BoundOptional(guildEvent.Location, 100),
            guildEvent.CoverImageId,
            guildEvent.RecurrenceRule?.ToString());
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }

    private static BanView NormalizeBan(RestBan ban) => new(
        FormatId(ban.User.Id),
        Bound(ban.User.Username, 100),
        BoundOptional(ban.Reason, 512),
        Operations.ResourceStateCanonicalizer.ComputeBanDigest(
            FormatId(ban.User.Id),
            BoundOptional(ban.Reason, 512),
            isBanned: true));

    private static InviteView NormalizeInvite(RestInviteMetadata invite)
    {
        var result = new InviteView(
            invite.Code,
            invite.GuildId.HasValue ? FormatId(invite.GuildId.Value) : string.Empty,
            FormatId(invite.ChannelId),
            Bound(invite.ChannelName, 100),
            invite.ChannelType.ToString(),
            invite.ExpiresAt,
            invite.IsTemporary,
            invite.MaxAge,
            invite.MaxUses,
            invite.Uses,
            invite.CreatedAt,
            invite.Roles.Select(role => FormatId(role.Id)).Order(StringComparer.Ordinal).ToImmutableArray());
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }

    private static IntegrationView NormalizeIntegration(RestIntegration integration) => new(
        FormatId(integration.Id),
        FormatId(integration.GuildId),
        Bound(integration.Name, 100),
        integration.Type,
        integration.IsEnabled,
        integration.IsSyncing,
        integration.RoleId.HasValue ? FormatId(integration.RoleId.Value) : null,
        integration.SyncedAt,
        integration.SubscriberCount,
        integration.IsRevoked,
        integration.Application is null ? null : FormatId(integration.Application.Id));

    private static WebhookView NormalizeWebhook(RestWebhook webhook)
    {
        var result = new WebhookView(
            FormatId(webhook.Id),
            webhook.GuildId.HasValue ? FormatId(webhook.GuildId.Value) : string.Empty,
            webhook.ChannelId.HasValue ? FormatId(webhook.ChannelId.Value) : null,
            Bound(webhook.Name ?? string.Empty, 80),
            webhook.Type.ToString(),
            webhook.ApplicationId.HasValue ? FormatId(webhook.ApplicationId.Value) : null,
            webhook.FollowedChannelId.HasValue ? FormatId(webhook.FollowedChannelId.Value) : null,
            BoundOptional(webhook.FollowedChannelName, 100),
            webhook.CreatedAt,
            webhook.AvatarId);
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }

    private static ThreadView NormalizeThread(RestThreadChannel thread) => new(
        FormatId(thread.Id),
        FormatId(thread.GuildId),
        FormatId(thread.ParentChannelId),
        Bound(thread.Name, 100),
        thread.IsArchived,
        thread.IsLocked,
        thread.IsInvitable,
        (int)thread.AutoArchiveDuration,
        thread.ArchiveTimestamp,
        thread.MemberCount,
        thread.MessageCount,
        FormatId(thread.OwnerId),
        FormatIds(thread.AppliedTags));

    private InviteView ParseChannelInvite(JsonElement root, string channelId)
    {
        var channel = RequiredJsonObject(root, "channel");
        var observedChannelId = RequiredJsonSnowflake(channel, "id");
        if (observedChannelId != channelId)
        {
            throw NotFound();
        }

        var guildId = OptionalJsonSnowflake(root, "guild_id");
        if (root.TryGetProperty("guild", out var guild) && guild.ValueKind == JsonValueKind.Object)
        {
            guildId = RequiredJsonSnowflake(guild, "id");
        }

        if (guildId is not null && guildId != GuildId())
        {
            throw NotFound();
        }

        var grantedRoleIds = root.TryGetProperty("role_ids", out var roleIds) && roleIds.ValueKind == JsonValueKind.Array
            ? roleIds.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
                ? ChannelApiState.NormalizeSnowflake(value.GetString() ?? string.Empty)
                : throw InvalidResponse("invite role ID")).Order(StringComparer.Ordinal).ToImmutableArray()
            : root.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
                ? roles.EnumerateArray().Select(role => RequiredJsonSnowflake(role, "id"))
                    .Order(StringComparer.Ordinal).ToImmutableArray()
                : [];
        var result = new InviteView(
            NormalizeCode(RequiredJsonString(root, "code", 100)),
            guildId ?? GuildId(),
            observedChannelId,
            RequiredJsonString(channel, "name", 100),
            ChannelTypeName(RequiredJsonNonNegativeInt32(channel, "type")),
            OptionalJsonTimestamp(root, "expires_at"),
            OptionalJsonBoolean(root, "temporary") ?? false,
            OptionalJsonNonNegativeInt32(root, "max_age"),
            OptionalJsonNonNegativeInt32(root, "max_uses"),
            OptionalJsonNonNegativeInt32(root, "uses"),
            OptionalJsonTimestamp(root, "created_at"),
            grantedRoleIds);
        return result with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }

    private static StickerPackView ParseStickerPack(JsonElement root)
    {
        var packId = RequiredJsonSnowflake(root, "id");
        return new StickerPackView(
            packId,
            RequiredJsonString(root, "name", 100),
            OptionalJsonString(root, "description", 1_024) ?? string.Empty,
            RequiredJsonSnowflake(root, "sku_id"),
            OptionalJsonSnowflake(root, "cover_sticker_id"),
            OptionalJsonSnowflake(root, "banner_asset_id"),
            ParseJsonArray(RequiredJsonArray(root, "stickers"), sticker => new StickerPackStickerView(
                    RequiredJsonSnowflake(sticker, "id"),
                    OptionalJsonSnowflake(sticker, "pack_id") ?? packId,
                    RequiredJsonString(sticker, "name", 100),
                    OptionalJsonString(sticker, "description", 1_024),
                    (OptionalJsonString(sticker, "tags", 4_096) ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Order(StringComparer.Ordinal)
                        .ToImmutableArray(),
                    StickerFormat(RequiredJsonNonNegativeInt32(sticker, "format_type")),
                    OptionalJsonNonNegativeInt32(sticker, "sort_value")), 200)
                .OrderBy(sticker => sticker.Id, StringComparer.Ordinal)
                .ToImmutableArray());
    }

    private static string StickerFormat(int value) => value switch
    {
        1 => "png",
        2 => "apng",
        3 => "lottie",
        4 => "gif",
        _ => "unknown"
    };

    private static string ChannelTypeName(int value) => value switch
    {
        0 => "text",
        2 => "voice",
        5 => "announcement",
        13 => "stage",
        15 => "forum",
        16 => "media",
        _ => "unknown"
    };

    private static ImmutableArray<T> ParseJsonArray<T>(
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
                throw new StewardException(StewardErrorCodes.ResultTooLarge, "Discord returned too many records.");
            }
        }

        return values.ToImmutable();
    }

    private static JsonElement RequiredJsonArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw InvalidResponse(name);

    private static JsonElement RequiredJsonObject(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw InvalidResponse(name);

    private static string RequiredJsonSnowflake(JsonElement root, string name) =>
        OptionalJsonSnowflake(root, name) ?? throw InvalidResponse(name);

    private static string? OptionalJsonSnowflake(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? ChannelApiState.NormalizeSnowflake(value.GetString() ?? string.Empty)
            : throw InvalidResponse(name);
    }

    private static string RequiredJsonString(JsonElement root, string name, int maximum) =>
        OptionalJsonString(root, name, maximum) ?? throw InvalidResponse(name);

    private static string? OptionalJsonString(JsonElement root, string name, int maximum)
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
        return text.Length <= maximum ? text : throw InvalidResponse(name);
    }

    private static bool? OptionalJsonBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw InvalidResponse(name);
    }

    private static int RequiredJsonNonNegativeInt32(JsonElement root, string name) =>
        OptionalJsonNonNegativeInt32(root, name) ?? throw InvalidResponse(name);

    private static int? OptionalJsonNonNegativeInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetInt32(out var parsed) && parsed >= 0 ? parsed : throw InvalidResponse(name);
    }

    private static double RequiredJsonDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) && double.IsFinite(parsed)
            ? parsed
            : throw InvalidResponse(name);

    private static DateTimeOffset RequiredJsonTimestamp(JsonElement root, string name) =>
        OptionalJsonTimestamp(root, name) ?? throw InvalidResponse(name);

    private static DateTimeOffset? OptionalJsonTimestamp(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : throw InvalidResponse(name);
    }

    private static ImmutableArray<string> ParseStringArray(JsonElement root, string name, int maximum)
    {
        var array = RequiredJsonArray(root, name);
        if (array.GetArrayLength() > maximum)
        {
            throw new StewardException(StewardErrorCodes.ResultTooLarge, "Discord returned too many string values.");
        }

        return array.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
                ? RequiredBoundedValue(value.GetString(), 100)
                : throw InvalidResponse(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string RequiredBoundedValue(string? value, int maximum) =>
        !string.IsNullOrEmpty(value) && value.Length <= maximum ? value : throw InvalidResponse("string");

    private static string NormalizeExpressionType(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "emoji" or "sticker" or "soundboard"
            ? normalized
            : throw InvalidArgument("Expression type must be emoji, sticker, or soundboard.");
    }

    private static string NormalizeCode(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            ? value
            : throw InvalidArgument("Invite code is invalid.");

    private static ulong ParseSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw InvalidArgument("Discord ID must be a positive snowflake string.");

    private static string FormatId(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static ImmutableArray<string> FormatIds(IEnumerable<ulong> values) =>
        values.Select(FormatId).Order(StringComparer.Ordinal).ToImmutableArray();

    private static void ValidateLimit(int limit, int maximum)
    {
        if (limit is < 1 || limit > maximum)
        {
            throw InvalidArgument($"Limit must be between 1 and {maximum.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static string Bound(string? value, int maximum) =>
        (value ?? string.Empty).Length <= maximum ? value ?? string.Empty : (value ?? string.Empty)[..maximum];

    private static string? BoundOptional(string? value, int maximum) =>
        value is null ? null : Bound(value, maximum);

    private static StewardException NotFound() => new(
        StewardErrorCodes.ResourceNotFound,
        "The requested Discord resource was not found in the configured guild.");

    private static StewardException InvalidArgument(string message) => new(
        StewardErrorCodes.InvalidArgument,
        message);

    private string GuildId() => settings.GuildId?.ToString(CultureInfo.InvariantCulture)
        ?? throw DiscordErrorMapper.NotConfigured();

    private static StewardException InvalidResponse(string resource) => new(
        StewardErrorCodes.DiscordUnavailable,
        $"Discord returned invalid {resource} data.");
}
