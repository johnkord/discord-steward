using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

internal sealed class MessageMutationHandler(
    DiscordApiTransport transport,
    IDiscordMessageReader messageReader,
    IDiscordBotInspector botInspector,
    IAssetVault assetVault,
    StewardRuntimeSettings settings,
    TimeProvider timeProvider) : ICatalogMutationFamilyHandler
{
    private const int CrosspostedFlag = 1 << 0;
    private const int SuppressEmbedsFlag = 1 << 2;
    private const int SuppressNotificationsFlag = 1 << 12;
    private const int VoiceMessageFlag = 1 << 13;
    private const int ComponentsV2Flag = 1 << 15;
    private static readonly ImmutableHashSet<string> ToolNames = new[]
    {
        "send_message",
        "edit_own_message",
        "delete_message",
        "bulk_delete_messages",
        "crosspost_announcement_message",
        "set_message_pinned",
        "set_own_reaction",
        "remove_user_reaction",
        "clear_message_reactions",
        "create_poll",
        "end_poll"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken) => invocation.ToolName switch
        {
            "send_message" => await PrepareSendAsync(invocation, cancellationToken).ConfigureAwait(false),
            "edit_own_message" => await PrepareEditAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_message" => await PrepareDeleteAsync(invocation, cancellationToken).ConfigureAwait(false),
            "bulk_delete_messages" => await PrepareBulkDeleteAsync(invocation, cancellationToken).ConfigureAwait(false),
            "crosspost_announcement_message" => await PrepareCrosspostAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_message_pinned" => await PreparePinAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_own_reaction" => await PrepareOwnReactionAsync(invocation, cancellationToken).ConfigureAwait(false),
            "remove_user_reaction" => await PrepareRemoveReactionAsync(invocation, cancellationToken).ConfigureAwait(false),
            "clear_message_reactions" => await PrepareClearReactionsAsync(invocation, cancellationToken).ConfigureAwait(false),
            "create_poll" => await PrepareCreatePollAsync(invocation, cancellationToken).ConfigureAwait(false),
            "end_poll" => await PrepareEndPollAsync(invocation, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response = invocation.ToolName switch
        {
            "send_message" => await WriteSendAsync(invocation, cancellationToken).ConfigureAwait(false),
            "edit_own_message" => await WriteEditAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
            "delete_message" => await DeleteMessageAsync(invocation, cancellationToken).ConfigureAwait(false),
            "bulk_delete_messages" => await WriteBulkDeleteAsync(invocation, cancellationToken).ConfigureAwait(false),
            "crosspost_announcement_message" => await transport.SendJsonAsync(
                HttpMethod.Post,
                MessageRoute(invocation, suffix: "/crosspost"),
                JsonSerializer.SerializeToElement(new { }, CatalogJson.Options),
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false),
            "set_message_pinned" => await WritePinAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_own_reaction" => await WriteOwnReactionAsync(invocation, cancellationToken).ConfigureAwait(false),
            "remove_user_reaction" => await WriteRemoveReactionAsync(invocation, cancellationToken).ConfigureAwait(false),
            "clear_message_reactions" => await WriteClearReactionsAsync(invocation, cancellationToken).ConfigureAwait(false),
            "create_poll" => await WriteCreatePollAsync(invocation, cancellationToken).ConfigureAwait(false),
            "end_poll" => await transport.SendJsonAsync(
                HttpMethod.Post,
                PollRoute(invocation, "/expire"),
                JsonSerializer.SerializeToElement(new { }, CatalogJson.Options),
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };
        return new CatalogMutationWriteReceipt(response?.GetRawText());
    }

    public async Task<CatalogMutationObservation> ObserveAsync(
        CatalogMutationInvocation invocation,
        string beforeStateJson,
        string desiredStateJson,
        string? priorReceiptJson,
        CancellationToken cancellationToken)
    {
        if (invocation.ToolName is "send_message" or "create_poll")
        {
            var createdId = CreatedMessageId(priorReceiptJson);
            var channelId = invocation.ResourceId;
            var message = await TryGetMessageAsync(channelId, createdId, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new MessageExistenceState(channelId, createdId, Exists: false)));
            }

            if (invocation.ToolName == "send_message")
            {
                var desired = DeserializeState<CreatedMessageExpectation>(desiredStateJson);
                return new CatalogMutationObservation(
                    desired.Matches(message)
                        ? CatalogMutationObservationKind.DesiredState
                        : CatalogMutationObservationKind.UnexpectedState,
                    State(MessageMutationState.FromMessage(message)));
            }

            var pollDesired = DeserializeState<CreatedPollExpectation>(desiredStateJson);
            return new CatalogMutationObservation(
                pollDesired.Matches(message)
                    ? CatalogMutationObservationKind.DesiredState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(MessageMutationState.FromMessage(message)));
        }

        if (invocation.ToolName == "edit_own_message")
        {
            var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
            var message = await TryGetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new MessageExistenceState(channelId, messageId, Exists: false)));
            }

            var desired = DeserializeState<EditedMessageExpectation>(desiredStateJson);
            var observed = MessageMutationState.FromMessage(message);
            return new CatalogMutationObservation(
                desired.Matches(message)
                    ? CatalogMutationObservationKind.DesiredState
                    : observed.EquivalentTo(DeserializeState<MessageMutationState>(beforeStateJson))
                        ? CatalogMutationObservationKind.BeforeState
                        : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (invocation.ToolName == "delete_message")
        {
            var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
            var exists = await transport.TryGetJsonAsync(
                $"channels/{channelId}/messages/{messageId}",
                cancellationToken).ConfigureAwait(false) is not null;
            return Observation(
                new MessageExistenceState(channelId, messageId, exists),
                beforeStateJson,
                desiredStateJson);
        }

        if (invocation.ToolName == "bulk_delete_messages")
        {
            var input = Arguments<BulkDeleteMessagesInput>(invocation);
            BulkMessageExistenceState observed;
            for (var attempt = 0; ; attempt++)
            {
                observed = await ObserveBulkMessageExistenceAsync(invocation, input, cancellationToken).ConfigureAwait(false);
                var hasExisting = observed.Messages.Any(message => message.Exists);
                var hasDeleted = observed.Messages.Any(message => !message.Exists);
                if (!hasExisting || !hasDeleted || attempt >= 2)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            return Observation(observed, beforeStateJson, desiredStateJson);
        }

        if (invocation.ToolName is "set_own_reaction" or "remove_user_reaction")
        {
            var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
            string emoji;
            string userId;
            bool desiredPresence;
            if (invocation.ToolName == "set_own_reaction")
            {
                var input = Arguments<SetOwnReactionInput>(invocation);
                emoji = input.Emoji;
                userId = (await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false)).BotUserId;
                desiredPresence = input.Present;
            }
            else
            {
                var input = Arguments<RemoveUserReactionInput>(invocation);
                emoji = input.Emoji;
                userId = input.UserId;
                desiredPresence = false;
            }

            var present = await HasReactionAsync(channelId, messageId, emoji, userId, cancellationToken).ConfigureAwait(false);
            var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
            var observed = new ReactionPresenceObservationState(
                channelId,
                messageId,
                NormalizeEmoji(emoji),
                NormalizeSnowflake(userId),
                present,
                message.StateDigest,
                message.ReactionStateDigest);
            var desired = DeserializeState<ReactionPresenceState>(desiredStateJson);
            var before = DeserializeState<ReactionPresenceState>(beforeStateJson);
            return new CatalogMutationObservation(
                present == desiredPresence && observed.UserId == desired.UserId
                    ? CatalogMutationObservationKind.DesiredState
                    : observed.Matches(before) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (invocation.ToolName == "clear_message_reactions")
        {
            var input = Arguments<ClearMessageReactionsInput>(invocation);
            var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
            var cleared = await ReactionsClearedAsync(channelId, messageId, input.Emoji, cancellationToken).ConfigureAwait(false);
            var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
            var observed = new ReactionClearObservationState(
                channelId,
                messageId,
                input.Emoji is null ? null : NormalizeEmoji(input.Emoji),
                cleared,
                message.StateDigest,
                message.ReactionStateDigest);
            var before = DeserializeState<ReactionClearState>(beforeStateJson);
            return new CatalogMutationObservation(
                cleared ? CatalogMutationObservationKind.DesiredState :
                    observed.Matches(before)
                        ? CatalogMutationObservationKind.BeforeState
                        : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (invocation.ToolName == "end_poll")
        {
            var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
            var message = await TryGetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new MessageExistenceState(channelId, messageId, Exists: false)));
            }

            var observed = MessageMutationState.FromMessage(message);
            var before = DeserializeState<MessageMutationState>(beforeStateJson);
            var desired = DeserializeState<MessageMutationState>(desiredStateJson);
            var finalizedAsDesired = observed.PollFinalized == desired.PollFinalized &&
                observed.ChannelId == desired.ChannelId &&
                observed.MessageId == desired.MessageId &&
                observed.AuthorId == desired.AuthorId &&
                observed.Content == desired.Content;
            return new CatalogMutationObservation(
                finalizedAsDesired
                    ? CatalogMutationObservationKind.DesiredState
                    : observed.EquivalentTo(before)
                        ? CatalogMutationObservationKind.BeforeState
                        : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        var (observedChannelId, observedMessageId) = ParseMessageResource(invocation.ResourceId);
        var observedMessage = await TryGetMessageAsync(observedChannelId, observedMessageId, cancellationToken).ConfigureAwait(false);
        if (observedMessage is null)
        {
            return new CatalogMutationObservation(
                CatalogMutationObservationKind.UnexpectedState,
                State(new MessageExistenceState(observedChannelId, observedMessageId, Exists: false)));
        }

        var observedState = MessageMutationState.FromMessage(observedMessage);
        var beforeState = DeserializeState<MessageMutationState>(beforeStateJson);
        var desiredState = DeserializeState<MessageMutationState>(desiredStateJson);
        return new CatalogMutationObservation(
            observedState.EquivalentTo(desiredState)
                ? CatalogMutationObservationKind.DesiredState
                : observedState.EquivalentTo(beforeState)
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
            State(observedState));
    }

    private async Task<BulkMessageExistenceState> ObserveBulkMessageExistenceAsync(
        CatalogMutationInvocation invocation,
        BulkDeleteMessagesInput input,
        CancellationToken cancellationToken)
    {
        var channelId = NormalizeSnowflake(invocation.ResourceId);
        var states = ImmutableArray.CreateBuilder<MessageExistenceState>();
        foreach (var item in input.Messages)
        {
            var messageId = NormalizeSnowflake(item.MessageId);
            var exists = await transport.TryGetJsonAsync(
                $"channels/{channelId}/messages/{messageId}",
                cancellationToken).ConfigureAwait(false) is not null;
            states.Add(new MessageExistenceState(channelId, messageId, exists));
        }

        return new BulkMessageExistenceState(
            states.OrderBy(item => item.MessageId, StringComparer.Ordinal).ToImmutableArray());
    }

    private async Task<CatalogMutationPreparation> PrepareSendAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SendMessageInput>(invocation);
        var channel = await GetWritableMessageChannelAsync(invocation.ResourceId, input.ExpectedChannelStateDigest, cancellationToken).ConfigureAwait(false);
        var mode = NormalizeMode(input.Mode);
        var content = input.Content is null ? null : NormalizeContent(input.Content, allowEmpty: true);
        MessageView? source = null;
        if (mode is "reply" or "forward")
        {
            if (input.SourceChannelId is null || input.SourceMessageId is null || input.ExpectedSourceMessageStateDigest is null)
            {
                throw InvalidArgument("Reply and forward modes require exact source channel, message, and state digest.");
            }

            var sourceChannelId = NormalizeSnowflake(input.SourceChannelId);
            if (mode == "reply" && sourceChannelId != channel.Id)
            {
                throw InvalidArgument("Replies must reference a message in the destination channel.");
            }

            source = await messageReader.GetMessageAsync(sourceChannelId, input.SourceMessageId, cancellationToken).ConfigureAwait(false);
            EnsureDigest(input.ExpectedSourceMessageStateDigest, source.StateDigest);
        }
        else if (input.SourceChannelId is not null || input.SourceMessageId is not null || input.ExpectedSourceMessageStateDigest is not null)
        {
            throw InvalidArgument("New-message mode cannot include source message fields.");
        }

        var assets = await GetAssetsAsync(input.AssetIds, cancellationToken).ConfigureAwait(false);
        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds) ?? [];
        RichMessagePayloadNormalizer.ValidateEmbedAttachmentReferences(embeds, assets.Select(asset => asset.Filename));
        if (input.SuppressEmbeds && !embeds.IsEmpty)
        {
            throw InvalidArgument("Explicit rich embeds cannot be combined with suppress_embeds.");
        }

        var stickerIds = RichMessagePayloadNormalizer.NormalizeStickerIds(input.StickerIds);
        await ValidateGuildStickersAsync(stickerIds, cancellationToken).ConfigureAwait(false);
        var theme = RichMessagePayloadNormalizer.NormalizeTheme(input.SharedClientTheme);
        var voice = input.VoiceMessage is null ? null : RichMessagePayloadNormalizer.NormalizeVoiceMessage(input.VoiceMessage);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames);
        if (components.HasValue &&
            (content is not null || !embeds.IsEmpty || !stickerIds.IsEmpty || theme is not null || voice is not null || input.Tts || mode == "forward"))
        {
            throw InvalidArgument("Components v2 messages can contain only static components and their referenced assets.");
        }

        if (voice is not null)
        {
            var voiceAsset = assets.SingleOrDefault(asset => asset.AssetId == voice.AssetId);
            if (assets.Length != 1 || voiceAsset is null || !voiceAsset.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
                content is not null || mode != "new" || !embeds.IsEmpty || !stickerIds.IsEmpty || theme is not null || input.Tts ||
                input.SuppressEmbeds || components.HasValue)
            {
                throw InvalidArgument("Voice messages require one exact audio asset and no other message content.");
            }
        }

        if (input.Tts && string.IsNullOrEmpty(content))
        {
            throw InvalidArgument("TTS messages require explicit text content.");
        }

        if (content is null && assets.IsEmpty && mode != "forward" && embeds.IsEmpty && stickerIds.IsEmpty &&
            theme is null && !components.HasValue)
        {
            throw InvalidArgument("A message requires content, rich content, an approved asset, or forward mode.");
        }

        ValidateAllowedMentions(input.AllowedMentions);
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        if (input.Tts && !bot.EffectivePermissions.Contains("SendTTSMessages", StringComparer.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.DiscordPermissionDenied,
                "The Steward bot lacks Send TTS Messages for this explicit TTS call.");
        }

        var desired = new CreatedMessageExpectation(
            channel.Id,
            bot.BotUserId,
            mode,
            content ?? string.Empty,
            assets.Select(asset => new ExpectedAttachment(
                    asset.Filename,
                    asset.ByteLength,
                    DurationSeconds: voice?.AssetId == asset.AssetId ? voice.DurationSeconds : null,
                    Waveform: voice?.AssetId == asset.AssetId ? voice.WaveformBase64 : null))
                .ToImmutableArray(),
            input.SuppressNotifications,
            input.SuppressEmbeds,
            voice is not null,
            components.HasValue,
            new RichPayloadState(
                input.Tts,
                stickerIds,
                RichMessagePayloadNormalizer.CanonicalEmbedsJson(embeds),
                components?.CanonicalJson ?? "[]",
                theme));
        return new CatalogMutationPreparation(State(channel), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareEditAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<EditOwnMessageInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var message = await GetOwnedMessageAsync(channelId, messageId, input.ExpectedMessageStateDigest, cancellationToken).ConfigureAwait(false);
        var retained = input.RetainedAttachmentIds.Select(NormalizeSnowflake).ToImmutableArray();
        if (retained.Distinct(StringComparer.Ordinal).Count() != retained.Length ||
            retained.Any(id => !message.Attachments.Any(attachment => attachment.Id == id)))
        {
            throw InvalidArgument("Retained attachment IDs must be unique and belong to the current message.");
        }

        var assets = await GetAssetsAsync(input.NewAssetIds, cancellationToken).ConfigureAwait(false);
        ValidateTotalAttachmentBytes(
            message.Attachments.Where(item => retained.Contains(item.Id, StringComparer.Ordinal)).Sum(item => item.SizeBytes) +
            assets.Sum(item => item.ByteLength));
        ValidateAllowedMentions(input.AllowedMentions);
        var clear = NormalizeEditClear(input.Clear);
        if (input.Content is not null && clear.Contains("content"))
        {
            throw InvalidArgument("Message content cannot be set and cleared in the same edit.");
        }

        var desiredContent = clear.Contains("content")
            ? string.Empty
            : input.Content is null
                ? message.Content
                : NormalizeContent(input.Content, allowEmpty: true);
        var allFilenames = message.Attachments
            .Where(item => retained.Contains(item.Id, StringComparer.Ordinal))
            .Select(item => item.Filename)
            .Concat(assets.Select(asset => asset.Filename))
            .ToArray();
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds);
        if (embeds.HasValue)
        {
            RichMessagePayloadNormalizer.ValidateEmbedAttachmentReferences(embeds.Value, allFilenames);
            if (input.SuppressEmbeds == true && !embeds.Value.IsEmpty)
            {
                throw InvalidArgument("Explicit rich embeds cannot be combined with suppress_embeds.");
            }
        }

        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames, allowEmpty: true);
        var beforeUsesComponentsV2 = (message.Flags & ComponentsV2Flag) != 0;
        var setsComponentsV2 = components.HasValue && components.Value.Count > 0;
        if (beforeUsesComponentsV2 && components.HasValue && components.Value.Count == 0)
        {
            throw InvalidArgument("Components v2 cannot be removed after it is set on a message.");
        }

        var desiredEmbedsJson = embeds.HasValue
            ? RichMessagePayloadNormalizer.CanonicalEmbedsJson(embeds.Value)
            : message.EmbedsJson;
        var desiredComponentsJson = components?.CanonicalJson ?? message.ComponentsJson;
        if ((beforeUsesComponentsV2 || setsComponentsV2) &&
            (!string.IsNullOrEmpty(desiredContent) || desiredEmbedsJson != "[]"))
        {
            throw InvalidArgument("Components v2 edits require empty content and embeds.");
        }

        if (beforeUsesComponentsV2 && !assets.IsEmpty && !components.HasValue)
        {
            throw InvalidArgument("Appending assets to a Components v2 message requires an explicit component replacement.");
        }

        var before = MessageMutationState.FromMessage(message);
        var desiredAttachments = message.Attachments
            .Where(item => retained.Contains(item.Id, StringComparer.Ordinal))
            .Select(ExpectedAttachment.FromView)
            .Concat(assets.Select(item => new ExpectedAttachment(item.Filename, item.ByteLength)))
            .OrderBy(item => item.Filename, StringComparer.Ordinal)
            .ThenBy(item => item.SizeBytes)
            .ToImmutableArray();
        return new CatalogMutationPreparation(
            State(before),
            State(new EditedMessageExpectation(
                channelId,
                messageId,
                desiredContent,
                desiredAttachments,
                ApplyEditableFlags(message.Flags, input.SuppressEmbeds, setsComponentsV2),
                new RichPayloadState(
                    message.IsTts,
                    message.StickerIds.IsDefault ? [] : message.StickerIds,
                    desiredEmbedsJson,
                    desiredComponentsJson,
                    message.SharedClientTheme))));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteMessageInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMessageStateDigest, message.StateDigest);
        return new CatalogMutationPreparation(
            State(new MessageExistenceState(channelId, messageId, Exists: true)),
            State(new MessageExistenceState(channelId, messageId, Exists: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareBulkDeleteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<BulkDeleteMessagesInput>(invocation);
        if (input.Messages.Length is < 2 or > 100 ||
            input.Messages.Select(item => item.MessageId).Distinct(StringComparer.Ordinal).Count() != input.Messages.Length)
        {
            throw InvalidArgument("Bulk deletion requires 2 to 100 unique exact message IDs.");
        }

        var channelId = NormalizeSnowflake(invocation.ResourceId);
        var before = ImmutableArray.CreateBuilder<MessageExistenceState>();
        foreach (var item in input.Messages)
        {
            var message = await messageReader.GetMessageAsync(channelId, item.MessageId, cancellationToken).ConfigureAwait(false);
            EnsureDigest(item.StateDigest, message.StateDigest);
            if (timeProvider.GetUtcNow() - message.CreatedAt >= TimeSpan.FromDays(14))
            {
                throw InvalidArgument("Bulk delete cannot include messages 14 days old or older.");
            }

            before.Add(new MessageExistenceState(channelId, message.Id, Exists: true));
        }

        var ordered = before.OrderBy(item => item.MessageId, StringComparer.Ordinal).ToImmutableArray();
        return new CatalogMutationPreparation(
            State(new BulkMessageExistenceState(ordered)),
            State(new BulkMessageExistenceState(ordered.Select(item => item with { Exists = false }).ToImmutableArray())));
    }

    private async Task<CatalogMutationPreparation> PrepareCrosspostAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CrosspostAnnouncementMessageInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var channel = ChannelApiState.Parse(await transport.GetJsonAsync($"channels/{channelId}", cancellationToken).ConfigureAwait(false));
        if (channel.GuildId != GuildId() || channel.Type != 5)
        {
            throw InvalidArgument("Crossposting requires an announcement channel in the configured guild.");
        }

        var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMessageStateDigest, message.StateDigest);
        var before = MessageMutationState.FromMessage(message);
        return new CatalogMutationPreparation(State(before), State(before with { Flags = before.Flags | CrosspostedFlag }));
    }

    private async Task<CatalogMutationPreparation> PreparePinAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetMessagePinnedInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMessageStateDigest, message.StateDigest);
        var before = MessageMutationState.FromMessage(message);
        return new CatalogMutationPreparation(State(before), State(before with { IsPinned = input.Pinned }));
    }

    private async Task<CatalogMutationPreparation> PrepareOwnReactionAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetOwnReactionInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMessageStateDigest, message.StateDigest);
        EnsureDigest(input.ExpectedReactionStateDigest, message.ReactionStateDigest);
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var present = await HasReactionAsync(channelId, messageId, input.Emoji, bot.BotUserId, cancellationToken).ConfigureAwait(false);
        return new CatalogMutationPreparation(
            State(new ReactionPresenceState(channelId, messageId, NormalizeEmoji(input.Emoji), bot.BotUserId, present)),
            State(new ReactionPresenceState(channelId, messageId, NormalizeEmoji(input.Emoji), bot.BotUserId, input.Present)));
    }

    private async Task<CatalogMutationPreparation> PrepareRemoveReactionAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<RemoveUserReactionInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMessageStateDigest, message.StateDigest);
        EnsureDigest(input.ExpectedReactionStateDigest, message.ReactionStateDigest);
        var userId = NormalizeSnowflake(input.UserId);
        var present = await HasReactionAsync(channelId, messageId, input.Emoji, userId, cancellationToken).ConfigureAwait(false);
        if (!present)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The exact user reaction is no longer present.");
        }

        return new CatalogMutationPreparation(
            State(new ReactionPresenceState(channelId, messageId, NormalizeEmoji(input.Emoji), userId, Present: true)),
            State(new ReactionPresenceState(channelId, messageId, NormalizeEmoji(input.Emoji), userId, Present: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareClearReactionsAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ClearMessageReactionsInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMessageStateDigest, message.StateDigest);
        EnsureDigest(input.ExpectedReactionStateDigest, message.ReactionStateDigest);
        var emoji = input.Emoji is null ? null : NormalizeEmoji(input.Emoji);
        var alreadyCleared = await ReactionsClearedAsync(channelId, messageId, input.Emoji, cancellationToken).ConfigureAwait(false);
        if (alreadyCleared)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The requested reactions are already clear.");
        }

        return new CatalogMutationPreparation(
            State(new ReactionClearState(channelId, messageId, emoji, Cleared: false, message.ReactionStateDigest)),
            State(new ReactionClearState(channelId, messageId, emoji, Cleared: true, ReactionStateDigest: string.Empty)));
    }

    private async Task<CatalogMutationPreparation> PrepareCreatePollAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreatePollInput>(invocation);
        var channel = await GetWritableMessageChannelAsync(invocation.ResourceId, input.ExpectedChannelStateDigest, cancellationToken).ConfigureAwait(false);
        var answers = NormalizePollAnswers(input.Answers);
        if (input.DurationHours is < 1 or > 768)
        {
            throw InvalidArgument("Poll duration must be between 1 and 768 hours.");
        }

        ValidateAllowedMentions(input.AllowedMentions);
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var desired = new CreatedPollExpectation(
            channel.Id,
            bot.BotUserId,
            input.Content is null ? string.Empty : NormalizeContent(input.Content, allowEmpty: true),
            NormalizePollText(input.Question, 300),
            answers.Select(answer => answer.Text).ToImmutableArray(),
            input.AllowMultiselect,
            input.SuppressNotifications);
        return new CatalogMutationPreparation(State(channel), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareEndPollAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<EndPollInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var message = await GetOwnedMessageAsync(channelId, messageId, input.ExpectedMessageStateDigest, cancellationToken).ConfigureAwait(false);
        if (message.Poll is null)
        {
            throw InvalidArgument("The exact message does not contain a poll.");
        }

        EnsureDigest(input.ExpectedPollStateDigest, message.Poll.StateDigest);
        if (message.Poll.IsFinalized)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The poll is already finalized.");
        }

        var before = MessageMutationState.FromMessage(message);
        return new CatalogMutationPreparation(State(before), State(before with { PollFinalized = true }));
    }

    private async Task<JsonElement?> WriteSendAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SendMessageInput>(invocation);
        var assets = await GetAssetsAsync(input.AssetIds, cancellationToken).ConfigureAwait(false);
        var payload = MessagePayload(input, assets);
        return await SendMessagePayloadAsync(
            HttpMethod.Post,
            $"channels/{NormalizeSnowflake(invocation.ResourceId)}/messages",
            payload,
            assets,
            invocation,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> WriteEditAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<EditOwnMessageInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var assets = await GetAssetsAsync(input.NewAssetIds, cancellationToken).ConfigureAwait(false);
        var attachments = input.RetainedAttachmentIds.Select(id => (object)new Dictionary<string, object?>
        {
            ["id"] = NormalizeSnowflake(id)
        }).ToList();
        for (var index = 0; index < assets.Length; index++)
        {
            attachments.Add(new Dictionary<string, object?>
            {
                ["id"] = index,
                ["filename"] = assets[index].Filename
            });
        }

        var desired = DeserializeState<EditedMessageExpectation>(preparation.DesiredStateJson);
        var clear = NormalizeEditClear(input.Clear);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["allowed_mentions"] = AllowedMentionsPayload(input.AllowedMentions),
            ["attachments"] = attachments
        };
        if (input.Content is not null || clear.Contains("content"))
        {
            payload["content"] = clear.Contains("content") ? null : NormalizeContent(input.Content!, allowEmpty: true);
        }

        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds);
        if (embeds.HasValue)
        {
            payload["embeds"] = RichMessagePayloadNormalizer.EmbedsPayload(embeds.Value);
        }

        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames, allowEmpty: true);
        if (components.HasValue)
        {
            payload["components"] = components.Value.Payload;
        }

        if (input.SuppressEmbeds.HasValue || components.HasValue && components.Value.Count > 0)
        {
            payload["flags"] = desired.Flags;
        }

        return await SendMessagePayloadAsync(
            HttpMethod.Patch,
            $"channels/{channelId}/messages/{messageId}",
            payload,
            assets,
            invocation,
            cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonElement?> DeleteMessageAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken) => transport.SendJsonAsync(
            HttpMethod.Delete,
            MessageRoute(invocation),
            body: null,
            AuditReason(invocation),
            cancellationToken);

    private Task<JsonElement?> WriteBulkDeleteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<BulkDeleteMessagesInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Post,
            $"channels/{NormalizeSnowflake(invocation.ResourceId)}/messages/bulk-delete",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["messages"] = input.Messages.Select(item => NormalizeSnowflake(item.MessageId)).ToArray()
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WritePinAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetMessagePinnedInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        return transport.SendJsonAsync(
            input.Pinned ? HttpMethod.Put : HttpMethod.Delete,
            $"channels/{channelId}/messages/pins/{messageId}",
            body: null,
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteOwnReactionAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetOwnReactionInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        return transport.SendJsonAsync(
            input.Present ? HttpMethod.Put : HttpMethod.Delete,
            $"channels/{channelId}/messages/{messageId}/reactions/{NormalizeEmoji(input.Emoji)}/@me",
            body: null,
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteRemoveReactionAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<RemoveUserReactionInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        return transport.SendJsonAsync(
            HttpMethod.Delete,
            $"channels/{channelId}/messages/{messageId}/reactions/{NormalizeEmoji(input.Emoji)}/{NormalizeSnowflake(input.UserId)}",
            body: null,
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteClearReactionsAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ClearMessageReactionsInput>(invocation);
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        var route = $"channels/{channelId}/messages/{messageId}/reactions";
        if (input.Emoji is not null)
        {
            route += $"/{NormalizeEmoji(input.Emoji)}";
        }

        return transport.SendJsonAsync(
            HttpMethod.Delete,
            route,
            body: null,
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteCreatePollAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreatePollInput>(invocation);
        var answers = NormalizePollAnswers(input.Answers);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["content"] = input.Content,
            ["nonce"] = NormalizeNonce(input.Nonce),
            ["enforce_nonce"] = true,
            ["allowed_mentions"] = AllowedMentionsPayload(input.AllowedMentions),
            ["flags"] = input.SuppressNotifications ? SuppressNotificationsFlag : 0,
            ["poll"] = new Dictionary<string, object?>
            {
                ["question"] = new Dictionary<string, object?> { ["text"] = NormalizePollText(input.Question, 300) },
                ["answers"] = answers.Select(answer => new Dictionary<string, object?>
                {
                    ["poll_media"] = PollMediaPayload(answer)
                }).ToArray(),
                ["duration"] = input.DurationHours,
                ["allow_multiselect"] = input.AllowMultiselect,
                ["layout_type"] = 1
            }
        };
        return transport.SendJsonAsync(
            HttpMethod.Post,
            $"channels/{NormalizeSnowflake(invocation.ResourceId)}/messages",
            JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private async Task<JsonElement?> SendMessagePayloadAsync(
        HttpMethod method,
        string route,
        Dictionary<string, object?> payload,
        ImmutableArray<AssetView> assets,
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (assets.IsEmpty)
        {
            return await transport.SendJsonAsync(
                method,
                route,
                JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false);
        }

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(
            JsonSerializer.Serialize(payload, CatalogJson.Options),
            Encoding.UTF8,
            "application/json"), "payload_json");
        for (var index = 0; index < assets.Length; index++)
        {
            var path = await assetVault.GetContentPathAsync(assets[index].AssetId, cancellationToken).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(assets[index].MediaType);
            multipart.Add(content, $"files[{index.ToString(CultureInfo.InvariantCulture)}]", assets[index].Filename);
        }

        return await transport.SendContentAsync(
            method,
            route,
            multipart,
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, object?> MessagePayload(
        SendMessageInput input,
        ImmutableArray<AssetView> assets)
    {
        var mode = NormalizeMode(input.Mode);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nonce"] = NormalizeNonce(input.Nonce),
            ["enforce_nonce"] = true,
            ["allowed_mentions"] = AllowedMentionsPayload(input.AllowedMentions),
            ["flags"] = RichMessagePayloadNormalizer.AddMessageFlags(
                flags: 0,
                input.SuppressEmbeds,
                input.SuppressNotifications,
                input.VoiceMessage is not null,
                input.Components.HasValue)
        };
        if (input.Content is not null) payload["content"] = input.Content;
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds) ?? [];
        if (!embeds.IsEmpty) payload["embeds"] = RichMessagePayloadNormalizer.EmbedsPayload(embeds);
        var stickerIds = RichMessagePayloadNormalizer.NormalizeStickerIds(input.StickerIds);
        if (!stickerIds.IsEmpty) payload["sticker_ids"] = stickerIds.ToArray();
        if (input.Tts) payload["tts"] = true;
        var theme = RichMessagePayloadNormalizer.NormalizeTheme(input.SharedClientTheme);
        if (theme is not null) payload["shared_client_theme"] = RichMessagePayloadNormalizer.ThemePayload(theme);
        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames);
        if (components.HasValue) payload["components"] = components.Value.Payload;
        if (!assets.IsEmpty)
        {
            var voice = input.VoiceMessage is null ? null : RichMessagePayloadNormalizer.NormalizeVoiceMessage(input.VoiceMessage);
            payload["attachments"] = assets.Select((asset, index) =>
            {
                var attachment = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = index,
                    ["filename"] = asset.Filename
                };
                if (voice?.AssetId == asset.AssetId)
                {
                    attachment["duration_secs"] = voice.DurationSeconds;
                    attachment["waveform"] = voice.WaveformBase64;
                }

                return (object)attachment;
            }).ToArray();
        }

        if (mode is "reply" or "forward")
        {
            payload["message_reference"] = new Dictionary<string, object?>
            {
                ["type"] = mode == "reply" ? 0 : 1,
                ["message_id"] = NormalizeSnowflake(input.SourceMessageId!),
                ["channel_id"] = NormalizeSnowflake(input.SourceChannelId!),
                ["fail_if_not_exists"] = true
            };
        }

        return payload;
    }

    private async Task<ChannelApiState> GetWritableMessageChannelAsync(
        string channelId,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        var state = ChannelApiState.Parse(await transport.GetJsonAsync(
            $"channels/{NormalizeSnowflake(channelId)}",
            cancellationToken).ConfigureAwait(false));
        if (state.GuildId != GuildId() || state.Type is not 0 and not 2 and not 5 and not 10 and not 11 and not 12)
        {
            throw InvalidArgument("Message destination must be a text-capable channel in the configured guild.");
        }

        EnsureDigest(expectedDigest, state.ComputeDigest());
        return state;
    }

    private async Task<MessageView> GetOwnedMessageAsync(
        string channelId,
        string messageId,
        string expectedDigest,
        CancellationToken cancellationToken)
    {
        var message = await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(expectedDigest, message.StateDigest);
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        return message.AuthorId == bot.BotUserId
            ? message
            : throw PolicyDenied("The message was not authored by this Steward bot.");
    }

    private async Task<MessageView?> TryGetMessageAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await messageReader.GetMessageAsync(channelId, messageId, cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
            return null;
        }
    }

    private async Task<ImmutableArray<AssetView>> GetAssetsAsync(
        ImmutableArray<string> assetIds,
        CancellationToken cancellationToken)
    {
        if (assetIds.IsDefault)
        {
            return [];
        }

        if (assetIds.Length > 10 || assetIds.Distinct(StringComparer.Ordinal).Count() != assetIds.Length)
        {
            throw InvalidArgument("Message assets must contain at most 10 unique asset IDs.");
        }

        var assets = ImmutableArray.CreateBuilder<AssetView>();
        foreach (var id in assetIds)
        {
            assets.Add(await assetVault.GetAsync(id, cancellationToken).ConfigureAwait(false));
        }

        ValidateTotalAttachmentBytes(assets.Sum(item => item.ByteLength));
        return assets.ToImmutable();
    }

    private async Task ValidateGuildStickersAsync(
        ImmutableArray<string> stickerIds,
        CancellationToken cancellationToken)
    {
        foreach (var stickerId in stickerIds)
        {
            var root = await transport.GetJsonAsync(
                $"guilds/{GuildId()}/stickers/{stickerId}",
                cancellationToken).ConfigureAwait(false);
            if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                NormalizeSnowflake(id.GetString() ?? string.Empty) != stickerId)
            {
                throw InvalidResponse("guild sticker");
            }

            if (root.TryGetProperty("guild_id", out var guildId) && guildId.ValueKind == JsonValueKind.String &&
                NormalizeSnowflake(guildId.GetString() ?? string.Empty) != GuildId())
            {
                throw new StewardException(StewardErrorCodes.GuildMismatch, "A sticker belongs to another guild.");
            }
        }
    }

    private static ImmutableHashSet<string> NormalizeEditClear(ImmutableArray<string>? fields)
    {
        var source = !fields.HasValue || fields.Value.IsDefault ? [] : fields.Value;
        var normalized = source.Select(field => field?.Trim().ToLowerInvariant() ?? string.Empty)
            .ToImmutableHashSet(StringComparer.Ordinal);
        if (normalized.Any(field => field != "content"))
        {
            throw InvalidArgument("Message edit clear accepts only content.");
        }

        return normalized;
    }

    private static long ApplyEditableFlags(long current, bool? suppressEmbeds, bool setsComponentsV2)
    {
        var flags = suppressEmbeds switch
        {
            true => current | SuppressEmbedsFlag,
            false => current & ~SuppressEmbedsFlag,
            null => current
        };
        return setsComponentsV2 ? flags | ComponentsV2Flag : flags;
    }

    private static void ValidateTotalAttachmentBytes(long total)
    {
        if (total > 25L * 1024 * 1024)
        {
            throw InvalidArgument("Combined message attachments exceed Discord's 25 MiB request limit.");
        }
    }

    private async Task<bool> HasReactionAsync(
        string channelId,
        string messageId,
        string emoji,
        string userId,
        CancellationToken cancellationToken)
    {
        string? after = null;
        for (var page = 0; page < 10; page++)
        {
            var result = await messageReader.ListMessageReactionsAsync(
                channelId,
                messageId,
                emoji,
                limit: 100,
                after,
                reactionType: "normal",
                cancellationToken).ConfigureAwait(false);
            if (result.Items.Any(item => item.UserId == NormalizeSnowflake(userId)))
            {
                return true;
            }

            if (result.NextAfterUserId is null)
            {
                return false;
            }

            after = result.NextAfterUserId;
        }

        throw new StewardException(StewardErrorCodes.ResultTooLarge, "Reaction users exceed the 1000-user verification limit.");
    }

    private async Task<bool> ReactionsClearedAsync(
        string channelId,
        string messageId,
        string? emoji,
        CancellationToken cancellationToken)
    {
        var root = await transport.GetJsonAsync(
            $"channels/{NormalizeSnowflake(channelId)}/messages/{NormalizeSnowflake(messageId)}",
            cancellationToken).ConfigureAwait(false);
        if (!root.TryGetProperty("reactions", out var reactions) || reactions.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (reactions.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse("reaction state");
        }

        if (emoji is null)
        {
            return reactions.GetArrayLength() == 0;
        }

        var key = NormalizeEmojiKey(emoji);
        return !reactions.EnumerateArray().Any(reaction => ReactionKey(reaction) == key);
    }

    private static ImmutableArray<PollAnswerInput> NormalizePollAnswers(ImmutableArray<PollAnswerInput> answers)
    {
        if (answers.Length is < 2 or > 10)
        {
            throw InvalidArgument("Polls require 2 to 10 answers.");
        }

        return answers.Select(answer =>
        {
            if (answer.EmojiId is not null && answer.EmojiName is not null)
            {
                throw InvalidArgument("Poll answer emoji ID and name are mutually exclusive.");
            }

            return answer with
            {
                Text = NormalizePollText(answer.Text, 55),
                EmojiId = answer.EmojiId is null ? null : NormalizeSnowflake(answer.EmojiId),
                EmojiName = answer.EmojiName is null ? null : NormalizePollText(answer.EmojiName, 100)
            };
        }).ToImmutableArray();
    }

    private static Dictionary<string, object?> PollMediaPayload(PollAnswerInput answer)
    {
        var media = new Dictionary<string, object?> { ["text"] = answer.Text };
        if (answer.EmojiId is not null || answer.EmojiName is not null)
        {
            media["emoji"] = new Dictionary<string, object?>
            {
                ["id"] = answer.EmojiId,
                ["name"] = answer.EmojiName
            };
        }

        return media;
    }

    private static Dictionary<string, object?> AllowedMentionsPayload(AllowedMentionsInput input)
    {
        ValidateAllowedMentions(input);
        return new Dictionary<string, object?>
        {
            ["parse"] = input.Everyone ? new[] { "everyone" } : Array.Empty<string>(),
            ["users"] = input.UserIds.Select(NormalizeSnowflake).ToArray(),
            ["roles"] = input.RoleIds.Select(NormalizeSnowflake).ToArray(),
            ["replied_user"] = input.RepliedUser
        };
    }

    private static void ValidateAllowedMentions(AllowedMentionsInput input)
    {
        if (input is null || input.UserIds.IsDefault || input.RoleIds.IsDefault ||
            input.UserIds.Length > 100 || input.RoleIds.Length > 100)
        {
            throw InvalidArgument("Allowed mentions are required and limited to 100 exact users and roles.");
        }

        _ = input.UserIds.Select(NormalizeSnowflake).ToArray();
        _ = input.RoleIds.Select(NormalizeSnowflake).ToArray();
    }

    private static string MessageRoute(CatalogMutationInvocation invocation, string suffix = "")
    {
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        return $"channels/{channelId}/messages/{messageId}{suffix}";
    }

    private static string PollRoute(CatalogMutationInvocation invocation, string suffix)
    {
        var (channelId, messageId) = ParseMessageResource(invocation.ResourceId);
        return $"channels/{channelId}/polls/{messageId}{suffix}";
    }

    private static (string ChannelId, string MessageId) ParseMessageResource(string value)
    {
        var parts = value.Split(':', StringSplitOptions.None);
        return parts.Length == 2
            ? (NormalizeSnowflake(parts[0]), NormalizeSnowflake(parts[1]))
            : throw InvalidArgument("Message resource must contain exact channel and message IDs.");
    }

    internal static string MessageResourceId(string channelId, string messageId) =>
        $"{NormalizeSnowflake(channelId)}:{NormalizeSnowflake(messageId)}";

    private static string NormalizeMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "new" => "new",
        "reply" => "reply",
        "forward" => "forward",
        _ => throw InvalidArgument("Message mode must be new, reply, or forward.")
    };

    private static string NormalizeContent(string value, bool allowEmpty)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > 2_000 ||
            value.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
        {
            throw InvalidArgument("Message content must contain at most 2000 valid characters.");
        }

        return value;
    }

    private static string NormalizePollText(string value, int maximum)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is >= 1 && normalized.Length <= maximum && !normalized.Any(char.IsControl)
            ? normalized
            : throw InvalidArgument("Poll text is empty, too long, or contains control characters.");
    }

    private static string NormalizeNonce(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is >= 1 and <= 25 && !normalized.Any(char.IsControl)
            ? normalized
            : throw InvalidArgument("Message nonce must contain 1 to 25 printable characters.");
    }

    private static string NormalizeEmoji(string value) => Uri.EscapeDataString(NormalizeEmojiKey(value));

    private static string NormalizeEmojiKey(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is >= 1 and <= 100 && !normalized.Any(char.IsControl)
            ? normalized
            : throw InvalidArgument("Emoji must be a bounded Unicode emoji or custom name:id value.");
    }

    private static string ReactionKey(JsonElement reaction)
    {
        if (!reaction.TryGetProperty("emoji", out var emoji) || emoji.ValueKind != JsonValueKind.Object)
        {
            throw InvalidResponse("reaction emoji");
        }

        var name = emoji.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String
            ? nameValue.GetString() ?? string.Empty
            : string.Empty;
        var id = emoji.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
            ? idValue.GetString()
            : null;
        return id is null ? name : $"{name}:{id}";
    }

    private static string CreatedMessageId(string? receiptJson)
    {
        if (receiptJson is null)
        {
            throw new StewardException(StewardErrorCodes.DiscordUnavailable, "Created message ID is unavailable.");
        }

        using var document = JsonDocument.Parse(receiptJson);
        return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(id.GetString() ?? string.Empty)
            : throw InvalidResponse("created message receipt");
    }

    private static CatalogMutationObservation Observation<T>(T observed, string beforeStateJson, string desiredStateJson)
    {
        var observedJson = State(observed);
        return new CatalogMutationObservation(
            JsonEqual(observedJson, desiredStateJson)
                ? CatalogMutationObservationKind.DesiredState
                : JsonEqual(observedJson, beforeStateJson)
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
            observedJson);
    }

    private static bool JsonEqual(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
    }

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options)
        ?? throw InvalidArgument("Mutation arguments are invalid.");

    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options)
        ?? throw new StewardException(StewardErrorCodes.AuditUnavailable, "Stored mutation state is invalid.");

    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static string NormalizeSnowflake(string value) => ChannelApiState.NormalizeSnowflake(value);

    private static void EnsureDigest(string? supplied, string observed)
    {
        var normalized = supplied?.Trim().ToLowerInvariant();
        if (normalized is not { Length: 64 } || !normalized.All(char.IsAsciiHexDigit))
        {
            throw InvalidArgument("Expected state digest must be a lowercase SHA-256 digest.");
        }

        if (normalized != observed)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Message, reaction, poll, or channel state changed after approval.");
        }
    }

    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);

    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException PolicyDenied(string message) => new(StewardErrorCodes.PolicyDenied, message);

    private static StewardException InvalidResponse(string resource) =>
        new(StewardErrorCodes.DiscordUnavailable, $"Discord returned invalid {resource} data.");

    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "Message mutation is not supported.");

    private sealed record ExpectedAttachment(
        string Filename,
        long SizeBytes,
        bool IsSpoiler = false,
        double? DurationSeconds = null,
        string? Waveform = null)
    {
        internal static ExpectedAttachment FromView(MessageAttachmentView attachment) => new(
            attachment.Filename,
            attachment.SizeBytes,
            attachment.IsSpoiler,
            attachment.DurationSeconds,
            attachment.Waveform);
    }

    private sealed record CreatedMessageExpectation(
        string ChannelId,
        string AuthorId,
        string Mode,
        string Content,
        ImmutableArray<ExpectedAttachment> Attachments,
        bool SuppressNotifications,
        bool SuppressEmbeds,
        bool VoiceMessage,
        bool ComponentsV2,
        RichPayloadState RichPayload)
    {
        internal bool Matches(MessageView message) =>
            message.ChannelId == ChannelId &&
            message.AuthorId == AuthorId &&
            (Mode == "forward" || message.Content == Content) &&
            Attachments.OrderBy(item => item.Filename, StringComparer.Ordinal).ThenBy(item => item.SizeBytes)
                .SequenceEqual(message.Attachments
                    .Select(ExpectedAttachment.FromView)
                    .OrderBy(item => item.Filename, StringComparer.Ordinal)
                    .ThenBy(item => item.SizeBytes)) &&
            (!SuppressNotifications || (message.Flags & SuppressNotificationsFlag) != 0) &&
            (!SuppressEmbeds || (message.Flags & SuppressEmbedsFlag) != 0) &&
            (!VoiceMessage || (message.Flags & VoiceMessageFlag) != 0) &&
            (!ComponentsV2 || (message.Flags & ComponentsV2Flag) != 0) &&
            RichPayloadMatches(RichPayload, message);
    }

    private sealed record EditedMessageExpectation(
        string ChannelId,
        string MessageId,
        string Content,
        ImmutableArray<ExpectedAttachment> Attachments,
        long Flags,
        RichPayloadState RichPayload)
    {
        internal bool Matches(MessageView message) =>
            message.ChannelId == ChannelId && message.Id == MessageId && message.Content == Content && message.Flags == Flags &&
            Attachments.SequenceEqual(message.Attachments
                .Select(ExpectedAttachment.FromView)
                .OrderBy(item => item.Filename, StringComparer.Ordinal)
                .ThenBy(item => item.SizeBytes)) &&
            RichPayloadMatches(RichPayload, message);
    }

    private sealed record CreatedPollExpectation(
        string ChannelId,
        string AuthorId,
        string Content,
        string Question,
        ImmutableArray<string> Answers,
        bool AllowMultiselect,
        bool SuppressNotifications)
    {
        internal bool Matches(MessageView message) =>
            message.ChannelId == ChannelId && message.AuthorId == AuthorId && message.Content == Content &&
            message.Poll is not null && message.Poll.Question == Question &&
            message.Poll.Answers.OrderBy(answer => answer.AnswerId).Select(answer => answer.Text)
                .SequenceEqual(Answers, StringComparer.Ordinal) &&
            message.Poll.AllowsMultiselect == AllowMultiselect &&
            (!SuppressNotifications || (message.Flags & SuppressNotificationsFlag) != 0);
    }

    private sealed record MessageMutationState(
        string ChannelId,
        string MessageId,
        string AuthorId,
        string Content,
        string StateDigest,
        bool IsPinned,
        long Flags,
        ImmutableArray<ExpectedAttachment> Attachments,
        string ReactionStateDigest,
        bool? PollFinalized,
        string? PollStateDigest,
        RichPayloadState RichPayload)
    {
        internal static MessageMutationState FromMessage(MessageView message) => new(
            message.ChannelId,
            message.Id,
            message.AuthorId,
            message.Content,
            message.StateDigest,
            message.IsPinned,
            message.Flags,
            message.Attachments.Select(ExpectedAttachment.FromView)
                .OrderBy(item => item.Filename, StringComparer.Ordinal)
                .ThenBy(item => item.SizeBytes)
                .ToImmutableArray(),
            message.ReactionStateDigest,
            message.Poll?.IsFinalized,
            message.Poll?.StateDigest,
            RichPayloadFromMessage(message));

        internal bool EquivalentTo(MessageMutationState other) =>
            ChannelId == other.ChannelId && MessageId == other.MessageId && AuthorId == other.AuthorId &&
            Content == other.Content && IsPinned == other.IsPinned && Flags == other.Flags &&
            Attachments.SequenceEqual(other.Attachments) && ReactionStateDigest == other.ReactionStateDigest &&
            PollFinalized == other.PollFinalized && PollStateDigest == other.PollStateDigest &&
            RichPayloadEquivalent(RichPayload, other.RichPayload);
    }

    private static RichPayloadState RichPayloadFromMessage(MessageView message) => new(
        message.IsTts,
        message.StickerIds.IsDefault ? [] : message.StickerIds,
        message.EmbedsJson,
        message.ComponentsJson,
        message.SharedClientTheme);

    private static bool RichPayloadMatches(RichPayloadState expected, MessageView message) =>
        RichPayloadEquivalent(expected, RichPayloadFromMessage(message));

    private static bool RichPayloadEquivalent(RichPayloadState left, RichPayloadState right) =>
        left.IsTts == right.IsTts &&
        left.StickerIds.SequenceEqual(right.StickerIds, StringComparer.Ordinal) &&
        JsonEqual(left.EmbedsJson, right.EmbedsJson) &&
        JsonEqual(left.ComponentsJson, right.ComponentsJson) &&
        JsonEqual(State(left.SharedClientTheme), State(right.SharedClientTheme));

    private sealed record MessageExistenceState(string ChannelId, string MessageId, bool Exists);

    private sealed record BulkMessageExistenceState(ImmutableArray<MessageExistenceState> Messages);

    private sealed record ReactionPresenceState(
        string ChannelId,
        string MessageId,
        string Emoji,
        string UserId,
        bool Present);

    private sealed record ReactionPresenceObservationState(
        string ChannelId,
        string MessageId,
        string Emoji,
        string UserId,
        bool Present,
        string StateDigest,
        string ReactionStateDigest)
    {
        internal bool Matches(ReactionPresenceState state) =>
            ChannelId == state.ChannelId && MessageId == state.MessageId && Emoji == state.Emoji &&
            UserId == state.UserId && Present == state.Present;
    }

    private sealed record ReactionClearState(
        string ChannelId,
        string MessageId,
        string? Emoji,
        bool Cleared,
        string ReactionStateDigest);

    private sealed record ReactionClearObservationState(
        string ChannelId,
        string MessageId,
        string? Emoji,
        bool Cleared,
        string StateDigest,
        string ReactionStateDigest)
    {
        internal bool Matches(ReactionClearState state) =>
            ChannelId == state.ChannelId && MessageId == state.MessageId && Emoji == state.Emoji &&
            Cleared == state.Cleared && ReactionStateDigest == state.ReactionStateDigest;
    }
}
