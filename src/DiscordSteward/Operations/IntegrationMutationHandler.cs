using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Secrets;
using DiscordSteward.Tools;

namespace DiscordSteward.Operations;

internal sealed class IntegrationMutationHandler(
    DiscordApiTransport transport,
    IDiscordExtendedReader extendedReader,
    IGuildCatalogReader catalogReader,
    IDiscordMessageReader messageReader,
    IDiscordGuildReader guildReader,
    IAssetVault assetVault,
    IWebhookSecretVault webhookVault,
    StewardRuntimeSettings settings) : ICatalogMutationFamilyHandler
{
    private const int SuppressEmbedsFlag = 1 << 2;
    private const int SuppressNotificationsFlag = 1 << 12;
    private const int ComponentsV2Flag = 1 << 15;

    private static readonly ImmutableHashSet<string> ToolNames = new[]
    {
        "create_invite",
        "create_targeted_invite",
        "update_invite_target_users",
        "delete_invite",
        "create_webhook",
        "update_webhook",
        "delete_webhook",
        "send_webhook_message",
        "edit_webhook_message",
        "delete_webhook_message",
        "create_guild_template",
        "sync_guild_template",
        "update_guild_template",
        "delete_guild_template"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken) => invocation.ToolName switch
        {
            "create_invite" => await PrepareCreateInviteAsync(invocation, targeted: false, cancellationToken).ConfigureAwait(false),
            "create_targeted_invite" => await PrepareCreateInviteAsync(invocation, targeted: true, cancellationToken).ConfigureAwait(false),
            "update_invite_target_users" => await PrepareUpdateTargetsAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_invite" => await PrepareDeleteInviteAsync(invocation, cancellationToken).ConfigureAwait(false),
            "create_webhook" => await PrepareCreateWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_webhook" => await PrepareUpdateWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_webhook" => await PrepareDeleteWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "send_webhook_message" => await PrepareSendWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "edit_webhook_message" => await PrepareEditWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_webhook_message" => await PrepareDeleteWebhookMessageAsync(invocation, cancellationToken).ConfigureAwait(false),
            "create_guild_template" => await PrepareCreateTemplateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "sync_guild_template" => await PrepareSyncTemplateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_template" => await PrepareUpdateTemplateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_guild_template" => await PrepareDeleteTemplateAsync(invocation, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response = invocation.ToolName switch
        {
            "create_invite" => await WriteCreateInviteAsync(invocation, targeted: false, cancellationToken).ConfigureAwait(false),
            "create_targeted_invite" => await WriteCreateInviteAsync(invocation, targeted: true, cancellationToken).ConfigureAwait(false),
            "update_invite_target_users" => await WriteUpdateTargetsAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_invite" => await transport.SendJsonAsync(
                HttpMethod.Delete,
                $"invites/{EscapeCode(invocation.ResourceId)}",
                body: null,
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false),
            "create_webhook" => await WriteCreateWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_webhook" => await WriteUpdateWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_webhook" => await WriteDeleteWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "send_webhook_message" => await WriteSendWebhookAsync(invocation, cancellationToken).ConfigureAwait(false),
            "edit_webhook_message" => await WriteEditWebhookAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
            "delete_webhook_message" => await WriteDeleteWebhookMessageAsync(invocation, cancellationToken).ConfigureAwait(false),
            "create_guild_template" => await WriteCreateTemplateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "sync_guild_template" => await WriteSyncTemplateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_template" => await WriteUpdateTemplateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_guild_template" => await transport.SendJsonAsync(
                HttpMethod.Delete,
                TemplateRoute(invocation.ResourceId),
                body: null,
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
        return invocation.ToolName switch
        {
            "create_invite" => await ObserveCreatedInviteAsync(invocation, desiredStateJson, priorReceiptJson, targeted: false, cancellationToken).ConfigureAwait(false),
            "create_targeted_invite" => await ObserveCreatedInviteAsync(invocation, desiredStateJson, priorReceiptJson, targeted: true, cancellationToken).ConfigureAwait(false),
            "update_invite_target_users" => await ObserveTargetJobAsync(invocation, beforeStateJson, desiredStateJson, cancellationToken).ConfigureAwait(false),
            "delete_invite" => await ObserveDeletedInviteAsync(invocation, beforeStateJson, cancellationToken).ConfigureAwait(false),
            "create_webhook" => await ObserveCreatedWebhookAsync(desiredStateJson, priorReceiptJson, cancellationToken).ConfigureAwait(false),
            "update_webhook" => await ObserveWebhookAsync(invocation, beforeStateJson, desiredStateJson, cancellationToken).ConfigureAwait(false),
            "delete_webhook" => await ObserveDeletedWebhookAsync(invocation, beforeStateJson, cancellationToken).ConfigureAwait(false),
            "send_webhook_message" => await ObserveSentWebhookMessageAsync(invocation, desiredStateJson, priorReceiptJson, cancellationToken).ConfigureAwait(false),
            "edit_webhook_message" => await ObserveEditedWebhookMessageAsync(invocation, beforeStateJson, desiredStateJson, cancellationToken).ConfigureAwait(false),
            "delete_webhook_message" => await ObserveDeletedWebhookMessageAsync(invocation, beforeStateJson, cancellationToken).ConfigureAwait(false),
            "create_guild_template" => await ObserveCreatedTemplateAsync(desiredStateJson, priorReceiptJson, cancellationToken).ConfigureAwait(false),
            "sync_guild_template" => await ObserveSyncedTemplateAsync(invocation, beforeStateJson, desiredStateJson, cancellationToken).ConfigureAwait(false),
            "update_guild_template" => await ObserveTemplateAsync(invocation, beforeStateJson, desiredStateJson, cancellationToken).ConfigureAwait(false),
            "delete_guild_template" => await ObserveDeletedTemplateAsync(invocation, beforeStateJson, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };
    }

    private async Task<CatalogMutationPreparation> PrepareCreateInviteAsync(
        CatalogMutationInvocation invocation,
        bool targeted,
        CancellationToken cancellationToken)
    {
        var input = targeted
            ? Arguments<CreateTargetedInviteInput>(invocation).Invite
            : Arguments<CreateInviteInput>(invocation);
        var normalized = await NormalizeInviteAsync(input, cancellationToken).ConfigureAwait(false);
        ImmutableArray<string> users = [];
        if (targeted)
        {
            users = NormalizeTargetUsers(Arguments<CreateTargetedInviteInput>(invocation).UserIds);
        }

        return new CatalogMutationPreparation(
            State(new ChannelInvitePrecondition(normalized.ChannelId, normalized.ExpectedChannelStateDigest)),
            State(new InviteCreationExpectation(normalized, targeted, users.Length, TargetDigest(users))));
    }

    private async Task<CatalogMutationPreparation> PrepareUpdateTargetsAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateInviteTargetUsersInput>(invocation);
        var code = NormalizeCode(invocation.ResourceId);
        var current = await catalogReader.ListInviteTargetUsersAsync(code, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedTargetUsersDigest, current.StateDigest);
        var users = NormalizeTargetUsers(input.UserIds);
        return new CatalogMutationPreparation(
            State(current),
            State(new InviteTargetUpdateExpectation(code, users.Length, TargetDigest(users))));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteInviteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteInviteInput>(invocation);
        var current = await extendedReader.GetInviteAsync(NormalizeCode(invocation.ResourceId), cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        return new CatalogMutationPreparation(State(current), State(new ResourceExistenceState(current.Code, Exists: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareCreateWebhookAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateWebhookInput>(invocation);
        var channel = await GetGuildChannelAsync(input.ChannelId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedChannelStateDigest, channel.ComputeDigest());
        var name = NormalizeWebhookName(input.Name);
        if (input.AvatarAssetId is not null)
        {
            await ValidateImageAssetAsync(input.AvatarAssetId, cancellationToken).ConfigureAwait(false);
        }
        return new CatalogMutationPreparation(
            State(channel),
            State(new WebhookCreationExpectation(channel.Id, name, input.AvatarAssetId is null ? null : "__present__")));
    }

    private async Task<CatalogMutationPreparation> PrepareUpdateWebhookAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateWebhookInput>(invocation);
        var current = await extendedReader.GetWebhookAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        var clear = NormalizeClear(input.Clear, ["avatar"]);
        if (input.AvatarAssetId is not null && clear.Contains("avatar")) throw InvalidArgument("Webhook avatar cannot be set and cleared.");
        if (input.AvatarAssetId is not null)
        {
            await ValidateImageAssetAsync(input.AvatarAssetId, cancellationToken).ConfigureAwait(false);
        }
        var channelId = input.ChannelId is null ? current.ChannelId : (await GetGuildChannelAsync(input.ChannelId, cancellationToken).ConfigureAwait(false)).Id;
        if (input.StoredWebhookId is not null)
        {
            var secret = await webhookVault.GetAsync(input.StoredWebhookId, cancellationToken).ConfigureAwait(false);
            if (secret.WebhookId != current.Id) throw new StewardException(StewardErrorCodes.StateConflict, "Stored webhook ID belongs to another webhook.");
        }

        var desired = new WebhookUpdateExpectation(
            current.Id,
            channelId,
            input.Name is null ? current.Name : NormalizeWebhookName(input.Name),
            clear.Contains("avatar") ? null : input.AvatarAssetId is null ? current.AvatarHash : "__present__");
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteWebhookAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteWebhookInput>(invocation);
        var current = await extendedReader.GetWebhookAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        if (input.StoredWebhookId is not null)
        {
            var secret = await webhookVault.GetAsync(input.StoredWebhookId, cancellationToken).ConfigureAwait(false);
            if (secret.WebhookId != current.Id) throw new StewardException(StewardErrorCodes.StateConflict, "Stored webhook ID belongs to another webhook.");
        }

        return new CatalogMutationPreparation(State(current), State(new ResourceExistenceState(current.Id, Exists: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareSendWebhookAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SendWebhookMessageInput>(invocation);
        var secret = await GetVerifiedSecretAsync(input.StoredWebhookId, input.ExpectedWebhookStateDigest, cancellationToken).ConfigureAwait(false);
        ValidateAllowedMentions(input.AllowedMentions);
        var assets = await GetAssetsAsync(input.AssetIds, cancellationToken).ConfigureAwait(false);
        var content = input.Content is null ? null : NormalizeContent(input.Content);
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds) ?? [];
        RichMessagePayloadNormalizer.ValidateEmbedAttachmentReferences(embeds, assets.Select(asset => asset.Filename));
        if (input.SuppressEmbeds && !embeds.IsEmpty)
            throw InvalidArgument("Explicit rich embeds cannot be combined with suppress_embeds.");
        var poll = input.Poll is null ? null : NormalizeWebhookPoll(input.Poll);
        var username = input.UsernameOverride is null ? null : NormalizeText(input.UsernameOverride, 80, false);
        var avatarUrl = input.AvatarUrlOverride is null ? null : RichMessagePayloadNormalizer.NormalizeWebUrl(input.AvatarUrlOverride);
        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames);
        if (components.HasValue && (content is not null || !embeds.IsEmpty || !assets.IsEmpty || poll is not null || input.Tts))
        {
            throw InvalidArgument("Components v2 webhook messages can contain only static components.");
        }

        if (input.Tts && string.IsNullOrEmpty(content))
        {
            throw InvalidArgument("Webhook TTS requires explicit text content.");
        }

        if (content is null && embeds.IsEmpty && assets.IsEmpty && poll is null && !components.HasValue)
        {
            throw InvalidArgument("Webhook execution requires content, an embed, a poll, a component, or an approved asset.");
        }

        if (input.ThreadId is not null && input.ThreadName is not null)
        {
            throw InvalidArgument("Webhook execution cannot target and create a thread simultaneously.");
        }

        string? destination;
        ImmutableArray<string> appliedTags = [];
        string? threadName = null;
        if (input.ThreadId is not null)
        {
            var thread = await GetGuildChannelAsync(input.ThreadId, cancellationToken).ConfigureAwait(false);
            if (thread.Type is not 10 and not 11 and not 12 || thread.ParentId != secret.ChannelId)
            {
                throw InvalidArgument("Webhook thread target must belong to the stored webhook channel.");
            }

            destination = thread.Id;
        }
        else
        {
            var channel = await GetGuildChannelAsync(secret.ChannelId, cancellationToken).ConfigureAwait(false);
            if (input.ThreadName is not null)
            {
                if (channel.Type is not 15 and not 16 || input.ExpectedAvailableTagsDigest is null)
                {
                    throw InvalidArgument("Webhook thread creation requires a forum/media channel and available-tags digest.");
                }

                EnsureDigest(input.ExpectedAvailableTagsDigest, channel.ComputeAvailableTagsDigest());
                appliedTags = NormalizeWebhookTags(input.AppliedTagIds, channel);
                threadName = NormalizeText(input.ThreadName, 100, false);
                destination = null;
            }
            else
            {
                if (channel.Type is 15 or 16)
                {
                    throw InvalidArgument("Forum/media webhook execution requires thread_id or thread_name.");
                }

                if (input.ExpectedAvailableTagsDigest is not null || input.AppliedTagIds.HasValue)
                {
                    throw InvalidArgument("Webhook tag evidence is valid only when creating a forum/media thread.");
                }

                destination = secret.ChannelId;
            }
        }

        return new CatalogMutationPreparation(
            State(new StoredWebhookPrecondition(secret.VaultId, secret.WebhookId, secret.ChannelId)),
            State(new WebhookMessageExpectation(
                destination,
                secret.WebhookId,
                content ?? string.Empty,
                assets.Select(item => new ExpectedAttachment(item.Filename, item.ByteLength)).ToImmutableArray(),
                RichMessagePayloadNormalizer.AddMessageFlags(0, input.SuppressEmbeds, input.SuppressNotifications, false, components.HasValue),
                new RichPayloadState(
                    input.Tts,
                    StickerIds: [],
                    RichMessagePayloadNormalizer.CanonicalEmbedsJson(embeds),
                    components?.CanonicalJson ?? "[]",
                    SharedClientTheme: null),
                username,
                avatarUrl is null ? null : "__present__",
                poll,
                secret.ChannelId,
                threadName,
                appliedTags)));
    }

    private async Task<CatalogMutationPreparation> PrepareEditWebhookAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<EditWebhookMessageInput>(invocation);
        var secret = await GetVerifiedSecretAsync(input.StoredWebhookId, input.ExpectedWebhookStateDigest, cancellationToken).ConfigureAwait(false);
        var destination = input.ThreadId is null ? secret.ChannelId : NormalizeSnowflake(input.ThreadId);
        var message = await messageReader.GetMessageAsync(destination, input.MessageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMessageStateDigest, message.StateDigest);
        if (message.AuthorId != secret.WebhookId) throw PolicyDenied("The message was not authored by the stored webhook.");
        ValidateAllowedMentions(input.AllowedMentions);
        var retained = NormalizeIds(input.RetainedAttachmentIds, 10);
        if (retained.Any(id => !message.Attachments.Any(item => item.Id == id))) throw InvalidArgument("Retained attachment does not belong to the message.");
        var assets = await GetAssetsAsync(input.NewAssetIds, cancellationToken).ConfigureAwait(false);
        var clear = NormalizeClear(input.Clear ?? [], ["content"]);
        if (input.Content is not null && clear.Contains("content")) throw InvalidArgument("Webhook content cannot be set and cleared.");
        var content = clear.Contains("content") ? string.Empty : input.Content is null ? message.Content : NormalizeContent(input.Content);
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds);
        var allFilenames = message.Attachments.Where(item => retained.Contains(item.Id, StringComparer.Ordinal)).Select(item => item.Filename)
            .Concat(assets.Select(asset => asset.Filename)).ToArray();
        if (embeds.HasValue)
        {
            RichMessagePayloadNormalizer.ValidateEmbedAttachmentReferences(embeds.Value, allFilenames);
            if (input.SuppressEmbeds == true && !embeds.Value.IsEmpty)
                throw InvalidArgument("Explicit rich embeds cannot be combined with suppress_embeds.");
        }
        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames, allowEmpty: true);
        var beforeComponentsV2 = (message.Flags & ComponentsV2Flag) != 0;
        var setsComponentsV2 = components.HasValue && components.Value.Count > 0;
        if (beforeComponentsV2 && components.HasValue && components.Value.Count == 0)
            throw InvalidArgument("Components v2 cannot be removed after it is set on a webhook message.");
        var desiredEmbedsJson = embeds.HasValue ? RichMessagePayloadNormalizer.CanonicalEmbedsJson(embeds.Value) : message.EmbedsJson;
        var desiredComponentsJson = components?.CanonicalJson ?? message.ComponentsJson;
        if ((beforeComponentsV2 || setsComponentsV2) && (!string.IsNullOrEmpty(content) || desiredEmbedsJson != "[]"))
            throw InvalidArgument("Components v2 webhook edits require empty content and embeds.");
        var desiredAttachments = message.Attachments.Where(item => retained.Contains(item.Id, StringComparer.Ordinal))
            .Select(item => new ExpectedAttachment(item.Filename, item.SizeBytes))
            .Concat(assets.Select(item => new ExpectedAttachment(item.Filename, item.ByteLength)))
            .OrderBy(item => item.Filename, StringComparer.Ordinal).ThenBy(item => item.SizeBytes).ToImmutableArray();
        return new CatalogMutationPreparation(
            State(MessageState.FromMessage(message)),
            State(new WebhookMessageExpectation(
                destination,
                secret.WebhookId,
                content,
                desiredAttachments,
                ApplyWebhookEditFlags(message.Flags, input.SuppressEmbeds, setsComponentsV2),
                new RichPayloadState(
                    message.IsTts,
                    message.StickerIds.IsDefault ? [] : message.StickerIds,
                    desiredEmbedsJson,
                    desiredComponentsJson,
                    message.SharedClientTheme),
                ExpectedAuthorName: null,
                ExpectedAuthorAvatarHash: null,
                Poll: message.Poll is null ? null : WebhookPollExpectation.FromView(message.Poll),
                ParentChannelId: secret.ChannelId,
                ThreadName: null,
                AppliedTagIds: [])));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteWebhookMessageAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteWebhookMessageInput>(invocation);
        var secret = await GetVerifiedSecretAsync(input.StoredWebhookId, input.ExpectedWebhookStateDigest, cancellationToken).ConfigureAwait(false);
        var destination = input.ThreadId is null ? secret.ChannelId : NormalizeSnowflake(input.ThreadId);
        var message = await messageReader.GetMessageAsync(destination, input.MessageId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMessageStateDigest, message.StateDigest);
        if (message.AuthorId != secret.WebhookId) throw PolicyDenied("The message was not authored by the stored webhook.");
        return new CatalogMutationPreparation(
            State(new MessageExistenceState(destination, message.Id, Exists: true)),
            State(new MessageExistenceState(destination, message.Id, Exists: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareCreateTemplateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateGuildTemplateInput>(invocation);
        await EnsureConfigurationDigestAsync(input.ExpectedConfigurationStateDigest, cancellationToken).ConfigureAwait(false);
        return new CatalogMutationPreparation(
            State(new ConfigurationPrecondition(input.ExpectedConfigurationStateDigest)),
            State(new TemplateCreationExpectation(
                NormalizeText(input.Name, 100, false),
                input.Description is null ? null : NormalizeText(input.Description, 120, true),
                GuildId())));
    }

    private async Task<CatalogMutationPreparation> PrepareSyncTemplateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SyncGuildTemplateInput>(invocation);
        await EnsureConfigurationDigestAsync(input.ExpectedConfigurationStateDigest, cancellationToken).ConfigureAwait(false);
        var current = await catalogReader.GetGuildTemplateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedTemplateStateDigest, current.StateDigest);
        if (current.IsDirty == false)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The guild template already matches current configuration.");
        }
        return new CatalogMutationPreparation(State(current), State(new TemplateSyncExpectation(current.Code, current.UpdatedAt)));
    }

    private async Task<CatalogMutationPreparation> PrepareUpdateTemplateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildTemplateInput>(invocation);
        var current = await catalogReader.GetGuildTemplateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedTemplateStateDigest, current.StateDigest);
        var clear = NormalizeClear(input.Clear, ["description"]);
        if (input.Description is not null && clear.Contains("description")) throw InvalidArgument("Template description cannot be set and cleared.");
        var desiredBase = current with
        {
            Name = input.Name is null ? current.Name : NormalizeText(input.Name, 100, false),
            Description = clear.Contains("description") ? null : input.Description is null ? current.Description : NormalizeText(input.Description, 120, true),
            StateDigest = string.Empty
        };
        var desired = desiredBase with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(desiredBase) };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteTemplateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteGuildTemplateInput>(invocation);
        var current = await catalogReader.GetGuildTemplateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedTemplateStateDigest, current.StateDigest);
        return new CatalogMutationPreparation(State(current), State(new ResourceExistenceState(current.Code, Exists: false)));
    }

    private async Task<JsonElement?> WriteCreateInviteAsync(
        CatalogMutationInvocation invocation,
        bool targeted,
        CancellationToken cancellationToken)
    {
        var input = targeted ? Arguments<CreateTargetedInviteInput>(invocation).Invite : Arguments<CreateInviteInput>(invocation);
        var normalized = await NormalizeInviteAsync(input, cancellationToken).ConfigureAwait(false);
        var payload = InvitePayload(normalized);
        if (!targeted)
        {
            return await transport.SendJsonAsync(
                HttpMethod.Post,
                $"channels/{normalized.ChannelId}/invites",
                JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false);
        }

        var users = NormalizeTargetUsers(Arguments<CreateTargetedInviteInput>(invocation).UserIds);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(payload, CatalogJson.Options), Encoding.UTF8, "application/json"), "payload_json");
        multipart.Add(new ByteArrayContent(TargetCsv(users)), "target_users_file", "target-users.csv");
        return await transport.SendContentAsync(
            HttpMethod.Post,
            $"channels/{normalized.ChannelId}/invites",
            multipart,
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> WriteUpdateTargetsAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var users = NormalizeTargetUsers(Arguments<UpdateInviteTargetUsersInput>(invocation).UserIds);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(TargetCsv(users)), "target_users_file", "target-users.csv");
        return await transport.SendContentAsync(
            HttpMethod.Put,
            $"invites/{EscapeCode(invocation.ResourceId)}/target-users",
            multipart,
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> WriteCreateWebhookAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<CreateWebhookInput>(invocation);
        var body = new Dictionary<string, object?> { ["name"] = NormalizeWebhookName(input.Name) };
        await AddAvatarAsync(body, input.AvatarAssetId, cancellationToken).ConfigureAwait(false);
        var response = await transport.SendJsonAsync(
            HttpMethod.Post,
            $"channels/{NormalizeSnowflake(input.ChannelId)}/webhooks",
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false) ?? throw InvalidResponse("webhook creation");
        var id = RequiredSnowflake(response, "id");
        var channelId = RequiredSnowflake(response, "channel_id");
        var token = RequiredString(response, "token", 256);
        var secret = await webhookVault.StoreAsync(id, channelId, token, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["channel_id"] = channelId,
            ["vault_id"] = secret.VaultId
        }, CatalogJson.Options);
    }

    private async Task<JsonElement?> WriteUpdateWebhookAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateWebhookInput>(invocation);
        var body = new Dictionary<string, object?>();
        if (input.Name is not null) body["name"] = NormalizeWebhookName(input.Name);
        if (input.ChannelId is not null) body["channel_id"] = NormalizeSnowflake(input.ChannelId);
        await AddAvatarAsync(body, input.AvatarAssetId, cancellationToken).ConfigureAwait(false);
        if (input.Clear.Contains("avatar", StringComparer.Ordinal)) body["avatar"] = null;
        var response = await transport.SendJsonAsync(
            HttpMethod.Patch,
            $"webhooks/{NormalizeSnowflake(invocation.ResourceId)}",
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
        if (input.StoredWebhookId is not null && input.ChannelId is not null)
        {
            await webhookVault.UpdateChannelAsync(input.StoredWebhookId, input.ChannelId, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<JsonElement?> WriteDeleteWebhookAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteWebhookInput>(invocation);
        var response = await transport.SendJsonAsync(
            HttpMethod.Delete,
            $"webhooks/{NormalizeSnowflake(invocation.ResourceId)}",
            body: null,
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
        if (input.StoredWebhookId is not null)
        {
            await webhookVault.DeleteAsync(input.StoredWebhookId, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<JsonElement?> WriteSendWebhookAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<SendWebhookMessageInput>(invocation);
        var secret = await webhookVault.GetAsync(input.StoredWebhookId, cancellationToken).ConfigureAwait(false);
        var assets = await GetAssetsAsync(input.AssetIds, cancellationToken).ConfigureAwait(false);
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds) ?? [];
        var poll = input.Poll is null ? null : NormalizeWebhookPoll(input.Poll);
        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["allowed_mentions"] = AllowedMentionsPayload(input.AllowedMentions),
            ["flags"] = RichMessagePayloadNormalizer.AddMessageFlags(
                0,
                input.SuppressEmbeds,
                input.SuppressNotifications,
                voiceMessage: false,
                componentsV2: components.HasValue)
        };
        if (input.Content is not null) payload["content"] = NormalizeContent(input.Content);
        if (input.UsernameOverride is not null) payload["username"] = NormalizeText(input.UsernameOverride, 80, false);
        if (input.AvatarUrlOverride is not null) payload["avatar_url"] = RichMessagePayloadNormalizer.NormalizeWebUrl(input.AvatarUrlOverride);
        if (input.Tts) payload["tts"] = true;
        if (!embeds.IsEmpty) payload["embeds"] = RichMessagePayloadNormalizer.EmbedsPayload(embeds);
        if (poll is not null) payload["poll"] = WebhookPollPayload(poll, input.Poll!.DurationHours);
        if (components.HasValue) payload["components"] = components.Value.Payload;
        if (input.ThreadName is not null)
        {
            payload["thread_name"] = NormalizeText(input.ThreadName, 100, false);
            payload["applied_tags"] = input.AppliedTagIds.HasValue
                ? input.AppliedTagIds.Value.Select(NormalizeSnowflake).ToArray()
                : Array.Empty<string>();
        }

        if (!assets.IsEmpty)
        {
            payload["attachments"] = assets.Select((asset, index) => (object)new Dictionary<string, object?>
            {
                ["id"] = index,
                ["filename"] = asset.Filename
            }).ToArray();
        }

        var route = WebhookExecutionRoute(secret, input.ThreadId, messageId: null, withComponents: components.HasValue);
        return await SendWebhookPayloadAsync(HttpMethod.Post, route, payload, assets, invocation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> WriteEditWebhookAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<EditWebhookMessageInput>(invocation);
        var secret = await webhookVault.GetAsync(input.StoredWebhookId, cancellationToken).ConfigureAwait(false);
        var assets = await GetAssetsAsync(input.NewAssetIds, cancellationToken).ConfigureAwait(false);
        var attachments = input.RetainedAttachmentIds.Select(id => (object)new Dictionary<string, object?> { ["id"] = NormalizeSnowflake(id) }).ToList();
        for (var index = 0; index < assets.Length; index++)
        {
            attachments.Add(new Dictionary<string, object?> { ["id"] = index, ["filename"] = assets[index].Filename });
        }

        var desired = DeserializeState<WebhookMessageExpectation>(preparation.DesiredStateJson);
        var clear = NormalizeClear(input.Clear ?? [], ["content"]);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["allowed_mentions"] = AllowedMentionsPayload(input.AllowedMentions),
            ["attachments"] = attachments
        };
        if (input.Content is not null || clear.Contains("content"))
            payload["content"] = clear.Contains("content") ? null : NormalizeContent(input.Content!);
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds);
        if (embeds.HasValue) payload["embeds"] = RichMessagePayloadNormalizer.EmbedsPayload(embeds.Value);
        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames, allowEmpty: true);
        if (components.HasValue) payload["components"] = components.Value.Payload;
        if (input.SuppressEmbeds.HasValue || components.HasValue && components.Value.Count > 0) payload["flags"] = desired.Flags;
        return await SendWebhookPayloadAsync(
            HttpMethod.Patch,
            WebhookExecutionRoute(secret, input.ThreadId, input.MessageId, withComponents: components.HasValue),
            payload,
            assets,
            invocation,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> WriteDeleteWebhookMessageAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteWebhookMessageInput>(invocation);
        var secret = await webhookVault.GetAsync(input.StoredWebhookId, cancellationToken).ConfigureAwait(false);
        return await transport.SendJsonAsync(
            HttpMethod.Delete,
            WebhookExecutionRoute(secret, input.ThreadId, input.MessageId),
            body: null,
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonElement?> WriteCreateTemplateAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<CreateGuildTemplateInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Post,
            $"guilds/{GuildId()}/templates",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["name"] = NormalizeText(input.Name, 100, false),
                ["description"] = input.Description
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteSyncTemplateAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken) =>
        transport.SendJsonAsync(
            HttpMethod.Put,
            TemplateRoute(invocation.ResourceId),
            JsonSerializer.SerializeToElement(new { }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);

    private Task<JsonElement?> WriteUpdateTemplateAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildTemplateInput>(invocation);
        var body = new Dictionary<string, object?>();
        if (input.Name is not null) body["name"] = NormalizeText(input.Name, 100, false);
        if (input.Description is not null) body["description"] = NormalizeText(input.Description, 120, true);
        if (input.Clear.Contains("description", StringComparer.Ordinal)) body["description"] = null;
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            TemplateRoute(invocation.ResourceId),
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private async Task<CatalogMutationObservation> ObserveCreatedInviteAsync(
        CatalogMutationInvocation invocation,
        string desiredJson,
        string? receipt,
        bool targeted,
        CancellationToken cancellationToken)
    {
        var code = ReceiptString(receipt, "code");
        var observed = await TryGetInviteAsync(code, cancellationToken).ConfigureAwait(false);
        if (observed is null) return UnexpectedMissing(code);
        var desired = DeserializeState<InviteCreationExpectation>(desiredJson);
        var matches = desired.Matches(observed);
        if (matches && targeted)
        {
            var job = await GetTargetJobAsync(code, cancellationToken).ConfigureAwait(false);
            matches = job.Status is 1 or 2;
        }

        return new CatalogMutationObservation(
            matches ? CatalogMutationObservationKind.DesiredState : CatalogMutationObservationKind.UnexpectedState,
            State(observed));
    }

    private async Task<CatalogMutationObservation> ObserveTargetJobAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        string desiredJson,
        CancellationToken cancellationToken)
    {
        var desired = DeserializeState<InviteTargetUpdateExpectation>(desiredJson);
        var job = await GetTargetJobAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (job.Status == 2)
        {
            var users = await catalogReader.ListInviteTargetUsersAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            return new CatalogMutationObservation(
                users.UserCount == desired.UserCount && users.StateDigest == desired.TargetDigest
                    ? CatalogMutationObservationKind.DesiredState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(users));
        }

        return new CatalogMutationObservation(
            job.Status == 1 ? CatalogMutationObservationKind.DesiredState : CatalogMutationObservationKind.UnexpectedState,
            State(job));
    }

    private async Task<CatalogMutationObservation> ObserveDeletedInviteAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        CancellationToken cancellationToken)
    {
        var observed = await TryGetInviteAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        return observed is null
            ? new CatalogMutationObservation(CatalogMutationObservationKind.DesiredState, State(new ResourceExistenceState(invocation.ResourceId, false)))
            : Observation(observed, beforeJson, State(new ResourceExistenceState(invocation.ResourceId, false)));
    }

    private async Task<CatalogMutationObservation> ObserveCreatedWebhookAsync(
        string desiredJson,
        string? receipt,
        CancellationToken cancellationToken)
    {
        var id = ReceiptString(receipt, "id");
        var vaultId = ReceiptString(receipt, "vault_id");
        var observed = await TryGetWebhookAsync(id, cancellationToken).ConfigureAwait(false);
        if (observed is null) return UnexpectedMissing(id);
        var desired = DeserializeState<WebhookCreationExpectation>(desiredJson);
        return new CatalogMutationObservation(
            desired.Matches(observed) ? CatalogMutationObservationKind.DesiredState : CatalogMutationObservationKind.UnexpectedState,
            State(new StoredWebhookObservation(vaultId, observed)));
    }

    private async Task<CatalogMutationObservation> ObserveWebhookAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        string desiredJson,
        CancellationToken cancellationToken)
    {
        var observed = await TryGetWebhookAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (observed is null) return UnexpectedMissing(invocation.ResourceId);
        var desired = DeserializeState<WebhookUpdateExpectation>(desiredJson);
        return new CatalogMutationObservation(
            desired.Matches(observed) ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(State(observed), beforeJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            State(observed));
    }

    private async Task<CatalogMutationObservation> ObserveDeletedWebhookAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        CancellationToken cancellationToken)
    {
        var observed = await TryGetWebhookAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        return observed is null
            ? new CatalogMutationObservation(CatalogMutationObservationKind.DesiredState, State(new ResourceExistenceState(invocation.ResourceId, false)))
            : Observation(observed, beforeJson, State(new ResourceExistenceState(invocation.ResourceId, false)));
    }

    private async Task<CatalogMutationObservation> ObserveSentWebhookMessageAsync(
        CatalogMutationInvocation invocation,
        string desiredJson,
        string? receipt,
        CancellationToken cancellationToken)
    {
        var id = ReceiptString(receipt, "id");
        var desired = DeserializeState<WebhookMessageExpectation>(desiredJson);
        var channelId = desired.ChannelId ?? ReceiptString(receipt, "channel_id");
        var message = await TryGetMessageAsync(channelId, id, cancellationToken).ConfigureAwait(false);
        var threadMatches = true;
        if (message is not null && desired.ThreadName is not null)
        {
            var thread = await GetGuildChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
            threadMatches = thread.ParentId == desired.ParentChannelId && thread.Name == desired.ThreadName &&
                thread.AppliedTags.SequenceEqual(desired.AppliedTagIds, StringComparer.Ordinal);
        }

        return message is not null && desired.Matches(message) && threadMatches
            ? new CatalogMutationObservation(CatalogMutationObservationKind.DesiredState, State(MessageState.FromMessage(message)))
            : UnexpectedMissing(id);
    }

    private async Task<CatalogMutationObservation> ObserveEditedWebhookMessageAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        string desiredJson,
        CancellationToken cancellationToken)
    {
        var input = Arguments<EditWebhookMessageInput>(invocation);
        var secret = await webhookVault.GetAsync(input.StoredWebhookId, cancellationToken).ConfigureAwait(false);
        var channel = input.ThreadId is null ? secret.ChannelId : NormalizeSnowflake(input.ThreadId);
        var message = await TryGetMessageAsync(channel, input.MessageId, cancellationToken).ConfigureAwait(false);
        if (message is null) return UnexpectedMissing(input.MessageId);
        var desired = DeserializeState<WebhookMessageExpectation>(desiredJson);
        return new CatalogMutationObservation(
            desired.Matches(message) ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(State(MessageState.FromMessage(message)), beforeJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            State(MessageState.FromMessage(message)));
    }

    private async Task<CatalogMutationObservation> ObserveDeletedWebhookMessageAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteWebhookMessageInput>(invocation);
        var secret = await webhookVault.GetAsync(input.StoredWebhookId, cancellationToken).ConfigureAwait(false);
        var channel = input.ThreadId is null ? secret.ChannelId : NormalizeSnowflake(input.ThreadId);
        var message = await TryGetMessageAsync(channel, input.MessageId, cancellationToken).ConfigureAwait(false);
        return message is null
            ? new CatalogMutationObservation(CatalogMutationObservationKind.DesiredState, State(new MessageExistenceState(channel, input.MessageId, false)))
            : Observation(new MessageExistenceState(channel, input.MessageId, true), beforeJson, State(new MessageExistenceState(channel, input.MessageId, false)));
    }

    private async Task<CatalogMutationObservation> ObserveCreatedTemplateAsync(
        string desiredJson,
        string? receipt,
        CancellationToken cancellationToken)
    {
        var code = ReceiptString(receipt, "code");
        var template = await TryGetTemplateAsync(code, cancellationToken).ConfigureAwait(false);
        if (template is null) return UnexpectedMissing(code);
        var desired = DeserializeState<TemplateCreationExpectation>(desiredJson);
        return new CatalogMutationObservation(
            desired.Matches(template) ? CatalogMutationObservationKind.DesiredState : CatalogMutationObservationKind.UnexpectedState,
            State(template));
    }

    private async Task<CatalogMutationObservation> ObserveSyncedTemplateAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        string desiredJson,
        CancellationToken cancellationToken)
    {
        var template = await TryGetTemplateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (template is null) return UnexpectedMissing(invocation.ResourceId);
        var desired = DeserializeState<TemplateSyncExpectation>(desiredJson);
        return new CatalogMutationObservation(
            template.UpdatedAt > desired.PreviousUpdatedAt && template.IsDirty == false
                ? CatalogMutationObservationKind.DesiredState
                : JsonEqual(State(template), beforeJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            State(template));
    }

    private async Task<CatalogMutationObservation> ObserveTemplateAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        string desiredJson,
        CancellationToken cancellationToken)
    {
        var template = await TryGetTemplateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (template is null) return UnexpectedMissing(invocation.ResourceId);
        var desired = DeserializeState<GuildTemplateView>(desiredJson);
        var matches = template.Code == desired.Code && template.Name == desired.Name &&
            template.Description == desired.Description && template.SourceGuildId == desired.SourceGuildId;
        return new CatalogMutationObservation(
            matches ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(State(template), beforeJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            State(template));
    }

    private async Task<CatalogMutationObservation> ObserveDeletedTemplateAsync(
        CatalogMutationInvocation invocation,
        string beforeJson,
        CancellationToken cancellationToken)
    {
        var template = await TryGetTemplateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        return template is null
            ? new CatalogMutationObservation(CatalogMutationObservationKind.DesiredState, State(new ResourceExistenceState(invocation.ResourceId, false)))
            : Observation(template, beforeJson, State(new ResourceExistenceState(invocation.ResourceId, false)));
    }

    private async Task<CreateInviteInput> NormalizeInviteAsync(CreateInviteInput input, CancellationToken cancellationToken)
    {
        var channel = await GetGuildChannelAsync(input.ChannelId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedChannelStateDigest, channel.ComputeDigest());
        if (input.MaxAgeSeconds is < 0 or > 604_800 || input.MaxUses is < 0 or > 100 || input.RoleIds.Length > 100)
            throw InvalidArgument("Invite lifetime, use count, or role count exceeds Discord limits.");
        var target = input.TargetType?.Trim().ToLowerInvariant();
        if (target is not null and not "stream" and not "application") throw InvalidArgument("Invite target type must be stream or application.");
        if (target == "stream" && input.TargetUserId is null || target == "application" && input.TargetApplicationId is null ||
            target is null && (input.TargetUserId is not null || input.TargetApplicationId is not null))
            throw InvalidArgument("Invite target fields do not match target type.");
        return input with
        {
            ChannelId = channel.Id,
            TargetType = target,
            TargetUserId = input.TargetUserId is null ? null : NormalizeSnowflake(input.TargetUserId),
            TargetApplicationId = input.TargetApplicationId is null ? null : NormalizeSnowflake(input.TargetApplicationId),
            RoleIds = NormalizeIds(input.RoleIds, 100)
        };
    }

    private static Dictionary<string, object?> InvitePayload(CreateInviteInput input) => new()
    {
        ["max_age"] = input.MaxAgeSeconds,
        ["max_uses"] = input.MaxUses,
        ["temporary"] = input.Temporary,
        ["unique"] = input.Unique,
        ["target_type"] = input.TargetType switch { "stream" => 1, "application" => 2, _ => null },
        ["target_user_id"] = input.TargetUserId,
        ["target_application_id"] = input.TargetApplicationId,
        ["role_ids"] = input.RoleIds
    };

    private async Task<StoredWebhookSecret> GetVerifiedSecretAsync(string vaultId, string expectedDigest, CancellationToken cancellationToken)
    {
        var secret = await webhookVault.GetAsync(vaultId, cancellationToken).ConfigureAwait(false);
        var webhook = await extendedReader.GetWebhookAsync(secret.WebhookId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(expectedDigest, webhook.StateDigest);
        return secret;
    }

    private async Task<ChannelApiState> GetGuildChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        var state = ChannelApiState.Parse(await transport.GetJsonAsync($"channels/{NormalizeSnowflake(channelId)}", cancellationToken).ConfigureAwait(false));
        return state.GuildId == GuildId() ? state : throw new StewardException(StewardErrorCodes.GuildMismatch, "Channel belongs to another guild.");
    }

    private async Task EnsureConfigurationDigestAsync(string expected, CancellationToken cancellationToken)
    {
        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var current = ConfigurationViewFactory.CreateSnapshot(snapshot);
        EnsureDigest(expected, current.StateDigest);
    }

    private async Task AddAvatarAsync(Dictionary<string, object?> body, string? assetId, CancellationToken cancellationToken)
    {
        if (assetId is null) return;
        var asset = await ValidateImageAssetAsync(assetId, cancellationToken).ConfigureAwait(false);
        body["avatar"] = $"data:{asset.MediaType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(await assetVault.GetContentPathAsync(assetId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false))}";
    }

    private async Task<AssetView> ValidateImageAssetAsync(string? assetId, CancellationToken cancellationToken)
    {
        if (assetId is null) throw InvalidArgument("Image asset ID is required.");
        var asset = await assetVault.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        return asset.MediaType.StartsWith("image/", StringComparison.Ordinal) && asset.ByteLength <= 256 * 1024
            ? asset
            : throw InvalidArgument("Webhook avatar must be an image no larger than 256 KiB.");
    }

    private async Task<ImmutableArray<AssetView>> GetAssetsAsync(ImmutableArray<string> ids, CancellationToken cancellationToken)
    {
        if (ids.IsDefault || ids.Length > 10 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw InvalidArgument("Webhook assets must contain at most 10 unique IDs.");
        var result = ImmutableArray.CreateBuilder<AssetView>();
        foreach (var id in ids) result.Add(await assetVault.GetAsync(id, cancellationToken).ConfigureAwait(false));
        if (result.Sum(item => item.ByteLength) > 25L * 1024 * 1024) throw InvalidArgument("Webhook assets exceed 25 MiB.");
        return result.ToImmutable();
    }

    private async Task<JsonElement?> SendWebhookPayloadAsync(
        HttpMethod method,
        string route,
        Dictionary<string, object?> payload,
        ImmutableArray<AssetView> assets,
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (assets.IsEmpty)
            return await transport.SendJsonAsync(method, route, JsonSerializer.SerializeToElement(payload, CatalogJson.Options), AuditReason(invocation), cancellationToken).ConfigureAwait(false);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(payload, CatalogJson.Options), Encoding.UTF8, "application/json"), "payload_json");
        for (var index = 0; index < assets.Length; index++)
        {
            var content = new ByteArrayContent(await File.ReadAllBytesAsync(await assetVault.GetContentPathAsync(assets[index].AssetId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(assets[index].MediaType);
            multipart.Add(content, $"files[{index}]", assets[index].Filename);
        }
        return await transport.SendContentAsync(method, route, multipart, AuditReason(invocation), cancellationToken).ConfigureAwait(false);
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
        if (input is null || input.UserIds.Length > 100 || input.RoleIds.Length > 100) throw InvalidArgument("Allowed mentions exceed limits.");
        _ = input.UserIds.Select(NormalizeSnowflake).ToArray();
        _ = input.RoleIds.Select(NormalizeSnowflake).ToArray();
    }

    private static WebhookPollExpectation NormalizeWebhookPoll(WebhookPollInput input)
    {
        if (input.DurationHours is < 1 or > 768 || input.Answers.Length is < 2 or > 10)
            throw InvalidArgument("Webhook poll duration or answer count is outside Discord limits.");
        var answers = input.Answers.Select(answer =>
        {
            if (answer.EmojiId is not null && answer.EmojiName is not null)
                throw InvalidArgument("Webhook poll answer emoji ID and name are mutually exclusive.");
            return answer with
            {
                Text = NormalizeText(answer.Text, 55, false),
                EmojiId = answer.EmojiId is null ? null : NormalizeSnowflake(answer.EmojiId),
                EmojiName = answer.EmojiName is null ? null : NormalizeText(answer.EmojiName, 100, false)
            };
        }).ToImmutableArray();
        return new WebhookPollExpectation(
            NormalizeText(input.Question, 300, false),
            answers,
            input.AllowMultiselect);
    }

    private static Dictionary<string, object?> WebhookPollPayload(WebhookPollExpectation poll, int durationHours) => new()
    {
        ["question"] = new Dictionary<string, object?> { ["text"] = poll.Question },
        ["answers"] = poll.Answers.Select(answer => (object)new Dictionary<string, object?>
        {
            ["poll_media"] = new Dictionary<string, object?>
            {
                ["text"] = answer.Text,
                ["emoji"] = answer.EmojiId is null && answer.EmojiName is null ? null : new Dictionary<string, object?>
                {
                    ["id"] = answer.EmojiId,
                    ["name"] = answer.EmojiName
                }
            }
        }).ToArray(),
        ["duration"] = durationHours,
        ["allow_multiselect"] = poll.AllowMultiselect,
        ["layout_type"] = 1
    };

    private static ImmutableArray<string> NormalizeWebhookTags(
        ImmutableArray<string>? tags,
        ChannelApiState parent)
    {
        var source = !tags.HasValue || tags.Value.IsDefault ? [] : tags.Value;
        if (source.Length > 5) throw InvalidArgument("Webhook forum threads support at most five tags.");
        var normalized = source.Select(NormalizeSnowflake).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var available = parent.AvailableTags.Select(tag => tag.Id).ToHashSet(StringComparer.Ordinal);
        return normalized.All(available.Contains)
            ? normalized
            : throw new StewardException(StewardErrorCodes.StateConflict, "A webhook forum tag is no longer available.");
    }

    private static long ApplyWebhookEditFlags(long current, bool? suppressEmbeds, bool setsComponentsV2)
    {
        var flags = suppressEmbeds switch
        {
            true => current | SuppressEmbedsFlag,
            false => current & ~SuppressEmbedsFlag,
            null => current
        };
        return setsComponentsV2 ? flags | ComponentsV2Flag : flags;
    }

    private static string WebhookExecutionRoute(
        StoredWebhookSecret secret,
        string? threadId,
        string? messageId,
        bool withComponents = false)
    {
        var route = $"webhooks/{secret.WebhookId}/{Uri.EscapeDataString(secret.Token)}";
        if (messageId is not null) route += $"/messages/{NormalizeSnowflake(messageId)}";
        var query = new List<string>();
        if (messageId is null) query.Add("wait=true");
        if (threadId is not null) query.Add($"thread_id={NormalizeSnowflake(threadId)}");
        if (withComponents) query.Add("with_components=true");
        return query.Count == 0 ? route : $"{route}?{string.Join('&', query)}";
    }

    private async Task<InviteView?> TryGetInviteAsync(string code, CancellationToken cancellationToken)
    {
        try { return await extendedReader.GetInviteAsync(NormalizeCode(code), cancellationToken).ConfigureAwait(false); }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound) { return null; }
    }

    private async Task<WebhookView?> TryGetWebhookAsync(string id, CancellationToken cancellationToken)
    {
        try { return await extendedReader.GetWebhookAsync(id, cancellationToken).ConfigureAwait(false); }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound) { return null; }
    }

    private async Task<MessageView?> TryGetMessageAsync(string channel, string id, CancellationToken cancellationToken)
    {
        try { return await messageReader.GetMessageAsync(channel, id, cancellationToken).ConfigureAwait(false); }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound) { return null; }
    }

    private async Task<GuildTemplateView?> TryGetTemplateAsync(string code, CancellationToken cancellationToken)
    {
        try { return await catalogReader.GetGuildTemplateAsync(code, cancellationToken).ConfigureAwait(false); }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound) { return null; }
    }

    private async Task<TargetJobState> GetTargetJobAsync(string code, CancellationToken cancellationToken)
    {
        var root = await transport.GetJsonAsync($"invites/{EscapeCode(code)}/target-users/job-status", cancellationToken).ConfigureAwait(false);
        return new TargetJobState(
            NormalizeCode(code),
            RequiredInt(root, "status"),
            RequiredInt(root, "total_users"),
            RequiredInt(root, "processed_users"));
    }

    private string TemplateRoute(string code) => $"guilds/{GuildId()}/templates/{EscapeCode(code)}";
    private static byte[] TargetCsv(ImmutableArray<string> users) => Encoding.UTF8.GetBytes($"user_id\n{string.Join('\n', users)}\n");
    private static string TargetDigest(ImmutableArray<string> users) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', users))));

    private static ImmutableArray<string> NormalizeTargetUsers(ImmutableArray<string> users)
    {
        if (users.IsDefaultOrEmpty || users.Length > 10_000) throw InvalidArgument("Targeted invites require 1 to 10000 exact user IDs.");
        return users.Select(NormalizeSnowflake).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableArray<string> NormalizeIds(ImmutableArray<string> values, int maximum)
    {
        if (values.IsDefault || values.Length > maximum) throw InvalidArgument("ID array exceeds limits.");
        return values.Select(NormalizeSnowflake).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableHashSet<string> NormalizeClear(ImmutableArray<string> clear, IEnumerable<string> allowed)
    {
        if (clear.IsDefault) return ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
        var result = clear.Select(value => value.Trim().ToLowerInvariant()).ToImmutableHashSet(StringComparer.Ordinal);
        return result.IsSubsetOf(allowed) ? result : throw InvalidArgument("Clear list contains unsupported fields.");
    }

    private static string NormalizeWebhookName(string value)
    {
        var name = NormalizeText(value, 80, false);
        return name.Contains("clyde", StringComparison.OrdinalIgnoreCase) || name.Contains("discord", StringComparison.OrdinalIgnoreCase)
            ? throw InvalidArgument("Webhook name contains a Discord-reserved term.")
            : name;
    }

    private static string NormalizeContent(string value) => NormalizeText(value, 2_000, true);
    private static string NormalizeText(string value, int maximum, bool allowEmpty)
    {
        var text = value ?? string.Empty;
        return (allowEmpty || !string.IsNullOrWhiteSpace(text)) && text.Length <= maximum &&
            !text.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t')
            ? text
            : throw InvalidArgument("Text is empty, too long, or invalid.");
    }

    private static string NormalizeCode(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? value
            : throw InvalidArgument("Invite or template code is invalid.");
    private static string EscapeCode(string value) => Uri.EscapeDataString(NormalizeCode(value));
    private static string NormalizeSnowflake(string value) => ChannelApiState.NormalizeSnowflake(value);

    private static CatalogMutationObservation Observation<T>(T observed, string before, string desired)
    {
        var json = State(observed);
        return new CatalogMutationObservation(
            JsonEqual(json, desired) ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(json, before) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            json);
    }

    private static bool JsonEqual(string left, string right)
    {
        using var a = JsonDocument.Parse(left);
        using var b = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(a.RootElement, b.RootElement);
    }

    private static CatalogMutationObservation UnexpectedMissing(string id) => new(
        CatalogMutationObservationKind.UnexpectedState,
        State(new ResourceExistenceState(id, Exists: false)));

    private static string ReceiptString(string? receipt, string name)
    {
        if (receipt is null) throw InvalidResponse("write receipt");
        using var document = JsonDocument.Parse(receipt);
        return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw InvalidResponse("write receipt")
            : throw InvalidResponse("write receipt");
    }

    private static string RequiredSnowflake(JsonElement root, string name) => NormalizeSnowflake(RequiredString(root, name, 100));
    private static string RequiredString(JsonElement root, string name, int maximum) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= maximum
            ? text : throw InvalidResponse(name);
    private static int RequiredInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) && parsed >= 0 ? parsed : throw InvalidResponse(name);

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options) ?? throw InvalidArgument("Mutation arguments are invalid.");
    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options) ?? throw InvalidResponse("stored state");
    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static void EnsureDigest(string? supplied, string observed)
    {
        var value = supplied?.Trim().ToLowerInvariant();
        if (value is not { Length: 64 } || !value.All(char.IsAsciiHexDigit)) throw InvalidArgument("Expected digest is invalid.");
        if (value != observed) throw new StewardException(StewardErrorCodes.StateConflict, "Integration state changed after approval.");
    }

    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);
    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);
    private static StewardException PolicyDenied(string message) => new(StewardErrorCodes.PolicyDenied, message);
    private static StewardException InvalidResponse(string resource) => new(StewardErrorCodes.DiscordUnavailable, $"Discord returned invalid {resource} data.");
    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "Integration mutation is not supported.");

    private sealed record ChannelInvitePrecondition(string ChannelId, string StateDigest);
    private sealed record ResourceExistenceState(string ResourceId, bool Exists);
    private sealed record ConfigurationPrecondition(string StateDigest);
    private sealed record TargetJobState(string Code, int Status, int TotalUsers, int ProcessedUsers);
    private sealed record StoredWebhookPrecondition(string VaultId, string WebhookId, string ChannelId);
    private sealed record StoredWebhookObservation(string StoredWebhookId, WebhookView Webhook);
    private sealed record MessageExistenceState(string ChannelId, string MessageId, bool Exists);
    private sealed record ExpectedAttachment(string Filename, long SizeBytes);

    private sealed record InviteCreationExpectation(CreateInviteInput Input, bool Targeted, int TargetUserCount, string TargetUsersDigest)
    {
        internal bool Matches(InviteView invite) =>
            invite.ChannelId == Input.ChannelId && invite.MaxAgeSeconds == Input.MaxAgeSeconds &&
            invite.MaxUses == Input.MaxUses && invite.IsTemporary == Input.Temporary &&
            invite.GrantedRoleIds.SequenceEqual(Input.RoleIds, StringComparer.Ordinal);
    }

    private sealed record InviteTargetUpdateExpectation(string Code, int UserCount, string TargetDigest);

    private sealed record WebhookCreationExpectation(string ChannelId, string Name, string? AvatarHash)
    {
        internal bool Matches(WebhookView webhook) => webhook.ChannelId == ChannelId && webhook.Name == Name &&
            (AvatarHash == "__present__" ? webhook.AvatarHash is not null : webhook.AvatarHash == AvatarHash);
    }

    private sealed record WebhookUpdateExpectation(string Id, string? ChannelId, string Name, string? AvatarHash)
    {
        internal bool Matches(WebhookView webhook) => webhook.Id == Id && webhook.ChannelId == ChannelId && webhook.Name == Name &&
            (AvatarHash == "__present__" ? webhook.AvatarHash is not null : webhook.AvatarHash == AvatarHash);
    }

    private sealed record WebhookPollExpectation(
        string Question,
        ImmutableArray<PollAnswerInput> Answers,
        bool AllowMultiselect)
    {
        internal static WebhookPollExpectation FromView(MessagePollView poll) => new(
            poll.Question,
            poll.Answers.OrderBy(answer => answer.AnswerId)
                .Select(answer => new PollAnswerInput(answer.Text, EmojiId: null, EmojiName: answer.Emoji))
                .ToImmutableArray(),
            poll.AllowsMultiselect);

        internal bool Matches(MessagePollView? poll) => poll is not null && poll.Question == Question &&
            poll.AllowsMultiselect == AllowMultiselect &&
            poll.Answers.OrderBy(answer => answer.AnswerId).Select(answer => answer.Text)
                .SequenceEqual(Answers.Select(answer => answer.Text), StringComparer.Ordinal);
    }

    private sealed record WebhookMessageExpectation(
        string? ChannelId,
        string WebhookId,
        string Content,
        ImmutableArray<ExpectedAttachment> Attachments,
        long Flags,
        RichPayloadState RichPayload,
        string? ExpectedAuthorName,
        string? ExpectedAuthorAvatarHash,
        WebhookPollExpectation? Poll,
        string ParentChannelId,
        string? ThreadName,
        ImmutableArray<string> AppliedTagIds)
    {
        internal bool Matches(MessageView message) => (ChannelId is null || message.ChannelId == ChannelId) &&
            message.AuthorId == WebhookId && message.Content == Content &&
            Attachments.OrderBy(item => item.Filename, StringComparer.Ordinal).ThenBy(item => item.SizeBytes)
                .SequenceEqual(message.Attachments.Select(item => new ExpectedAttachment(item.Filename, item.SizeBytes))
                    .OrderBy(item => item.Filename, StringComparer.Ordinal).ThenBy(item => item.SizeBytes)) &&
            (message.Flags & (SuppressEmbedsFlag | SuppressNotificationsFlag | ComponentsV2Flag)) ==
                (Flags & (SuppressEmbedsFlag | SuppressNotificationsFlag | ComponentsV2Flag)) &&
            (ExpectedAuthorName is null || message.AuthorName == ExpectedAuthorName) &&
            (ExpectedAuthorAvatarHash is null || ExpectedAuthorAvatarHash == "__present__" && message.AuthorAvatarHash is not null) &&
            RichPayloadEquivalent(RichPayload, message) &&
            (Poll is null ? message.Poll is null : Poll.Matches(message.Poll));
    }

    private static bool RichPayloadEquivalent(RichPayloadState expected, MessageView message) =>
        expected.IsTts == message.IsTts &&
        expected.StickerIds.SequenceEqual(message.StickerIds.IsDefault ? [] : message.StickerIds, StringComparer.Ordinal) &&
        JsonEqual(expected.EmbedsJson, message.EmbedsJson) &&
        JsonEqual(expected.ComponentsJson, message.ComponentsJson) &&
        JsonEqual(State(expected.SharedClientTheme), State(message.SharedClientTheme));

    private sealed record MessageState(string ChannelId, string MessageId, string AuthorId, string Content, string StateDigest)
    {
        internal static MessageState FromMessage(MessageView message) => new(message.ChannelId, message.Id, message.AuthorId, message.Content, message.StateDigest);
    }

    private sealed record TemplateCreationExpectation(string Name, string? Description, string GuildId)
    {
        internal bool Matches(GuildTemplateView template) => template.Name == Name && template.Description == Description && template.SourceGuildId == GuildId;
    }

    private sealed record TemplateSyncExpectation(string Code, DateTimeOffset PreviousUpdatedAt);
}
