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
using DiscordSteward.Permissions;
using global::Discord;

namespace DiscordSteward.Operations;

internal sealed class ChannelMutationHandler(
    DiscordApiTransport transport,
    IDiscordGuildReader guildReader,
    IDiscordBotInspector botInspector,
    ConfigurationAnalysisService analysisService,
    StewardPolicy policy,
    StewardRuntimeSettings settings,
    IAssetVault assetVault,
    IDiscordMessageReader messageReader) : ICatalogMutationFamilyHandler
{
    private const long RequireTagFlag = 1L << 4;
    private const long HideMediaDownloadOptionsFlag = 1L << 15;
    private const long SpoilerChannelFlag = 1L << 21;

    private static readonly ImmutableDictionary<string, int> CreationTypes =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["create_category"] = 4,
            ["create_text_channel"] = 0,
            ["create_announcement_channel"] = 5,
            ["create_voice_channel"] = 2,
            ["create_stage_channel"] = 13,
            ["create_forum_channel"] = 15,
            ["create_media_channel"] = 16
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableHashSet<string> ToolNames = CreationTypes.Keys.Concat(new[]
    {
        "update_channel",
        "move_channel",
        "move_channel_and_sync_permissions",
        "reorder_channels",
        "set_role_channel_permissions",
        "remove_role_channel_permissions",
        "set_member_channel_permissions",
        "remove_member_channel_permissions",
        "replace_channel_permission_overwrites",
        "archive_channel",
        "restore_archived_channel",
        "set_voice_channel_status",
        "trigger_typing_indicator",
        "follow_announcement_channel",
        "create_thread",
        "create_forum_post",
        "update_thread",
        "set_thread_member",
        "delete_thread",
        "delete_channel"
    }).ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (CreationTypes.ContainsKey(invocation.ToolName))
        {
            return await PrepareCreateChannelAsync(invocation, cancellationToken).ConfigureAwait(false);
        }

        return invocation.ToolName switch
        {
            "update_channel" => await PrepareUpdateChannelAsync(invocation, cancellationToken).ConfigureAwait(false),
            "move_channel" or "move_channel_and_sync_permissions" =>
                await PrepareMoveAsync(invocation, cancellationToken).ConfigureAwait(false),
            "reorder_channels" => await PrepareReorderAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_role_channel_permissions" or "set_member_channel_permissions" =>
                await PrepareSetOverwriteAsync(invocation, cancellationToken).ConfigureAwait(false),
            "remove_role_channel_permissions" or "remove_member_channel_permissions" =>
                await PrepareRemoveOverwriteAsync(invocation, cancellationToken).ConfigureAwait(false),
            "replace_channel_permission_overwrites" =>
                await PrepareReplaceOverwritesAsync(invocation, cancellationToken).ConfigureAwait(false),
            "archive_channel" => await PrepareArchiveAsync(invocation, cancellationToken).ConfigureAwait(false),
            "restore_archived_channel" => await PrepareRestoreAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_voice_channel_status" => await PrepareVoiceStatusAsync(invocation, cancellationToken).ConfigureAwait(false),
            "trigger_typing_indicator" => await PrepareTypingAsync(invocation, cancellationToken).ConfigureAwait(false),
            "follow_announcement_channel" => await PrepareFollowAsync(invocation, cancellationToken).ConfigureAwait(false),
            "create_thread" => await PrepareCreateThreadAsync(invocation, cancellationToken).ConfigureAwait(false),
            "create_forum_post" => await PrepareForumPostAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_thread" => await PrepareUpdateThreadAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_thread_member" => await PrepareThreadMemberAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_thread" => await PrepareDeleteThreadAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_channel" => await PrepareDeleteChannelAsync(invocation, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };
    }

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response;
        if (CreationTypes.ContainsKey(invocation.ToolName))
        {
            response = await WriteCreateChannelAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            response = invocation.ToolName switch
            {
                "update_channel" => await WriteUpdateChannelAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
                "move_channel" or "move_channel_and_sync_permissions" =>
                    await WriteMoveAsync(invocation, cancellationToken).ConfigureAwait(false),
                "reorder_channels" => await WriteReorderAsync(invocation, cancellationToken).ConfigureAwait(false),
                "set_role_channel_permissions" or "set_member_channel_permissions" =>
                    await WriteSetOverwriteAsync(invocation, cancellationToken).ConfigureAwait(false),
                "remove_role_channel_permissions" or "remove_member_channel_permissions" =>
                    await WriteRemoveOverwriteAsync(invocation, cancellationToken).ConfigureAwait(false),
                "replace_channel_permission_overwrites" =>
                    await WriteReplaceOverwritesAsync(invocation, cancellationToken).ConfigureAwait(false),
                "archive_channel" => await WriteArchiveAsync(invocation, cancellationToken).ConfigureAwait(false),
                "restore_archived_channel" => await WriteRestoreAsync(invocation, cancellationToken).ConfigureAwait(false),
                "set_voice_channel_status" => await WriteVoiceStatusAsync(invocation, cancellationToken).ConfigureAwait(false),
                "trigger_typing_indicator" => await WriteTypingAsync(invocation, cancellationToken).ConfigureAwait(false),
                "follow_announcement_channel" => await WriteFollowAsync(invocation, cancellationToken).ConfigureAwait(false),
                "create_thread" => await WriteCreateThreadAsync(invocation, cancellationToken).ConfigureAwait(false),
                "create_forum_post" => await WriteForumPostAsync(invocation, cancellationToken).ConfigureAwait(false),
                "update_thread" => await WriteUpdateThreadAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
                "set_thread_member" => await WriteThreadMemberAsync(invocation, cancellationToken).ConfigureAwait(false),
                "delete_thread" or "delete_channel" => await transport.SendJsonAsync(
                    HttpMethod.Delete,
                    $"channels/{NormalizeSnowflake(invocation.ResourceId)}",
                    body: null,
                    AuditReason(invocation),
                    cancellationToken).ConfigureAwait(false),
                _ => throw Unsupported()
            };
        }

        return new CatalogMutationWriteReceipt(response?.GetRawText());
    }

    public async Task<CatalogMutationObservation> ObserveAsync(
        CatalogMutationInvocation invocation,
        string beforeStateJson,
        string desiredStateJson,
        string? priorReceiptJson,
        CancellationToken cancellationToken)
    {
        if (CreationTypes.ContainsKey(invocation.ToolName) ||
            invocation.ToolName is "create_thread" or "create_forum_post")
        {
            var createdId = CreatedChannelId(priorReceiptJson);
            var observed = await TryGetChannelAsync(createdId, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new ChannelExistenceState(createdId, Exists: false)));
            }

            var desired = DeserializeState<CreatedChannelExpectation>(desiredStateJson);
            var matches = desired.Matches(observed);
            if (matches && desired.InitialMessage is not null)
            {
                try
                {
                    var messageId = CreatedForumMessageId(priorReceiptJson) ?? createdId;
                    var message = await messageReader.GetMessageAsync(createdId, messageId, cancellationToken).ConfigureAwait(false);
                    matches = desired.InitialMessage.Matches(message);
                }
                catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
                {
                    matches = false;
                }
            }

            return new CatalogMutationObservation(
                matches
                    ? CatalogMutationObservationKind.DesiredState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (invocation.ToolName == "update_channel")
        {
            var observed = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            var desired = DeserializeState<ChannelUpdateExpectation>(desiredStateJson);
            var observedJson = State(observed);
            return new CatalogMutationObservation(
            desired.Matches(observed)
                ? CatalogMutationObservationKind.DesiredState
                : JsonEqual(observedJson, beforeStateJson)
                ? CatalogMutationObservationKind.BeforeState
                : CatalogMutationObservationKind.UnexpectedState,
            observedJson);
        }

        if (invocation.ToolName == "reorder_channels")
        {
            var input = Arguments<ReorderChannelsInput>(invocation);
            var states = await GetSiblingStatesAsync(input.ParentId, cancellationToken).ConfigureAwait(false);
            var observed = new ChannelOrderState(
                input.ParentId,
                states.Select(state => new ChannelPositionInput(state.Id, state.Position))
                    .OrderBy(item => item.ChannelId, StringComparer.Ordinal)
                    .ToImmutableArray());
            return Observation(observed, beforeStateJson, desiredStateJson);
        }

        if (invocation.ToolName == "follow_announcement_channel")
        {
            var input = Arguments<FollowAnnouncementChannelInput>(invocation);
            var webhookId = FollowedWebhookId(priorReceiptJson);
            var webhook = webhookId is null
                ? null
                : await transport.TryGetJsonAsync($"webhooks/{webhookId}", cancellationToken).ConfigureAwait(false);
            var observed = ParseFollowState(webhook, input.SourceChannelId, input.DestinationChannelId, webhookId);
            return Observation(observed, beforeStateJson, desiredStateJson, allowDesiredUnknownIdentity: true);
        }

        if (invocation.ToolName == "set_thread_member")
        {
            var input = Arguments<SetThreadMemberInput>(invocation);
            var member = await transport.TryGetJsonAsync(
                $"channels/{NormalizeSnowflake(input.ThreadId)}/thread-members/{NormalizeSnowflake(input.UserId)}",
                cancellationToken).ConfigureAwait(false);
            var observed = new ThreadMemberPresenceState(
                NormalizeSnowflake(input.ThreadId),
                NormalizeSnowflake(input.UserId),
                member is not null);
            return Observation(observed, beforeStateJson, desiredStateJson);
        }

        if (invocation.ToolName == "trigger_typing_indicator")
        {
            if (!ReceiptAccepted(priorReceiptJson))
            {
                throw new StewardException(
                    StewardErrorCodes.OutcomeUnknown,
                    "Typing indicator acceptance could not be confirmed without repeating the event.");
            }

            return new CatalogMutationObservation(
                CatalogMutationObservationKind.DesiredState,
                desiredStateJson);
        }

        if (invocation.ToolName == "replace_channel_permission_overwrites")
        {
            var currentChannel = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            if (currentChannel.Type == 4)
            {
                var desired = DeserializeState<CategoryOverwriteMutationState>(desiredStateJson);
                var childStates = ImmutableArray.CreateBuilder<ChannelApiState>();
                foreach (var child in desired.SyncedChildren)
                {
                    childStates.Add(await GetGuildChannelAsync(child.Id, cancellationToken).ConfigureAwait(false));
                }

                var observed = new CategoryOverwriteMutationState(
                    currentChannel,
                    childStates.OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray());
                return Observation(observed, beforeStateJson, desiredStateJson);
            }
        }

        if (invocation.ToolName is "delete_thread" or "delete_channel")
        {
            var observed = await TryGetChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.DesiredState,
                    State(new ChannelExistenceState(invocation.ResourceId, Exists: false)));
            }

            var before = DeserializeState<ChannelApiState>(beforeStateJson);
            return new CatalogMutationObservation(
                observed == before
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        var current = await TryGetChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new CatalogMutationObservation(
                CatalogMutationObservationKind.UnexpectedState,
                State(new ChannelExistenceState(invocation.ResourceId, Exists: false)));
        }

        return Observation(current, beforeStateJson, desiredStateJson);
    }

    private async Task<CatalogMutationPreparation> PrepareCreateChannelAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateChannelInput>(invocation);
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedChannelsStateDigest, ResourceStateCanonicalizer.ComputeChannelsDigest(snapshot));
        var type = CreationTypes[invocation.ToolName];
        if (type is 5 or 13 or 15 or 16 && !snapshot.Features.Contains("COMMUNITY", StringComparer.OrdinalIgnoreCase))
        {
            throw InvalidArgument("Announcement, stage, forum, and media channels require the Community guild feature.");
        }

        var name = NormalizeName(input.Name);
        var parentId = type == 4 ? null : await ValidateParentAsync(
            input.ParentId,
            input.ExpectedParentStateDigest,
            cancellationToken).ConfigureAwait(false);
        if (type == 4 && (input.ParentId is not null || input.ExpectedParentStateDigest is not null))
        {
            throw InvalidArgument("Categories cannot have a parent category.");
        }

        ValidateCreateFields(type, input);
        var overwrites = NormalizeOverwrites(input.RoleOverwrites, snapshot, allowMembers: false);
        ImmutableArray<ForumTagExpectation>? availableTags = input.AvailableTags.HasValue
            ? NormalizeForumTags(input.AvailableTags.Value, allowIds: false)
            : null;
        var defaultReaction = input.DefaultReaction is null ? null : NormalizeDefaultReaction(input.DefaultReaction);
        var before = new ChannelCollectionState(ResourceStateCanonicalizer.ComputeChannelsDigest(snapshot));
        var desired = new CreatedChannelExpectation(
            type,
            name,
            parentId,
            input.Position,
            NormalizeOptionalTopic(input.Topic, type),
            input.IsNsfw,
            input.SlowModeSeconds,
            input.Bitrate,
            type == 13 && input.UserLimit == 0 ? 10_000 : input.UserLimit,
            NormalizeRegion(input.RtcRegion),
            input.VideoQualityMode,
            input.DefaultAutoArchiveMinutes,
            type is 15 or 16 && input.DefaultThreadSlowModeSeconds == 0
                ? null
                : input.DefaultThreadSlowModeSeconds,
            input.DefaultSortOrder,
            input.DefaultForumLayout,
            overwrites,
            Archived: null,
            Locked: null,
            Invitable: null,
            AutoArchiveDuration: null,
            AppliedTags: [],
            ExpectedFlags: null,
            AvailableTags: availableTags,
            DefaultReactionEmojiId: defaultReaction?.EmojiId,
            DefaultReactionEmojiName: defaultReaction?.EmojiName);
        return new CatalogMutationPreparation(State(before), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareUpdateChannelAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateChannelInput>(invocation);
        if (input.Set is null)
        {
            throw InvalidArgument("The typed set object is required.");
        }

        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        if (IsThread(current))
        {
            throw InvalidArgument("Thread fields must be changed with update_thread.");
        }

        var clear = NormalizeChannelClearFields(input.Clear);
        var targetType = NormalizeUpdatedChannelType(input.Set.ChannelType, current.Type);
        ValidateUpdateFields(targetType, input.Set, clear);
        if (targetType == 5 && current.Type != 5)
        {
            var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (!snapshot.Features.Contains("COMMUNITY", StringComparer.OrdinalIgnoreCase) &&
                !snapshot.Features.Contains("NEWS", StringComparer.OrdinalIgnoreCase))
            {
                throw InvalidArgument("Announcement conversion requires the Community guild feature.");
            }
        }

        ImmutableArray<ForumTagExpectation>? availableTags = input.Set.AvailableTags.HasValue
            ? NormalizeForumTags(input.Set.AvailableTags.Value, allowIds: true)
            : null;
        var defaultReaction = input.Set.DefaultReaction is null
            ? null
            : NormalizeDefaultReaction(input.Set.DefaultReaction);
        var flags = ApplyNamedChannelFlags(
            current.Flags,
            input.Set.RequireTag,
            input.Set.HideMediaDownloadOptions,
            input.Set.IsSpoiler);
        var desired = current with
        {
            Type = targetType,
            Name = input.Set.Name is null ? current.Name : NormalizeName(input.Set.Name),
            Topic = clear.Contains("topic")
                ? null
                : input.Set.Topic is null
                    ? current.Topic
                    : NormalizeOptionalTopic(input.Set.Topic, targetType),
            Nsfw = input.Set.IsNsfw ?? current.Nsfw,
            RateLimitPerUser = input.Set.SlowModeSeconds ?? current.RateLimitPerUser,
            Bitrate = input.Set.Bitrate ?? current.Bitrate,
            UserLimit = input.Set.UserLimit ?? current.UserLimit,
            RtcRegion = clear.Contains("rtc_region")
                ? null
                : input.Set.RtcRegion is null
                    ? current.RtcRegion
                    : NormalizeRegion(input.Set.RtcRegion),
            VideoQualityMode = input.Set.VideoQualityMode ?? current.VideoQualityMode,
            DefaultAutoArchiveDuration = input.Set.DefaultAutoArchiveMinutes ?? current.DefaultAutoArchiveDuration,
            Flags = flags,
            DefaultReactionEmojiId = clear.Contains("default_reaction")
                ? null
                : defaultReaction?.EmojiId ?? current.DefaultReactionEmojiId,
            DefaultReactionEmojiName = clear.Contains("default_reaction")
                ? null
                : defaultReaction?.EmojiName ?? current.DefaultReactionEmojiName,
            DefaultThreadRateLimitPerUser = input.Set.DefaultThreadSlowModeSeconds ?? current.DefaultThreadRateLimitPerUser,
            DefaultSortOrder = clear.Contains("default_sort_order")
                ? null
                : input.Set.DefaultSortOrder ?? current.DefaultSortOrder,
            DefaultForumLayout = input.Set.DefaultForumLayout ?? current.DefaultForumLayout
        };
        if (desired.Nsfw == true && (desired.Flags & SpoilerChannelFlag) != 0)
        {
            throw InvalidArgument("A spoiler channel cannot also be age-restricted.");
        }

        var expectation = new ChannelUpdateExpectation(desired, availableTags);
        if (expectation.Matches(current))
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The requested update already matches current channel state.");
        }

        return new CatalogMutationPreparation(State(current), State(expectation));
    }

    private async Task<CatalogMutationPreparation> PrepareMoveAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<MoveChannelInput>(invocation);
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedChannelsStateDigest, ResourceStateCanonicalizer.ComputeChannelsDigest(snapshot));
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        var parentId = await ValidateParentAsync(
            input.DestinationParentId,
            input.ExpectedDestinationParentStateDigest,
            cancellationToken).ConfigureAwait(false);
        if (input.Position < 0)
        {
            throw InvalidArgument("Channel position cannot be negative.");
        }

        var desired = current with { ParentId = parentId, Position = input.Position };
        if (invocation.ToolName == "move_channel_and_sync_permissions" &&
            string.Equals(current.ParentId, parentId, StringComparison.Ordinal))
        {
            throw InvalidArgument("Same-parent permission sync must use replace_channel_permission_overwrites.");
        }

        else if (invocation.ToolName == "move_channel_and_sync_permissions")
        {
            if (parentId is null)
            {
                throw InvalidArgument("Move-and-sync requires a destination category.");
            }

            var parent = await GetGuildChannelAsync(parentId, cancellationToken).ConfigureAwait(false);
            desired = desired with { PermissionOverwrites = parent.PermissionOverwrites };
        }

        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareReorderAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ReorderChannelsInput>(invocation);
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedChannelsStateDigest, ResourceStateCanonicalizer.ComputeChannelsDigest(snapshot));
        var parentId = input.ParentId is null ? null : NormalizeSnowflake(input.ParentId);
        var siblings = await GetSiblingStatesAsync(parentId, cancellationToken).ConfigureAwait(false);
        var normalized = input.Positions.Select(item => new ChannelPositionInput(
                NormalizeSnowflake(item.ChannelId),
                item.Position))
            .OrderBy(item => item.ChannelId, StringComparer.Ordinal)
            .ToImmutableArray();
        if (normalized.IsEmpty || normalized.Any(item => item.Position < 0) ||
            normalized.Select(item => item.ChannelId).Distinct(StringComparer.Ordinal).Count() != normalized.Length ||
            normalized.Select(item => item.Position).Distinct().Count() != normalized.Length)
        {
            throw InvalidArgument("Channel reorder requires unique IDs and non-negative unique positions.");
        }

        var expectedIds = siblings.Select(item => item.Id).Order(StringComparer.Ordinal);
        if (!expectedIds.SequenceEqual(normalized.Select(item => item.ChannelId), StringComparer.Ordinal))
        {
            throw InvalidArgument("Channel reorder must include the complete exact sibling set.");
        }

        var before = new ChannelOrderState(
            parentId,
            siblings.Select(item => new ChannelPositionInput(item.Id, item.Position))
                .OrderBy(item => item.ChannelId, StringComparer.Ordinal)
                .ToImmutableArray());
        return new CatalogMutationPreparation(State(before), State(new ChannelOrderState(parentId, normalized)));
    }

    private async Task<CatalogMutationPreparation> PrepareSetOverwriteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetChannelPermissionsInput>(invocation);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        var targetType = invocation.ToolName == "set_role_channel_permissions" ? 0 : 1;
        var targetId = NormalizeSnowflake(input.TargetId);
        var (allow, deny) = NormalizePermissionTriState(input.Allow, input.Deny, input.Inherit);
        var overwrites = current.PermissionOverwrites
            .Where(item => !(item.Id == targetId && item.Type == targetType))
            .Append(new ChannelOverwriteApiState(targetId, targetType, allow, deny))
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        return new CatalogMutationPreparation(State(current), State(current with { PermissionOverwrites = overwrites }));
    }

    private async Task<CatalogMutationPreparation> PrepareRemoveOverwriteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<RemoveChannelPermissionsInput>(invocation);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        var targetType = invocation.ToolName == "remove_role_channel_permissions" ? 0 : 1;
        var targetId = NormalizeSnowflake(input.TargetId);
        if (!current.PermissionOverwrites.Any(item => item.Id == targetId && item.Type == targetType))
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The requested overwrite does not exist.");
        }

        var desired = current with
        {
            PermissionOverwrites = current.PermissionOverwrites
                .Where(item => !(item.Id == targetId && item.Type == targetType))
                .ToImmutableArray()
        };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareReplaceOverwritesAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ReplaceChannelOverwritesInput>(invocation);
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        await ValidateOptionalParentDigestAsync(current.ParentId, input.ExpectedParentStateDigest, cancellationToken).ConfigureAwait(false);
        var overwrites = NormalizeOverwrites(input.Overwrites, snapshot, allowMembers: true);
        if (current.Type == 4)
        {
            var syncedChildren = await ValidateAndGetSyncedChildrenAsync(
                current,
                input.ExpectedSyncedChildren,
                snapshot,
                cancellationToken).ConfigureAwait(false);
            return new CatalogMutationPreparation(
                State(new CategoryOverwriteMutationState(current, syncedChildren)),
                State(new CategoryOverwriteMutationState(
                    current with { PermissionOverwrites = overwrites },
                    syncedChildren
                        .Select(child => child with { PermissionOverwrites = overwrites })
                        .ToImmutableArray())));
        }

        if (!input.ExpectedSyncedChildren.IsDefaultOrEmpty)
        {
            throw InvalidArgument("Synced-child evidence applies only to category overwrite replacement.");
        }

        return new CatalogMutationPreparation(
            State(current),
            State(current with { PermissionOverwrites = overwrites }));
    }

    private async Task<CatalogMutationPreparation> PrepareArchiveAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ArchiveChannelInput>(invocation);
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        var parentId = await ValidateParentAsync(
            input.DestinationParentId,
            input.ExpectedDestinationParentStateDigest,
            cancellationToken).ConfigureAwait(false)
            ?? throw InvalidArgument("Archive destination category is required.");
        var overwrites = NormalizeOverwrites(input.Overwrites, snapshot, allowMembers: true);
        ValidateVisibility(input.Visibility, overwrites);
        await ValidateStewardArchiveAccessAsync(input.Visibility, overwrites, cancellationToken).ConfigureAwait(false);
        var desired = current with
        {
            Name = NormalizeName(input.Name),
            ParentId = parentId,
            PermissionOverwrites = overwrites
        };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareRestoreAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<RestoreArchivedChannelInput>(invocation);
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        var parentId = await ValidateParentAsync(input.ParentId, input.ExpectedParentStateDigest, cancellationToken).ConfigureAwait(false);
        var overwrites = NormalizeOverwrites(input.Overwrites, snapshot, allowMembers: true);
        ValidateVisibility(input.Visibility, overwrites);
        var desired = current with
        {
            Name = NormalizeName(input.Name),
            ParentId = parentId,
            PermissionOverwrites = overwrites
        };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareVoiceStatusAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetVoiceChannelStatusInput>(invocation);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        if (current.Type is not 2 and not 13)
        {
            throw InvalidArgument("Voice status requires a voice or stage channel.");
        }

        var status = input.Status is null ? null : NormalizeBoundedText(input.Status, 500, allowEmpty: true);
        return new CatalogMutationPreparation(State(current), State(current with { VoiceStatus = status }));
    }

    private async Task<CatalogMutationPreparation> PrepareTypingAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<TriggerTypingIndicatorInput>(invocation);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedChannelStateDigest, current.ComputeDigest());
        if (current.Type is not 0 and not 2 and not 5 and not 10 and not 11 and not 12)
        {
            throw InvalidArgument("Typing indicators require a text-capable guild channel or thread.");
        }

        return new CatalogMutationPreparation(
            State(new TypingIndicatorState(current.Id, Accepted: false)),
            State(new TypingIndicatorState(current.Id, Accepted: true)));
    }

    private async Task<CatalogMutationPreparation> PrepareFollowAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<FollowAnnouncementChannelInput>(invocation);
        var source = await GetMutableChannelAsync(input.SourceChannelId, cancellationToken).ConfigureAwait(false);
        var destination = await GetMutableChannelAsync(input.DestinationChannelId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedSourceStateDigest, source.ComputeDigest());
        EnsureDigest(input.ExpectedDestinationStateDigest, destination.ComputeDigest());
        if (source.Type != 5 || destination.Type is not 0 and not 5)
        {
            throw InvalidArgument("Announcement follows require an announcement source and text-like destination.");
        }

        var before = await FindFollowerWebhookAsync(source.Id, destination.Id, cancellationToken).ConfigureAwait(false);
        if (before.Exists)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The announcement channel is already followed at this destination.");
        }

        return new CatalogMutationPreparation(State(before), State(new FollowState(source.Id, destination.Id, Exists: true, WebhookId: null)));
    }

    private async Task<CatalogMutationPreparation> PrepareCreateThreadAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateThreadInput>(invocation);
        var parent = await GetMutableChannelAsync(input.ParentChannelId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedParentStateDigest, parent.ComputeDigest());
        var type = NormalizeThreadType(input.ThreadType);
        ValidateThreadLifecycle(input.AutoArchiveMinutes, input.SlowModeSeconds);
        if (input.SourceMessageId is not null)
        {
            NormalizeSnowflake(input.SourceMessageId);
            if (type == 12 || parent.Type is not 0 and not 5)
            {
                throw InvalidArgument("Message-started threads must be public or announcement threads in a text-like parent.");
            }


            if (parent.Type == 0 && type != 11 || parent.Type == 5 && type != 10)
            {
                throw InvalidArgument("Message-started thread type must match its text or announcement parent.");
            }
        }
        else if (parent.Type is not 0 and not 5)
        {
            throw InvalidArgument("Standalone threads require a text or announcement parent.");
        }

        if (type == 12 && !input.IsInvitable.HasValue)
        {
            throw InvalidArgument("Private thread creation requires an explicit is_invitable value.");
        }

        var desired = new CreatedChannelExpectation(
            type,
            NormalizeName(input.Name),
            parent.Id,
            Position: null,
            Topic: null,
            Nsfw: null,
            RateLimitPerUser: input.SlowModeSeconds,
            Bitrate: null,
            UserLimit: null,
            RtcRegion: null,
            VideoQualityMode: null,
            DefaultAutoArchiveDuration: null,
            DefaultThreadRateLimitPerUser: null,
            DefaultSortOrder: null,
            DefaultForumLayout: null,
            PermissionOverwrites: [],
            Archived: false,
            Locked: false,
            Invitable: type == 12 ? input.IsInvitable : null,
            AutoArchiveDuration: input.AutoArchiveMinutes,
            AppliedTags: []);
        return new CatalogMutationPreparation(State(parent), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareForumPostAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateForumPostInput>(invocation);
        var parent = await GetMutableChannelAsync(input.ParentChannelId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedParentStateDigest, parent.ComputeDigest());
        EnsureDigest(input.ExpectedAvailableTagsDigest, parent.ComputeAvailableTagsDigest());
        if (parent.Type is not 15 and not 16)
        {
            throw InvalidArgument("Forum posts require a forum or media parent.");
        }

        var tags = NormalizeAppliedTags(input.AppliedTagIds, parent);
        ValidateThreadLifecycle(input.AutoArchiveMinutes, input.SlowModeSeconds);
        var content = input.Content is null ? null : NormalizeBoundedText(input.Content, 2_000, allowEmpty: true);
        var mentions = input.AllowedMentions ?? EmptyAllowedMentions();
        ValidateForumAllowedMentions(mentions);
        var assets = await GetForumAssetsAsync(input.AssetIds, cancellationToken).ConfigureAwait(false);
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds) ?? [];
        RichMessagePayloadNormalizer.ValidateEmbedAttachmentReferences(embeds, assets.Select(asset => asset.Filename));
        var stickerIds = RichMessagePayloadNormalizer.NormalizeStickerIds(input.StickerIds);
        await ValidateForumStickersAsync(stickerIds, cancellationToken).ConfigureAwait(false);
        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames);
        if (components.HasValue && (content is not null || !embeds.IsEmpty || !stickerIds.IsEmpty))
        {
            throw InvalidArgument("Components v2 forum posts can contain only static components and referenced assets.");
        }

        if (content is null && assets.IsEmpty && embeds.IsEmpty && stickerIds.IsEmpty && !components.HasValue)
        {
            throw InvalidArgument("A forum post requires content, rich content, or an approved asset.");
        }

        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var desired = new CreatedChannelExpectation(
            Type: 11,
            NormalizeName(input.Name),
            parent.Id,
            Position: null,
            Topic: null,
            Nsfw: null,
            RateLimitPerUser: input.SlowModeSeconds,
            Bitrate: null,
            UserLimit: null,
            RtcRegion: null,
            VideoQualityMode: null,
            DefaultAutoArchiveDuration: null,
            DefaultThreadRateLimitPerUser: null,
            DefaultSortOrder: null,
            DefaultForumLayout: null,
            PermissionOverwrites: [],
            Archived: false,
            Locked: false,
            Invitable: null,
            AutoArchiveDuration: input.AutoArchiveMinutes,
            AppliedTags: tags,
            InitialMessage: new ForumPostMessageExpectation(
                bot.BotUserId,
                content ?? string.Empty,
                assets.Select(asset => new ForumExpectedAttachment(asset.Filename, asset.ByteLength)).ToImmutableArray(),
                input.SuppressNotifications,
                input.SuppressEmbeds,
                components.HasValue,
                new RichPayloadState(
                    IsTts: false,
                    stickerIds,
                    RichMessagePayloadNormalizer.CanonicalEmbedsJson(embeds),
                    components?.CanonicalJson ?? "[]",
                    SharedClientTheme: null)));
        return new CatalogMutationPreparation(State(parent), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareUpdateThreadAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateThreadInput>(invocation);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        EnsureThread(current);
        if (input.Set.AutoArchiveMinutes.HasValue || input.Set.SlowModeSeconds.HasValue)
        {
            ValidateThreadLifecycle(
                input.Set.AutoArchiveMinutes ?? current.AutoArchiveDuration ?? 1440,
                input.Set.SlowModeSeconds ?? current.RateLimitPerUser ?? 0);
        }

        var tags = input.Set.AppliedTagIds.HasValue
            ? NormalizeAppliedTags(input.Set.AppliedTagIds.Value, await GetMutableChannelAsync(current.ParentId!, cancellationToken).ConfigureAwait(false))
            : current.AppliedTags;
        var flags = current.Flags;
        if (input.Set.IsPinned.HasValue)
        {
            flags = input.Set.IsPinned.Value ? flags | 2L : flags & ~2L;
        }

        var desired = current with
        {
            Name = input.Set.Name is null ? current.Name : NormalizeName(input.Set.Name),
            Archived = input.Set.IsArchived ?? current.Archived,
            Locked = input.Set.IsLocked ?? current.Locked,
            AutoArchiveDuration = input.Set.AutoArchiveMinutes ?? current.AutoArchiveDuration,
            RateLimitPerUser = input.Set.SlowModeSeconds ?? current.RateLimitPerUser,
            AppliedTags = tags,
            Invitable = input.Set.IsInvitable ?? current.Invitable,
            Flags = flags
        };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareThreadMemberAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetThreadMemberInput>(invocation);
        var thread = await GetMutableChannelAsync(input.ThreadId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedThreadStateDigest, thread.ComputeDigest());
        EnsureThread(thread);
        if (thread.Archived == true)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Archived threads cannot change membership.");
        }

        var userId = NormalizeSnowflake(input.UserId);
        var existing = await transport.TryGetJsonAsync(
            $"channels/{thread.Id}/thread-members/{userId}",
            cancellationToken).ConfigureAwait(false);
        return new CatalogMutationPreparation(
            State(new ThreadMemberPresenceState(thread.Id, userId, existing is not null)),
            State(new ThreadMemberPresenceState(thread.Id, userId, input.Present)));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteThreadAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteThreadInput>(invocation);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureThread(current);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        return new CatalogMutationPreparation(
            State(current),
            State(new ChannelExistenceState(current.Id, Exists: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteChannelAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteChannelInput>(invocation);
        var current = await GetMutableChannelAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (IsThread(current))
        {
            throw InvalidArgument("Use delete_thread for thread channels.");
        }

        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        var impact = await analysisService.AnalyzeDeletionImpactAsync(
            current.Type == 4 ? "category" : "channel",
            current.Id,
            cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedCascadeDigest, impact.CascadeDigest);
        var expectedBlockers = input.ExpectedBlockers.Order(StringComparer.Ordinal).ToArray();
        if (!expectedBlockers.SequenceEqual(impact.Blockers, StringComparer.Ordinal))
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Deletion blockers changed after approval.");
        }

        if (!policy.IsUnrestricted && !impact.Blockers.IsEmpty)
        {
            throw PolicyDenied("Current deletion analysis contains blockers.");
        }

        var expectedCascade = input.ExpectedCascade
            .Select(item => $"{item.ResourceType}:{item.ResourceId}:{item.Relationship}")
            .Order(StringComparer.Ordinal);
        var actualCascade = impact.Cascade
            .Select(item => $"{item.ResourceType}:{item.ResourceId}:{item.Relationship}")
            .Order(StringComparer.Ordinal);
        if (!expectedCascade.SequenceEqual(actualCascade, StringComparer.Ordinal))
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Deletion cascade changed after approval.");
        }

        return new CatalogMutationPreparation(
            State(current),
            State(new ChannelExistenceState(current.Id, Exists: false)));
    }

    private Task<JsonElement?> WriteCreateChannelAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateChannelInput>(invocation);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = NormalizeName(input.Name),
            ["type"] = CreationTypes[invocation.ToolName],
            ["permission_overwrites"] = OverwritePayload(input.RoleOverwrites)
        };
        AddOptional(payload, "position", input.Position);
        AddOptional(payload, "parent_id", input.ParentId is null ? null : NormalizeSnowflake(input.ParentId));
        AddOptional(payload, "topic", input.Topic);
        AddOptional(payload, "nsfw", input.IsNsfw);
        AddOptional(payload, "rate_limit_per_user", input.SlowModeSeconds);
        AddOptional(payload, "bitrate", input.Bitrate);
        AddOptional(payload, "user_limit", input.UserLimit);
        if (input.RtcRegion is not null) payload["rtc_region"] = input.RtcRegion;
        AddOptional(payload, "video_quality_mode", input.VideoQualityMode);
        AddOptional(payload, "default_auto_archive_duration", input.DefaultAutoArchiveMinutes);
        AddOptional(payload, "default_thread_rate_limit_per_user", input.DefaultThreadSlowModeSeconds);
        AddOptional(payload, "default_sort_order", input.DefaultSortOrder);
        AddOptional(payload, "default_forum_layout", input.DefaultForumLayout);
        if (input.AvailableTags.HasValue)
        {
            payload["available_tags"] = ForumTagsPayload(NormalizeForumTags(input.AvailableTags.Value, allowIds: false));
        }

        if (input.DefaultReaction is not null)
        {
            payload["default_reaction_emoji"] = DefaultReactionPayload(NormalizeDefaultReaction(input.DefaultReaction));
        }

        return transport.SendJsonAsync(
            HttpMethod.Post,
            $"guilds/{GuildId()}/channels",
            JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteUpdateChannelAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateChannelInput>(invocation);
        var desired = DeserializeState<ChannelUpdateExpectation>(preparation.DesiredStateJson);
        var clear = NormalizeChannelClearFields(input.Clear);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddOptional(body, "name", input.Set.Name is null ? null : desired.State.Name);
        AddOptional(body, "type", input.Set.ChannelType is null ? null : desired.State.Type);
        if (input.Set.Topic is not null || clear.Contains("topic")) body["topic"] = desired.State.Topic;
        AddOptional(body, "nsfw", input.Set.IsNsfw);
        AddOptional(body, "rate_limit_per_user", input.Set.SlowModeSeconds);
        AddOptional(body, "bitrate", input.Set.Bitrate);
        AddOptional(body, "user_limit", input.Set.UserLimit);
        if (input.Set.RtcRegion is not null || clear.Contains("rtc_region")) body["rtc_region"] = desired.State.RtcRegion;
        AddOptional(body, "video_quality_mode", input.Set.VideoQualityMode);
        AddOptional(body, "default_auto_archive_duration", input.Set.DefaultAutoArchiveMinutes);
        AddOptional(body, "default_thread_rate_limit_per_user", input.Set.DefaultThreadSlowModeSeconds);
        if (desired.AvailableTags.HasValue) body["available_tags"] = ForumTagsPayload(desired.AvailableTags.Value);
        if (input.Set.DefaultReaction is not null || clear.Contains("default_reaction"))
        {
            body["default_reaction_emoji"] = clear.Contains("default_reaction")
                ? null
                : DefaultReactionPayload(new DefaultReactionInput(
                    desired.State.DefaultReactionEmojiId,
                    desired.State.DefaultReactionEmojiName));
        }

        if (input.Set.DefaultSortOrder.HasValue || clear.Contains("default_sort_order"))
        {
            body["default_sort_order"] = desired.State.DefaultSortOrder;
        }

        AddOptional(body, "default_forum_layout", input.Set.DefaultForumLayout);
        if (input.Set.RequireTag.HasValue || input.Set.HideMediaDownloadOptions.HasValue || input.Set.IsSpoiler.HasValue)
        {
            body["flags"] = desired.State.Flags;
        }

        return PatchChannelAsync(invocation, body, cancellationToken);
    }

    private Task<JsonElement?> WriteMoveAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<MoveChannelInput>(invocation);
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = NormalizeSnowflake(invocation.ResourceId),
            ["position"] = input.Position,
            ["parent_id"] = input.DestinationParentId is null ? null : NormalizeSnowflake(input.DestinationParentId)
        };
        if (invocation.ToolName == "move_channel_and_sync_permissions")
        {
            item["lock_permissions"] = true;
        }

        return transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/channels",
            JsonSerializer.SerializeToElement(new[] { item }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteReorderAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ReorderChannelsInput>(invocation);
        var payload = input.Positions.Select(item => new Dictionary<string, object?>
        {
            ["id"] = NormalizeSnowflake(item.ChannelId),
            ["position"] = item.Position
        });
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/channels",
            JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteSetOverwriteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetChannelPermissionsInput>(invocation);
        var targetType = invocation.ToolName == "set_role_channel_permissions" ? 0 : 1;
        var (allow, deny) = NormalizePermissionTriState(input.Allow, input.Deny, input.Inherit);
        var body = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["allow"] = allow,
            ["deny"] = deny,
            ["type"] = targetType
        }, CatalogJson.Options);
        return transport.SendJsonAsync(
            HttpMethod.Put,
            $"channels/{NormalizeSnowflake(invocation.ResourceId)}/permissions/{NormalizeSnowflake(input.TargetId)}",
            body,
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteRemoveOverwriteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<RemoveChannelPermissionsInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Delete,
            $"channels/{NormalizeSnowflake(invocation.ResourceId)}/permissions/{NormalizeSnowflake(input.TargetId)}",
            body: null,
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteReplaceOverwritesAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ReplaceChannelOverwritesInput>(invocation);
        return PatchChannelAsync(
            invocation,
            new Dictionary<string, object?> { ["permission_overwrites"] = OverwritePayload(input.Overwrites) },
            cancellationToken);
    }

    private Task<JsonElement?> WriteArchiveAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ArchiveChannelInput>(invocation);
        return PatchChannelAsync(invocation, new Dictionary<string, object?>
        {
            ["name"] = NormalizeName(input.Name),
            ["parent_id"] = NormalizeSnowflake(input.DestinationParentId),
            ["permission_overwrites"] = OverwritePayload(input.Overwrites)
        }, cancellationToken);
    }

    private Task<JsonElement?> WriteRestoreAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<RestoreArchivedChannelInput>(invocation);
        return PatchChannelAsync(invocation, new Dictionary<string, object?>
        {
            ["name"] = NormalizeName(input.Name),
            ["parent_id"] = input.ParentId is null ? null : NormalizeSnowflake(input.ParentId),
            ["permission_overwrites"] = OverwritePayload(input.Overwrites)
        }, cancellationToken);
    }

    private Task<JsonElement?> WriteVoiceStatusAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetVoiceChannelStatusInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Put,
            $"channels/{NormalizeSnowflake(invocation.ResourceId)}/voice-status",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["status"] = input.Status }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private async Task<JsonElement?> WriteTypingAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        await transport.SendJsonAsync(
            HttpMethod.Post,
            $"channels/{NormalizeSnowflake(invocation.ResourceId)}/typing",
            body: null,
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(
            new Dictionary<string, object?> { ["accepted"] = true },
            CatalogJson.Options);
    }

    private Task<JsonElement?> WriteFollowAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<FollowAnnouncementChannelInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Post,
            $"channels/{NormalizeSnowflake(input.SourceChannelId)}/followers",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["webhook_channel_id"] = NormalizeSnowflake(input.DestinationChannelId)
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteCreateThreadAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateThreadInput>(invocation);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = NormalizeName(input.Name),
            ["auto_archive_duration"] = input.AutoArchiveMinutes,
            ["rate_limit_per_user"] = input.SlowModeSeconds
        };
        string route;
        if (input.SourceMessageId is null)
        {
            body["type"] = NormalizeThreadType(input.ThreadType);
            if (input.IsInvitable.HasValue) body["invitable"] = input.IsInvitable.Value;
            route = $"channels/{NormalizeSnowflake(input.ParentChannelId)}/threads";
        }
        else
        {
            route = $"channels/{NormalizeSnowflake(input.ParentChannelId)}/messages/{NormalizeSnowflake(input.SourceMessageId)}/threads";
        }

        return transport.SendJsonAsync(
            HttpMethod.Post,
            route,
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private async Task<JsonElement?> WriteForumPostAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateForumPostInput>(invocation);
        var assets = await GetForumAssetsAsync(input.AssetIds, cancellationToken).ConfigureAwait(false);
        var embeds = RichMessagePayloadNormalizer.NormalizeEmbeds(input.Embeds) ?? [];
        var stickerIds = RichMessagePayloadNormalizer.NormalizeStickerIds(input.StickerIds);
        var assetFilenames = assets.ToDictionary(asset => asset.AssetId, asset => asset.Filename, StringComparer.Ordinal);
        var components = RichMessagePayloadNormalizer.NormalizeComponents(input.Components, assetFilenames);
        var message = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["allowed_mentions"] = ForumAllowedMentionsPayload(input.AllowedMentions ?? EmptyAllowedMentions()),
            ["flags"] = RichMessagePayloadNormalizer.AddMessageFlags(
                0,
                input.SuppressEmbeds,
                input.SuppressNotifications,
                voiceMessage: false,
                componentsV2: components.HasValue)
        };
        if (input.Content is not null) message["content"] = NormalizeBoundedText(input.Content, 2_000, allowEmpty: true);
        if (!embeds.IsEmpty) message["embeds"] = RichMessagePayloadNormalizer.EmbedsPayload(embeds);
        if (!stickerIds.IsEmpty) message["sticker_ids"] = stickerIds.ToArray();
        if (components.HasValue) message["components"] = components.Value.Payload;
        if (!assets.IsEmpty)
        {
            message["attachments"] = assets.Select((asset, index) => (object)new Dictionary<string, object?>
            {
                ["id"] = index,
                ["filename"] = asset.Filename
            }).ToArray();
        }

        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = NormalizeName(input.Name),
            ["auto_archive_duration"] = input.AutoArchiveMinutes,
            ["rate_limit_per_user"] = input.SlowModeSeconds,
            ["applied_tags"] = input.AppliedTagIds.Select(NormalizeSnowflake).ToArray(),
            ["message"] = message
        };
        var route = $"channels/{NormalizeSnowflake(input.ParentChannelId)}/threads";
        if (assets.IsEmpty)
        {
            return await transport.SendJsonAsync(
                HttpMethod.Post,
                route,
                JsonSerializer.SerializeToElement(body, CatalogJson.Options),
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false);
        }

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(
            JsonSerializer.Serialize(body, CatalogJson.Options),
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
            HttpMethod.Post,
            route,
            multipart,
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonElement?> WriteUpdateThreadAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateThreadInput>(invocation);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddOptional(body, "name", input.Set.Name);
        AddOptional(body, "archived", input.Set.IsArchived);
        AddOptional(body, "locked", input.Set.IsLocked);
        AddOptional(body, "auto_archive_duration", input.Set.AutoArchiveMinutes);
        AddOptional(body, "rate_limit_per_user", input.Set.SlowModeSeconds);
        AddOptional(body, "invitable", input.Set.IsInvitable);
        if (input.Set.AppliedTagIds.HasValue)
        {
            body["applied_tags"] = input.Set.AppliedTagIds.Value.Select(NormalizeSnowflake).ToArray();
        }

        if (input.Set.IsPinned.HasValue)
        {
            var desired = DeserializeState<ChannelApiState>(preparation.DesiredStateJson);
            body["flags"] = desired.Flags;
        }

        return PatchChannelAsync(invocation, body, cancellationToken);
    }

    private Task<JsonElement?> WriteThreadMemberAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetThreadMemberInput>(invocation);
        return transport.SendJsonAsync(
            input.Present ? HttpMethod.Put : HttpMethod.Delete,
            $"channels/{NormalizeSnowflake(input.ThreadId)}/thread-members/{NormalizeSnowflake(input.UserId)}",
            body: null,
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> PatchChannelAsync(
        CatalogMutationInvocation invocation,
        Dictionary<string, object?> body,
        CancellationToken cancellationToken) => transport.SendJsonAsync(
            HttpMethod.Patch,
            $"channels/{NormalizeSnowflake(invocation.ResourceId)}",
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);

    private async Task<ChannelApiState> GetMutableChannelAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var state = await TryGetChannelAsync(channelId, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        if (!string.Equals(state.GuildId, GuildId(), StringComparison.Ordinal))
        {
            throw new StewardException(StewardErrorCodes.GuildMismatch, "The requested channel belongs to another guild.");
        }

        if (!policy.IsUnrestricted && policy.GetProtection(state.Id, state.Name).IsProtected)
        {
            throw PolicyDenied("Steward policy protects the requested channel.");
        }

        return state;
    }

    private async Task<ChannelApiState> GetGuildChannelAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var state = await TryGetChannelAsync(channelId, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        if (!string.Equals(state.GuildId, GuildId(), StringComparison.Ordinal))
        {
            throw new StewardException(StewardErrorCodes.GuildMismatch, "The requested channel belongs to another guild.");
        }

        return state;
    }

    private async Task<ChannelApiState?> TryGetChannelAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var root = await transport.TryGetJsonAsync(
            $"channels/{NormalizeSnowflake(channelId)}",
            cancellationToken).ConfigureAwait(false);
        return root.HasValue ? ChannelApiState.Parse(root.Value) : null;
    }

    private async Task<GuildSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(snapshot.Id, GuildId(), StringComparison.Ordinal)
            ? snapshot
            : throw new StewardException(StewardErrorCodes.GuildMismatch, "Discord returned a different guild.");
    }

    private async Task<string?> ValidateParentAsync(
        string? parentId,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            if (expectedDigest is not null)
            {
                throw InvalidArgument("Parent digest cannot be supplied without a parent ID.");
            }

            return null;
        }

        var parent = await GetGuildChannelAsync(parentId, cancellationToken).ConfigureAwait(false);
        if (parent.Type != 4)
        {
            throw InvalidArgument("Destination parent must be a category.");
        }

        EnsureDigest(expectedDigest, parent.ComputeDigest());
        return parent.Id;
    }

    private async Task ValidateOptionalParentDigestAsync(
        string? parentId,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        if (expectedDigest is null)
        {
            return;
        }

        if (parentId is null)
        {
            throw InvalidArgument("Parent digest was supplied for an unparented channel.");
        }

        var parent = await GetMutableChannelAsync(parentId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(expectedDigest, parent.ComputeDigest());
    }

    private async Task<ImmutableArray<ChannelApiState>> ValidateAndGetSyncedChildrenAsync(
        ChannelApiState current,
        ImmutableArray<ResourceStateDigestInput> expectedChildren,
        GuildSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var childIds = snapshot.Channels
            .Where(channel => channel.CategoryId == current.Id)
            .Select(channel => channel.Id)
            .ToArray();
        var childStates = ImmutableArray.CreateBuilder<ChannelApiState>();
        foreach (var childId in childIds)
        {
            childStates.Add(await GetGuildChannelAsync(childId, cancellationToken).ConfigureAwait(false));
        }

        var syncedChildren = childStates
            .Where(child => child.PermissionOverwrites.SequenceEqual(current.PermissionOverwrites))
            .OrderBy(child => child.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var suppliedIds = expectedChildren.Select(item => NormalizeSnowflake(item.ResourceId)).Order(StringComparer.Ordinal).ToArray();
        if (!syncedChildren.Select(item => item.Id).SequenceEqual(suppliedIds, StringComparer.Ordinal))
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The category's exact child set changed after approval.");
        }

        foreach (var child in expectedChildren)
        {
            var state = syncedChildren.Single(item => item.Id == NormalizeSnowflake(child.ResourceId));
            EnsureDigest(child.StateDigest, state.ComputeDigest());
        }

        return syncedChildren;
    }

    private async Task<ImmutableArray<ChannelApiState>> GetSiblingStatesAsync(
        string? parentId,
        CancellationToken cancellationToken)
    {
        var root = await transport.GetJsonAsync($"guilds/{GuildId()}/channels", cancellationToken).ConfigureAwait(false);
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse("guild channels");
        }

        var normalizedParent = parentId is null ? null : NormalizeSnowflake(parentId);
        return root.EnumerateArray()
            .Select(ChannelApiState.Parse)
            .Where(channel => channel.Type is not 10 and not 11 and not 12)
            .Where(channel => string.Equals(channel.ParentId, normalizedParent, StringComparison.Ordinal))
            .OrderBy(channel => channel.Position)
            .ThenBy(channel => channel.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private async Task<FollowState> FindFollowerWebhookAsync(
        string sourceChannelId,
        string destinationChannelId,
        CancellationToken cancellationToken)
    {
        var root = await transport.GetJsonAsync($"guilds/{GuildId()}/webhooks", cancellationToken).ConfigureAwait(false);
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse("webhooks");
        }

        foreach (var webhook in root.EnumerateArray())
        {
            var state = ParseFollowState(webhook, sourceChannelId, destinationChannelId, webhookId: null);
            if (state.Exists)
            {
                return state;
            }
        }

        return new FollowState(sourceChannelId, destinationChannelId, Exists: false, WebhookId: null);
    }

    private static FollowState ParseFollowState(
        JsonElement? webhook,
        string sourceChannelId,
        string destinationChannelId,
        string? webhookId)
    {
        if (!webhook.HasValue || webhook.Value.ValueKind != JsonValueKind.Object)
        {
            return new FollowState(sourceChannelId, destinationChannelId, Exists: false, webhookId);
        }

        var root = webhook.Value;
        var id = root.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(idValue.GetString() ?? string.Empty)
            : webhookId;
        var destination = root.TryGetProperty("channel_id", out var channel) && channel.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(channel.GetString() ?? string.Empty)
            : null;
        var source = root.TryGetProperty("source_channel", out var sourceObject) &&
            sourceObject.ValueKind == JsonValueKind.Object &&
            sourceObject.TryGetProperty("id", out var sourceId) && sourceId.ValueKind == JsonValueKind.String
                ? NormalizeSnowflake(sourceId.GetString() ?? string.Empty)
                : null;
        var exists = string.Equals(source, NormalizeSnowflake(sourceChannelId), StringComparison.Ordinal) &&
            string.Equals(destination, NormalizeSnowflake(destinationChannelId), StringComparison.Ordinal);
        return new FollowState(sourceChannelId, destinationChannelId, exists, id);
    }

    private static ImmutableArray<ChannelOverwriteApiState> NormalizeOverwrites(
        ImmutableArray<ChannelOverwriteInput> inputs,
        GuildSnapshot snapshot,
        bool allowMembers)
    {
        if (inputs.IsDefault || inputs.Length > 100)
        {
            throw InvalidArgument("Permission overwrite array is invalid or exceeds 100 entries.");
        }

        var roleIds = snapshot.Roles.Select(role => role.Id).ToHashSet(StringComparer.Ordinal);
        var result = inputs.Select(input =>
        {
            var targetId = NormalizeSnowflake(input.TargetId);
            var type = input.TargetType.Trim().ToLowerInvariant() switch
            {
                "role" => 0,
                "member" when allowMembers => 1,
                "member" => throw InvalidArgument("Member overwrites are not allowed in this operation."),
                _ => throw InvalidArgument("Overwrite target type must be role or member.")
            };
            if (type == 0 && !roleIds.Contains(targetId))
            {
                throw InvalidArgument("Overwrite references a role outside the current guild snapshot.");
            }

            var (allow, deny) = NormalizePermissionTriState(input.Allow, input.Deny, []);
            return new ChannelOverwriteApiState(targetId, type, allow, deny);
        }).OrderBy(item => item.Type).ThenBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();
        if (result.Select(item => $"{item.Type}:{item.Id}").Distinct(StringComparer.Ordinal).Count() != result.Length)
        {
            throw InvalidArgument("Permission overwrite targets must be unique.");
        }

        return result;
    }

    private static object[] OverwritePayload(ImmutableArray<ChannelOverwriteInput> inputs) => inputs
        .Select(input =>
        {
            var type = string.Equals(input.TargetType.Trim(), "role", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            var (allow, deny) = NormalizePermissionTriState(input.Allow, input.Deny, []);
            return (object)new Dictionary<string, object?>
            {
                ["id"] = NormalizeSnowflake(input.TargetId),
                ["type"] = type,
                ["allow"] = allow,
                ["deny"] = deny
            };
        }).ToArray();

    private static (string Allow, string Deny) NormalizePermissionTriState(
        ImmutableArray<string> allow,
        ImmutableArray<string> deny,
        ImmutableArray<string> inherit)
    {
        var allowed = NormalizePermissions(allow);
        var denied = NormalizePermissions(deny);
        var inherited = NormalizePermissions(inherit);
        if (allowed.Intersect(denied, StringComparer.Ordinal).Any() ||
            allowed.Intersect(inherited, StringComparer.Ordinal).Any() ||
            denied.Intersect(inherited, StringComparer.Ordinal).Any())
        {
            throw InvalidArgument("Allow, deny, and inherit permission sets must be disjoint.");
        }

        return (PermissionBits(allowed), PermissionBits(denied));
    }

    private static ImmutableArray<string> NormalizePermissions(ImmutableArray<string> permissions)
    {
        if (permissions.IsDefault)
        {
            return [];
        }

        var known = PermissionCatalog.All.ToImmutableHashSet(StringComparer.Ordinal);
        var normalized = permissions.Select(value => value?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        return normalized.All(known.Contains)
            ? normalized
            : throw InvalidArgument("Permission set contains an unknown named permission.");
    }

    private static string PermissionBits(ImmutableArray<string> permissions)
    {
        ulong bits = 0;
        foreach (var permission in permissions)
        {
            bits |= Convert.ToUInt64(Enum.Parse<GuildPermission>(permission), CultureInfo.InvariantCulture);
        }

        return bits.ToString(CultureInfo.InvariantCulture);
    }

    private void ValidateVisibility(string visibility, ImmutableArray<ChannelOverwriteApiState> overwrites)
    {
        var everyone = overwrites.SingleOrDefault(item => item.Type == 0 && item.Id == GuildId())
            ?? throw InvalidArgument("Archive visibility requires an explicit @everyone overwrite.");
        var viewBit = Convert.ToUInt64(GuildPermission.ViewChannel, CultureInfo.InvariantCulture);
        var allow = ulong.Parse(everyone.Allow, CultureInfo.InvariantCulture);
        var deny = ulong.Parse(everyone.Deny, CultureInfo.InvariantCulture);
        var normalized = visibility.Trim().ToLowerInvariant();
        if (normalized == "public" && (allow & viewBit) == 0 ||
            normalized == "private" && (deny & viewBit) == 0 ||
            normalized is not "public" and not "private")
        {
            throw InvalidArgument("Visibility must be public/private and match the @everyone ViewChannel overwrite.");
        }
    }

    private async Task ValidateStewardArchiveAccessAsync(
        string visibility,
        ImmutableArray<ChannelOverwriteApiState> overwrites,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(visibility.Trim(), "private", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (policy.IsUnrestricted)
        {
            return;
        }

        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var highestRoleId = bot.HighestRoleId ?? throw PolicyDenied("The bot's role ceiling is unavailable.");
        var roleOverwrite = overwrites.SingleOrDefault(item => item.Type == 0 && item.Id == highestRoleId);
        var viewBit = Convert.ToUInt64(GuildPermission.ViewChannel, CultureInfo.InvariantCulture);
        if (roleOverwrite is null ||
            (ulong.Parse(roleOverwrite.Allow, CultureInfo.InvariantCulture) & viewBit) == 0 ||
            (ulong.Parse(roleOverwrite.Deny, CultureInfo.InvariantCulture) & viewBit) != 0)
        {
            throw PolicyDenied("Private archives must explicitly retain ViewChannel for the bot's highest role.");
        }
    }

    private static ImmutableArray<string> NormalizeAppliedTags(
        ImmutableArray<string> tags,
        ChannelApiState parent)
    {
        if (tags.IsDefault || tags.Length > 5)
        {
            throw InvalidArgument("Applied tags must contain at most five exact IDs.");
        }

        var available = parent.AvailableTags.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var normalized = tags.Select(NormalizeSnowflake).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        return normalized.All(available.Contains)
            ? normalized
            : throw new StewardException(StewardErrorCodes.StateConflict, "One or more approved tags are no longer available.");
    }

    private async Task<ImmutableArray<AssetView>> GetForumAssetsAsync(
        ImmutableArray<string>? assetIds,
        CancellationToken cancellationToken)
    {
        var source = !assetIds.HasValue || assetIds.Value.IsDefault ? [] : assetIds.Value;
        if (source.Length > 10 || source.Distinct(StringComparer.Ordinal).Count() != source.Length)
        {
            throw InvalidArgument("Forum post assets must contain at most 10 unique asset IDs.");
        }

        var assets = ImmutableArray.CreateBuilder<AssetView>();
        foreach (var assetId in source)
        {
            assets.Add(await assetVault.GetAsync(assetId, cancellationToken).ConfigureAwait(false));
        }

        if (assets.Sum(asset => asset.ByteLength) > 25L * 1024 * 1024)
        {
            throw InvalidArgument("Forum post attachments exceed Discord's 25 MiB request limit.");
        }

        return assets.ToImmutable();
    }

    private async Task ValidateForumStickersAsync(
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

    private static AllowedMentionsInput EmptyAllowedMentions() => new([], [], Everyone: false, RepliedUser: false);

    private static void ValidateForumAllowedMentions(AllowedMentionsInput input)
    {
        if (input.UserIds.IsDefault || input.RoleIds.IsDefault || input.UserIds.Length > 100 || input.RoleIds.Length > 100)
        {
            throw InvalidArgument("Allowed mentions are limited to 100 exact users and roles.");
        }

        _ = input.UserIds.Select(NormalizeSnowflake).ToArray();
        _ = input.RoleIds.Select(NormalizeSnowflake).ToArray();
    }

    private static Dictionary<string, object?> ForumAllowedMentionsPayload(AllowedMentionsInput input)
    {
        ValidateForumAllowedMentions(input);
        return new Dictionary<string, object?>
        {
            ["parse"] = input.Everyone ? new[] { "everyone" } : Array.Empty<string>(),
            ["users"] = input.UserIds.Select(NormalizeSnowflake).ToArray(),
            ["roles"] = input.RoleIds.Select(NormalizeSnowflake).ToArray(),
            ["replied_user"] = input.RepliedUser
        };
    }

    private static ImmutableHashSet<string> NormalizeChannelClearFields(ImmutableArray<string> fields)
    {
        var source = fields.IsDefault ? [] : fields;
        var normalized = source
            .Select(field => field?.Trim().ToLowerInvariant() ?? string.Empty)
            .ToImmutableHashSet(StringComparer.Ordinal);
        if (normalized.Any(field => field is not "topic" and not "rtc_region" and not "default_reaction" and not "default_sort_order"))
        {
            throw InvalidArgument("Clear accepts only topic, rtc_region, default_reaction, or default_sort_order.");
        }

        return normalized;
    }

    private static int NormalizeUpdatedChannelType(string? value, int currentType)
    {
        if (value is null)
        {
            return currentType;
        }

        var target = value.Trim().ToLowerInvariant() switch
        {
            "text" => 0,
            "announcement" => 5,
            _ => throw InvalidArgument("Channel type conversion accepts only text or announcement.")
        };
        return currentType is 0 or 5
            ? target
            : throw InvalidArgument("Only text and announcement channels can be converted.");
    }

    private static void ValidateUpdateFields(
        int type,
        UpdateChannelSetInput set,
        ImmutableHashSet<string> clear)
    {
        if (type is not 0 and not 2 and not 4 and not 5 and not 13 and not 15 and not 16)
        {
            throw InvalidArgument("The channel type is not supported by update_channel.");
        }

        if (set.Name is null && set.ChannelType is null && set.Topic is null && !set.IsNsfw.HasValue &&
            !set.SlowModeSeconds.HasValue && !set.Bitrate.HasValue && !set.UserLimit.HasValue &&
            set.RtcRegion is null && !set.VideoQualityMode.HasValue && !set.DefaultAutoArchiveMinutes.HasValue &&
            !set.DefaultThreadSlowModeSeconds.HasValue && !set.AvailableTags.HasValue && set.DefaultReaction is null &&
            !set.DefaultSortOrder.HasValue && !set.DefaultForumLayout.HasValue && !set.RequireTag.HasValue &&
            !set.HideMediaDownloadOptions.HasValue && !set.IsSpoiler.HasValue && clear.IsEmpty)
        {
            throw InvalidArgument("At least one channel field must be set or cleared.");
        }

        if (set.Topic is not null && clear.Contains("topic") ||
            set.RtcRegion is not null && clear.Contains("rtc_region") ||
            set.DefaultReaction is not null && clear.Contains("default_reaction") ||
            set.DefaultSortOrder.HasValue && clear.Contains("default_sort_order"))
        {
            throw InvalidArgument("A channel field cannot be set and cleared in the same operation.");
        }

        if (set.SlowModeSeconds is < 0 or > 21_600 || set.DefaultThreadSlowModeSeconds is < 0 or > 21_600)
        {
            throw InvalidArgument("Channel slow mode is outside Discord limits.");
        }

        if (set.DefaultAutoArchiveMinutes.HasValue && !ValidArchiveDuration(set.DefaultAutoArchiveMinutes.Value))
        {
            throw InvalidArgument("Default auto-archive duration is invalid.");
        }

        var textLike = type is 0 or 5 or 15 or 16;
        if (!textLike && (set.Topic is not null || clear.Contains("topic")))
        {
            throw InvalidArgument("Topics apply only to text, announcement, forum, or media channels.");
        }

        if (type == 4 && (set.IsNsfw.HasValue || set.SlowModeSeconds.HasValue))
        {
            throw InvalidArgument("Categories support only name changes through update_channel.");
        }

        if (type is 2 or 13)
        {
            var maximumUserLimit = type == 2 ? 99 : 10_000;
            var maximumBitrate = type == 13 ? 64_000 : 384_000;
            if (set.Bitrate is < 8_000 || set.Bitrate > maximumBitrate ||
                set.UserLimit is < 0 || set.UserLimit > maximumUserLimit ||
                set.VideoQualityMode is not null and not 1 and not 2)
            {
                throw InvalidArgument("Voice or stage settings are outside Discord limits.");
            }
        }
        else if (set.Bitrate.HasValue || set.UserLimit.HasValue || set.RtcRegion is not null ||
                 clear.Contains("rtc_region") || set.VideoQualityMode.HasValue)
        {
            throw InvalidArgument("Voice settings apply only to voice and stage channels.");
        }

        if (type is not 0 and not 5 and not 15 and not 16 &&
            (set.DefaultAutoArchiveMinutes.HasValue || set.DefaultThreadSlowModeSeconds.HasValue))
        {
            throw InvalidArgument("Thread defaults apply only to text, announcement, forum, or media channels.");
        }

        if (type is not 15 and not 16 &&
            (set.AvailableTags.HasValue || set.DefaultReaction is not null || clear.Contains("default_reaction") ||
             set.DefaultSortOrder.HasValue || clear.Contains("default_sort_order") || set.RequireTag.HasValue))
        {
            throw InvalidArgument("Forum and media settings apply only to forum or media channels.");
        }

        if (set.DefaultSortOrder is not null and not 0 and not 1 ||
            set.DefaultForumLayout is not null and not 0 and not 1 and not 2)
        {
            throw InvalidArgument("Forum sorting or layout is invalid.");
        }

        if (type != 15 && set.DefaultForumLayout.HasValue || type != 16 && set.HideMediaDownloadOptions.HasValue)
        {
            throw InvalidArgument("Forum layout and media download controls require their exact channel types.");
        }

        if (type is 4 or 13 && set.IsSpoiler.HasValue)
        {
            throw InvalidArgument("Spoiler mode is not supported for categories or stage channels.");
        }
    }

    private static ImmutableArray<ForumTagExpectation> NormalizeForumTags(
        ImmutableArray<ForumTagInput> tags,
        bool allowIds)
    {
        var source = tags.IsDefault ? [] : tags;
        if (source.Length > 20)
        {
            throw InvalidArgument("Forum and media channels support at most 20 available tags.");
        }

        var normalized = source.Select(tag =>
        {
            var id = tag.Id is null ? null : NormalizeSnowflake(tag.Id);
            if (!allowIds && id is not null)
            {
                throw InvalidArgument("New channel tags cannot provide pre-existing IDs.");
            }

            var name = NormalizeBoundedText(tag.Name?.Trim() ?? string.Empty, 20, allowEmpty: false);
            var emojiId = tag.EmojiId is null ? null : NormalizeSnowflake(tag.EmojiId);
            var emojiName = tag.EmojiName is null
                ? null
                : NormalizeBoundedText(tag.EmojiName, 100, allowEmpty: false);
            if (emojiId is not null && emojiName is not null)
            {
                throw InvalidArgument("A forum tag can use either an emoji ID or emoji name, not both.");
            }

            return new ForumTagExpectation(id, name, tag.Moderated, emojiId, emojiName);
        }).ToImmutableArray();
        if (normalized.Where(tag => tag.Id is not null).Select(tag => tag.Id).Distinct(StringComparer.Ordinal).Count() !=
            normalized.Count(tag => tag.Id is not null) ||
            normalized.Select(tag => $"{tag.Name}\n{tag.Moderated}\n{tag.EmojiId}\n{tag.EmojiName}")
                .Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw InvalidArgument("Forum tag IDs and metadata must be unique.");
        }

        return normalized;
    }

    private static DefaultReactionInput NormalizeDefaultReaction(DefaultReactionInput input)
    {
        var emojiId = input.EmojiId is null ? null : NormalizeSnowflake(input.EmojiId);
        var emojiName = input.EmojiName is null
            ? null
            : NormalizeBoundedText(input.EmojiName, 100, allowEmpty: false);
        if ((emojiId is null) == (emojiName is null))
        {
            throw InvalidArgument("Default reaction requires exactly one emoji ID or emoji name.");
        }

        return new DefaultReactionInput(emojiId, emojiName);
    }

    private static object[] ForumTagsPayload(ImmutableArray<ForumTagExpectation> tags) => tags.Select(tag =>
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = tag.Name,
            ["moderated"] = tag.Moderated,
            ["emoji_id"] = tag.EmojiId,
            ["emoji_name"] = tag.EmojiName
        };
        if (tag.Id is not null) payload["id"] = tag.Id;
        return (object)payload;
    }).ToArray();

    private static Dictionary<string, object?> DefaultReactionPayload(DefaultReactionInput reaction) => new(StringComparer.Ordinal)
    {
        ["emoji_id"] = reaction.EmojiId,
        ["emoji_name"] = reaction.EmojiName
    };

    private static long ApplyNamedChannelFlags(
        long current,
        bool? requireTag,
        bool? hideMediaDownloadOptions,
        bool? isSpoiler)
    {
        var flags = ApplyFlag(current, RequireTagFlag, requireTag);
        flags = ApplyFlag(flags, HideMediaDownloadOptionsFlag, hideMediaDownloadOptions);
        return ApplyFlag(flags, SpoilerChannelFlag, isSpoiler);
    }

    private static long ApplyFlag(long flags, long bit, bool? value) => value switch
    {
        true => flags | bit,
        false => flags & ~bit,
        null => flags
    };

    private static void ValidateCreateFields(int type, CreateChannelInput input)
    {
        if (input.Position is < 0 || input.SlowModeSeconds is < 0 or > 21_600 ||
            input.DefaultThreadSlowModeSeconds is < 0 or > 21_600)
        {
            throw InvalidArgument("Channel position or slow-mode value is outside Discord limits.");
        }

        if (input.DefaultAutoArchiveMinutes.HasValue && !ValidArchiveDuration(input.DefaultAutoArchiveMinutes.Value))
        {
            throw InvalidArgument("Default auto-archive duration is invalid.");
        }

        if (type is 2 or 13)
        {
            var maximumUserLimit = type == 2 ? 99 : 10_000;
            var maximumBitrate = type == 13 ? 64_000 : 384_000;
            if (input.Bitrate is < 8_000 || input.Bitrate > maximumBitrate ||
                input.UserLimit is < 0 || input.UserLimit > maximumUserLimit ||
                input.VideoQualityMode is not null and not 1 and not 2)
            {
                throw InvalidArgument("Voice channel settings are outside Discord limits.");
            }
        }
        else if (input.Bitrate.HasValue || input.UserLimit.HasValue || input.RtcRegion is not null || input.VideoQualityMode.HasValue)
        {
            throw InvalidArgument("Voice settings are valid only for voice and stage channels.");
        }

        if (type is not 15 and not 16 && (input.DefaultSortOrder.HasValue || input.DefaultForumLayout.HasValue))
        {
            throw InvalidArgument("Forum sorting and layout apply only to forum or media channels.");
        }

        if (type is not 15 and not 16 &&
            (input.AvailableTags.HasValue || input.DefaultReaction is not null) ||
            type != 15 && input.DefaultForumLayout.HasValue)
        {
            throw InvalidArgument("Channel creation fields do not match the requested channel type.");
        }

        if (input.DefaultSortOrder is not null and not 0 and not 1 ||
            input.DefaultForumLayout is not null and not 0 and not 1 and not 2)
        {
            throw InvalidArgument("Forum sorting or layout is invalid.");
        }
    }

    private static void ValidateThreadLifecycle(int autoArchiveMinutes, int slowModeSeconds)
    {
        if (!ValidArchiveDuration(autoArchiveMinutes) || slowModeSeconds is < 0 or > 21_600)
        {
            throw InvalidArgument("Thread lifecycle settings are outside Discord limits.");
        }
    }

    private static bool ValidArchiveDuration(int value) => value is 60 or 1_440 or 4_320 or 10_080;

    private static int NormalizeThreadType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "announcement" => 10,
        "public" => 11,
        "private" => 12,
        _ => throw InvalidArgument("Thread type must be announcement, public, or private.")
    };

    private static void EnsureThread(ChannelApiState state)
    {
        if (!IsThread(state))
        {
            throw InvalidArgument("The requested channel is not a thread.");
        }
    }

    private static bool IsThread(ChannelApiState state) => state.Type is 10 or 11 or 12;

    private static string NormalizeName(string value) =>
        NormalizeBoundedText(value?.Trim() ?? string.Empty, 100, allowEmpty: false);

    private static string? NormalizeOptionalTopic(string? value, int type)
    {
        if (value is null)
        {
            return null;
        }

        return NormalizeBoundedText(value, type is 15 or 16 ? 4_096 : 1_024, allowEmpty: true);
    }

    private static string? NormalizeRegion(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return NormalizeBoundedText(value.Trim(), 100, allowEmpty: false);
    }

    private static string NormalizeBoundedText(string value, int maximum, bool allowEmpty)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > maximum ||
            value.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
        {
            throw InvalidArgument($"Text must contain {(allowEmpty ? "0" : "1")} to {maximum.ToString(CultureInfo.InvariantCulture)} valid characters.");
        }

        return value;
    }

    private static CatalogMutationObservation Observation<T>(
        T observed,
        string beforeStateJson,
        string desiredStateJson,
        bool allowDesiredUnknownIdentity = false)
    {
        var observedJson = State(observed);
        var desiredMatch = JsonEqual(observedJson, desiredStateJson);
        if (allowDesiredUnknownIdentity && observed is FollowState observedFollow)
        {
            var desired = DeserializeState<FollowState>(desiredStateJson);
            desiredMatch = observedFollow.Exists == desired.Exists &&
                observedFollow.SourceChannelId == desired.SourceChannelId &&
                observedFollow.DestinationChannelId == desired.DestinationChannelId;
        }

        return new CatalogMutationObservation(
            desiredMatch
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

    private static string CreatedChannelId(string? receiptJson)
    {
        if (receiptJson is null)
        {
            throw new StewardException(StewardErrorCodes.DiscordUnavailable, "Created channel ID is unavailable.");
        }

        using var document = JsonDocument.Parse(receiptJson);
        return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(id.GetString() ?? string.Empty)
            : throw InvalidResponse("created channel receipt");
    }

    private static string? CreatedForumMessageId(string? receiptJson)
    {
        if (receiptJson is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(receiptJson);
        return document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? NormalizeSnowflake(id.GetString() ?? string.Empty)
                : null;
    }

    private static string? FollowedWebhookId(string? receiptJson)
    {
        if (receiptJson is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(receiptJson);
        return document.RootElement.TryGetProperty("webhook_id", out var id) && id.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(id.GetString() ?? string.Empty)
            : null;
    }

    private static bool ReceiptAccepted(string? receiptJson)
    {
        if (receiptJson is null)
        {
            return false;
        }

        using var document = JsonDocument.Parse(receiptJson);
        return document.RootElement.TryGetProperty("accepted", out var accepted) &&
            accepted.ValueKind == JsonValueKind.True;
    }

    private static void AddOptional(Dictionary<string, object?> body, string name, object? value)
    {
        if (value is not null)
        {
            body[name] = value;
        }
    }

    private static string NormalizeSnowflake(string value) => ChannelApiState.NormalizeSnowflake(value);

    private static void EnsureDigest(string? supplied, string observed)
    {
        var normalized = supplied?.Trim().ToLowerInvariant();
        if (normalized is not { Length: 64 } || !normalized.All(char.IsAsciiHexDigit))
        {
            throw InvalidArgument("Expected state digest must be a lowercase SHA-256 digest.");
        }

        if (!string.Equals(normalized, observed, StringComparison.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.StateConflict,
                $"Discord state does not match the approved digest (expected {normalized}, observed {observed}).");
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

    private static StewardException NotFound() => new(StewardErrorCodes.ResourceNotFound, "The requested channel was not found.");

    private static StewardException InvalidResponse(string resource) =>
        new(StewardErrorCodes.DiscordUnavailable, $"Discord returned invalid {resource} data.");

    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "Channel mutation is not supported.");

    private sealed record ChannelCollectionState(string ChannelsStateDigest);

    private sealed record ChannelExistenceState(string ChannelId, bool Exists);

    private sealed record ChannelOrderState(
        string? ParentId,
        ImmutableArray<ChannelPositionInput> Positions);

    private sealed record CategoryOverwriteMutationState(
        ChannelApiState Channel,
        ImmutableArray<ChannelApiState> SyncedChildren);

    private sealed record ThreadMemberPresenceState(
        string ThreadId,
        string UserId,
        bool Present);

    private sealed record TypingIndicatorState(
        string ChannelId,
        bool Accepted);

    private sealed record FollowState(
        string SourceChannelId,
        string DestinationChannelId,
        bool Exists,
        string? WebhookId);

    private sealed record ForumTagExpectation(
        string? Id,
        string Name,
        bool Moderated,
        string? EmojiId,
        string? EmojiName);

    private sealed record ChannelUpdateExpectation(
        ChannelApiState State,
        ImmutableArray<ForumTagExpectation>? AvailableTags)
    {
        internal bool Matches(ChannelApiState observed) =>
            observed with
            {
                PermissionOverwrites = State.PermissionOverwrites,
                AvailableTags = State.AvailableTags,
                AppliedTags = State.AppliedTags
            } == State &&
            observed.PermissionOverwrites.SequenceEqual(State.PermissionOverwrites) &&
            observed.AppliedTags.SequenceEqual(State.AppliedTags, StringComparer.Ordinal) &&
            (!AvailableTags.HasValue || ForumTagsMatch(AvailableTags.Value, observed.AvailableTags));
    }

    private sealed record ForumExpectedAttachment(string Filename, long SizeBytes);

    private sealed record ForumPostMessageExpectation(
        string AuthorId,
        string Content,
        ImmutableArray<ForumExpectedAttachment> Attachments,
        bool SuppressNotifications,
        bool SuppressEmbeds,
        bool ComponentsV2,
        RichPayloadState RichPayload)
    {
        internal bool Matches(MessageView message) =>
            message.AuthorId == AuthorId && message.Content == Content &&
            Attachments.OrderBy(item => item.Filename, StringComparer.Ordinal).ThenBy(item => item.SizeBytes)
                .SequenceEqual(message.Attachments.Select(item => new ForumExpectedAttachment(item.Filename, item.SizeBytes))
                    .OrderBy(item => item.Filename, StringComparer.Ordinal).ThenBy(item => item.SizeBytes)) &&
            (!SuppressNotifications || (message.Flags & 1 << 12) != 0) &&
            (!SuppressEmbeds || (message.Flags & 1 << 2) != 0) &&
            (!ComponentsV2 || (message.Flags & 1 << 15) != 0) &&
            RichPayloadEquivalent(RichPayload, message);
    }

    private sealed record CreatedChannelExpectation(
        int Type,
        string Name,
        string? ParentId,
        int? Position,
        string? Topic,
        bool? Nsfw,
        int? RateLimitPerUser,
        int? Bitrate,
        int? UserLimit,
        string? RtcRegion,
        int? VideoQualityMode,
        int? DefaultAutoArchiveDuration,
        int? DefaultThreadRateLimitPerUser,
        int? DefaultSortOrder,
        int? DefaultForumLayout,
        ImmutableArray<ChannelOverwriteApiState> PermissionOverwrites,
        bool? Archived,
        bool? Locked,
        bool? Invitable,
        int? AutoArchiveDuration,
        ImmutableArray<string> AppliedTags,
        long? ExpectedFlags = null,
        ImmutableArray<ForumTagExpectation>? AvailableTags = null,
        string? DefaultReactionEmojiId = null,
        string? DefaultReactionEmojiName = null,
        ForumPostMessageExpectation? InitialMessage = null)
    {
        internal bool Matches(ChannelApiState state) =>
            state.Type == Type &&
            state.Name == Name &&
            state.ParentId == ParentId &&
            (!Position.HasValue || state.Position == Position.Value) &&
            (!TopicSpecified() || state.Topic == Topic) &&
            (!Nsfw.HasValue || state.Nsfw == Nsfw) &&
            (!RateLimitPerUser.HasValue || state.RateLimitPerUser == RateLimitPerUser) &&
            (!Bitrate.HasValue || state.Bitrate == Bitrate) &&
            (!UserLimit.HasValue || state.UserLimit == UserLimit) &&
            (RtcRegion is null || state.RtcRegion == RtcRegion) &&
            (!VideoQualityMode.HasValue || state.VideoQualityMode == VideoQualityMode) &&
            (!DefaultAutoArchiveDuration.HasValue || state.DefaultAutoArchiveDuration == DefaultAutoArchiveDuration) &&
            (!DefaultThreadRateLimitPerUser.HasValue || state.DefaultThreadRateLimitPerUser == DefaultThreadRateLimitPerUser) &&
            (!DefaultSortOrder.HasValue || state.DefaultSortOrder == DefaultSortOrder) &&
            (!DefaultForumLayout.HasValue || state.DefaultForumLayout == DefaultForumLayout) &&
            (!ExpectedFlags.HasValue || state.Flags == ExpectedFlags.Value) &&
            (!AvailableTags.HasValue || ForumTagsMatch(AvailableTags.Value, state.AvailableTags)) &&
            (DefaultReactionEmojiId is null || state.DefaultReactionEmojiId == DefaultReactionEmojiId) &&
            (DefaultReactionEmojiName is null || state.DefaultReactionEmojiName == DefaultReactionEmojiName) &&
            (PermissionOverwrites.IsDefault || state.PermissionOverwrites.SequenceEqual(PermissionOverwrites)) &&
            (!Archived.HasValue || state.Archived == Archived) &&
            (!Locked.HasValue || state.Locked == Locked) &&
            (!Invitable.HasValue || state.Invitable == Invitable) &&
            (!AutoArchiveDuration.HasValue || state.AutoArchiveDuration == AutoArchiveDuration) &&
            (AppliedTags.IsDefault || state.AppliedTags.SequenceEqual(AppliedTags, StringComparer.Ordinal));

        private bool TopicSpecified() => Topic is not null;
    }

    private static bool RichPayloadEquivalent(RichPayloadState expected, MessageView message) =>
        expected.IsTts == message.IsTts &&
        expected.StickerIds.SequenceEqual(message.StickerIds.IsDefault ? [] : message.StickerIds, StringComparer.Ordinal) &&
        JsonEqual(expected.EmbedsJson, message.EmbedsJson) &&
        JsonEqual(expected.ComponentsJson, message.ComponentsJson) &&
        JsonEqual(State(expected.SharedClientTheme), State(message.SharedClientTheme));

    private static bool ForumTagsMatch(
        ImmutableArray<ForumTagExpectation> expected,
        ImmutableArray<ForumTagApiState> observed)
    {
        if (expected.Length != observed.Length)
        {
            return false;
        }

        var remaining = observed.ToList();
        foreach (var tag in expected)
        {
            var index = remaining.FindIndex(candidate =>
                (tag.Id is null || candidate.Id == tag.Id) &&
                candidate.Name == tag.Name &&
                candidate.Moderated == tag.Moderated &&
                candidate.EmojiId == tag.EmojiId &&
                candidate.EmojiName == tag.EmojiName);
            if (index < 0)
            {
                return false;
            }

            remaining.RemoveAt(index);
        }

        return remaining.Count == 0;
    }
}
