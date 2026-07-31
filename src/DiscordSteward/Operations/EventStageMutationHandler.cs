using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

internal sealed class EventStageMutationHandler(
    DiscordApiTransport transport,
    IDiscordExtendedReader extendedReader,
    IGuildCatalogReader catalogReader,
    IDiscordMemberReader memberReader,
    IDiscordBotInspector botInspector,
    IAssetVault assetVault,
    StewardRuntimeSettings settings,
    TimeProvider timeProvider) : ICatalogMutationFamilyHandler
{
    private static readonly ImmutableHashSet<string> ToolNames = new[]
    {
        "create_scheduled_event",
        "update_scheduled_event",
        "start_scheduled_event",
        "complete_scheduled_event",
        "cancel_scheduled_event",
        "delete_scheduled_event",
        "start_stage_instance",
        "update_stage_instance",
        "end_stage_instance",
        "set_stage_speaker",
        "request_stage_speaking"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken) => invocation.ToolName switch
        {
            "create_scheduled_event" => await PrepareCreateEventAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_scheduled_event" => await PrepareUpdateEventAsync(invocation, cancellationToken).ConfigureAwait(false),
            "start_scheduled_event" => await PrepareEventTransitionAsync(invocation, 1, 2, cancellationToken).ConfigureAwait(false),
            "complete_scheduled_event" => await PrepareEventTransitionAsync(invocation, 2, 3, cancellationToken).ConfigureAwait(false),
            "cancel_scheduled_event" => await PrepareEventTransitionAsync(invocation, 1, 4, cancellationToken).ConfigureAwait(false),
            "delete_scheduled_event" => await PrepareDeleteEventAsync(invocation, cancellationToken).ConfigureAwait(false),
            "start_stage_instance" => await PrepareStartStageAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_stage_instance" => await PrepareUpdateStageAsync(invocation, cancellationToken).ConfigureAwait(false),
            "end_stage_instance" => await PrepareEndStageAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_stage_speaker" => await PrepareStageSpeakerAsync(invocation, cancellationToken).ConfigureAwait(false),
            "request_stage_speaking" => await PrepareRequestSpeakingAsync(invocation, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response = invocation.ToolName switch
        {
            "create_scheduled_event" => await WriteCreateEventAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_scheduled_event" => await WriteUpdateEventAsync(invocation, cancellationToken).ConfigureAwait(false),
            "start_scheduled_event" => await WriteEventStatusAsync(invocation, 2, cancellationToken).ConfigureAwait(false),
            "complete_scheduled_event" => await WriteEventStatusAsync(invocation, 3, cancellationToken).ConfigureAwait(false),
            "cancel_scheduled_event" => await WriteEventStatusAsync(invocation, 4, cancellationToken).ConfigureAwait(false),
            "delete_scheduled_event" => await transport.SendJsonAsync(
                HttpMethod.Delete,
                EventRoute(invocation.ResourceId),
                body: null,
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false),
            "start_stage_instance" => await WriteStartStageAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_stage_instance" => await WriteUpdateStageAsync(invocation, cancellationToken).ConfigureAwait(false),
            "end_stage_instance" => await transport.SendJsonAsync(
                HttpMethod.Delete,
                StageRoute(invocation.ResourceId),
                body: null,
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false),
            "set_stage_speaker" => await WriteStageSpeakerAsync(invocation, cancellationToken).ConfigureAwait(false),
            "request_stage_speaking" => await WriteRequestSpeakingAsync(invocation, cancellationToken).ConfigureAwait(false),
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
        if (invocation.ToolName == "create_scheduled_event")
        {
            var id = CreatedId(priorReceiptJson);
            var observed = await TryGetEventStateAsync(id, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new ResourceExistenceState(id, Exists: false)));
            }

            var desired = DeserializeState<EventCreationExpectation>(desiredStateJson);
            return new CatalogMutationObservation(
                desired.Matches(observed)
                    ? CatalogMutationObservationKind.DesiredState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (invocation.ToolName is "update_scheduled_event" or "start_scheduled_event" or
            "complete_scheduled_event" or "cancel_scheduled_event")
        {
            var observed = await TryGetEventStateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new ResourceExistenceState(invocation.ResourceId, Exists: false)));
            }

            var desired = DeserializeState<EventApiState>(desiredStateJson);
            var before = DeserializeState<EventApiState>(beforeStateJson);
            return new CatalogMutationObservation(
                observed.MatchesState(desired)
                    ? CatalogMutationObservationKind.DesiredState
                    : observed.MatchesState(before)
                        ? CatalogMutationObservationKind.BeforeState
                        : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (invocation.ToolName == "delete_scheduled_event")
        {
            var observed = await TryGetEventStateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.DesiredState,
                    State(new ResourceExistenceState(invocation.ResourceId, Exists: false)));
            }

            return Observation(observed, beforeStateJson, desiredStateJson);
        }

        if (invocation.ToolName == "start_stage_instance")
        {
            var observed = await TryGetStageAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new ResourceExistenceState(invocation.ResourceId, Exists: false)));
            }

            var desired = DeserializeState<StageCreationExpectation>(desiredStateJson);
            return new CatalogMutationObservation(
                desired.Matches(observed)
                    ? CatalogMutationObservationKind.DesiredState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (invocation.ToolName == "update_stage_instance")
        {
            var observed = await TryGetStageAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            return observed is null
                ? new CatalogMutationObservation(CatalogMutationObservationKind.UnexpectedState, State(new ResourceExistenceState(invocation.ResourceId, false)))
                : Observation(observed, beforeStateJson, desiredStateJson);
        }

        if (invocation.ToolName == "end_stage_instance")
        {
            var observed = await TryGetStageAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.DesiredState,
                    State(new ResourceExistenceState(invocation.ResourceId, Exists: false)));
            }

            return Observation(observed, beforeStateJson, desiredStateJson);
        }

        if (invocation.ToolName == "set_stage_speaker")
        {
            var input = Arguments<SetStageSpeakerInput>(invocation);
            var member = await memberReader.GetMemberAsync(input.UserId, cancellationToken).ConfigureAwait(false);
            return Observation(StageVoiceState.FromMember(member), beforeStateJson, desiredStateJson);
        }

        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var ownMember = await memberReader.GetMemberAsync(bot.BotUserId, cancellationToken).ConfigureAwait(false);
        var observedOwn = StageVoiceState.FromMember(ownMember);
        var desiredOwn = DeserializeState<StageVoiceState>(desiredStateJson);
        var desiredMatches = observedOwn.ChannelId == desiredOwn.ChannelId &&
            observedOwn.RequestToSpeakTimestamp.HasValue == desiredOwn.RequestToSpeakTimestamp.HasValue;
        return new CatalogMutationObservation(
            desiredMatches ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(State(observedOwn), beforeStateJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            State(observedOwn));
    }

    private async Task<CatalogMutationPreparation> PrepareCreateEventAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateScheduledEventInput>(invocation);
        var events = await extendedReader.ListScheduledEventsAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedEventsStateDigest, ResourceStateCanonicalizer.ComputeObjectDigest(events));
        var normalized = await NormalizeCreateEventAsync(input, cancellationToken).ConfigureAwait(false);
        return new CatalogMutationPreparation(
            State(new EventCollectionState(ResourceStateCanonicalizer.ComputeObjectDigest(events))),
            State(new EventCreationExpectation(normalized)));
    }

    private async Task<CatalogMutationPreparation> PrepareUpdateEventAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateScheduledEventInput>(invocation);
        var read = await extendedReader.GetScheduledEventAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, read.StateDigest);
        var current = await GetEventStateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (current.Status is 3 or 4)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Terminal scheduled events cannot be updated.");
        }

        var clear = NormalizeClear(input.Clear, ["channel", "description", "end_time", "location", "cover", "recurrence"]);
        if (input.Set.ChannelId is not null && clear.Contains("channel"))
        {
            throw InvalidArgument("Scheduled event channel cannot be set and cleared.");
        }
        await ValidateCoverAssetAsync(input.Set.CoverAssetId, cancellationToken).ConfigureAwait(false);
        var desired = current with
        {
            ChannelId = clear.Contains("channel") ? null : input.Set.ChannelId is null ? current.ChannelId : NormalizeSnowflake(input.Set.ChannelId),
            Name = input.Set.Name is null ? current.Name : NormalizeText(input.Set.Name, 100, false),
            Description = clear.Contains("description") ? null : input.Set.Description is null ? current.Description : NormalizeText(input.Set.Description, 1_000, true, allowFormatting: true),
            StartTime = input.Set.StartTime ?? current.StartTime,
            EndTime = clear.Contains("end_time") ? null : input.Set.EndTime ?? current.EndTime,
            EntityType = input.Set.EntityType is null ? current.EntityType : NormalizeEntityType(input.Set.EntityType),
            Location = clear.Contains("location") ? null : input.Set.Location is null ? current.Location : NormalizeText(input.Set.Location, 100, false),
            CoverImageHash = clear.Contains("cover") ? null : input.Set.CoverAssetId is null ? current.CoverImageHash : "__present__",
            Recurrence = clear.Contains("recurrence") ? null : input.Set.Recurrence is null ? current.Recurrence : NormalizeRecurrence(input.Set.Recurrence)
        };
        ValidateEventShape(desired);
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareEventTransitionAsync(
        CatalogMutationInvocation invocation,
        int requiredStatus,
        int desiredStatus,
        CancellationToken cancellationToken)
    {
        var input = Arguments<TransitionScheduledEventInput>(invocation);
        var read = await extendedReader.GetScheduledEventAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, read.StateDigest);
        var current = await GetEventStateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (current.Status != requiredStatus)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Scheduled event is not in the required source state.");
        }

        return new CatalogMutationPreparation(State(current), State(current with { Status = desiredStatus }));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteEventAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteScheduledEventInput>(invocation);
        var read = await extendedReader.GetScheduledEventAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, read.StateDigest);
        var current = await GetEventStateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        return new CatalogMutationPreparation(State(current), State(new ResourceExistenceState(current.Id, Exists: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareStartStageAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<StartStageInstanceInput>(invocation);
        var channel = ChannelApiState.Parse(await transport.GetJsonAsync($"channels/{NormalizeSnowflake(invocation.ResourceId)}", cancellationToken).ConfigureAwait(false));
        if (channel.GuildId != GuildId() || channel.Type != 13)
        {
            throw InvalidArgument("Stage start requires a stage channel in the configured guild.");
        }

        EnsureDigest(input.ExpectedChannelStateDigest, channel.ComputeDigest());
        if (input.SendStartNotification)
        {
            var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
            if (!bot.EffectivePermissions.Contains("MentionEveryone", StringComparer.Ordinal))
            {
                throw new StewardException(
                    StewardErrorCodes.DiscordPermissionDenied,
                    "Stage start notification requires Mention Everyone; no write was attempted.");
            }
        }

        if (await TryGetStageAsync(channel.Id, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The stage channel is already live.");
        }

        var scheduledEventId = input.ScheduledEventId is null ? null : NormalizeSnowflake(input.ScheduledEventId);
        return new CatalogMutationPreparation(
            State(channel),
            State(new StageCreationExpectation(
                channel.Id,
                NormalizeText(input.Topic, 120, false),
                "GuildOnly",
                scheduledEventId)));
    }

    private async Task<CatalogMutationPreparation> PrepareUpdateStageAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateStageInstanceInput>(invocation);
        var current = await catalogReader.GetStageInstanceAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        var privacy = NormalizePrivacy(input.PrivacyLevel);
        var desiredBase = current with { PrivacyLevel = privacy, StateDigest = string.Empty };
        var desired = desiredBase with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(desiredBase) };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareEndStageAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<EndStageInstanceInput>(invocation);
        var current = await catalogReader.GetStageInstanceAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        return new CatalogMutationPreparation(State(current), State(new ResourceExistenceState(current.ChannelId, Exists: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareStageSpeakerAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetStageSpeakerInput>(invocation);
        var stage = await catalogReader.GetStageInstanceAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStageStateDigest, stage.StateDigest);
        var member = await memberReader.GetMemberAsync(input.UserId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        if (member.VoiceState.ChannelId != stage.ChannelId)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The member is not connected to the exact stage channel.");
        }

        var before = StageVoiceState.FromMember(member);
        return new CatalogMutationPreparation(
            State(before),
            State(before with { Suppressed = !input.IsSpeaker, RequestToSpeakTimestamp = null }));
    }

    private async Task<CatalogMutationPreparation> PrepareRequestSpeakingAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<RequestStageSpeakingInput>(invocation);
        var stage = await catalogReader.GetStageInstanceAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStageStateDigest, stage.StateDigest);
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var member = await memberReader.GetMemberAsync(bot.BotUserId, cancellationToken).ConfigureAwait(false);
        if (member.VoiceState.ChannelId != stage.ChannelId)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Steward is not connected to the exact stage channel.");
        }

        var before = StageVoiceState.FromMember(member);
        return new CatalogMutationPreparation(
            State(before),
            State(before with
            {
                RequestToSpeakTimestamp = input.Requesting ? timeProvider.GetUtcNow() : null
            }));
    }

    private async Task<JsonElement?> WriteCreateEventAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = await NormalizeCreateEventAsync(Arguments<CreateScheduledEventInput>(invocation), cancellationToken).ConfigureAwait(false);
        var body = EventPayload(input);
        await AddCoverAsync(body, input.CoverAssetId, cancellationToken).ConfigureAwait(false);
        return await transport.SendJsonAsync(
            HttpMethod.Post,
            $"guilds/{GuildId()}/scheduled-events",
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> WriteUpdateEventAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateScheduledEventInput>(invocation);
        var clear = input.Clear.ToImmutableHashSet(StringComparer.Ordinal);
        var body = new Dictionary<string, object?>();
        if (input.Set.ChannelId is not null) body["channel_id"] = NormalizeSnowflake(input.Set.ChannelId);
        if (input.Set.Name is not null) body["name"] = NormalizeText(input.Set.Name, 100, false);
        if (input.Set.Description is not null) body["description"] = NormalizeText(input.Set.Description, 1_000, true, allowFormatting: true);
        if (input.Set.StartTime.HasValue) body["scheduled_start_time"] = input.Set.StartTime.Value;
        if (input.Set.EndTime.HasValue) body["scheduled_end_time"] = input.Set.EndTime.Value;
        if (input.Set.EntityType is not null) body["entity_type"] = NormalizeEntityType(input.Set.EntityType);
        if (input.Set.Location is not null) body["entity_metadata"] = new Dictionary<string, object?> { ["location"] = input.Set.Location };
        if (input.Set.Recurrence is not null) body["recurrence_rule"] = RecurrencePayload(NormalizeRecurrence(input.Set.Recurrence));
        if (clear.Contains("description")) body["description"] = null;
        if (clear.Contains("channel")) body["channel_id"] = null;
        if (clear.Contains("end_time")) body["scheduled_end_time"] = null;
        if (clear.Contains("location")) body["entity_metadata"] = null;
        if (clear.Contains("cover")) body["image"] = null;
        if (clear.Contains("recurrence")) body["recurrence_rule"] = null;
        await AddCoverAsync(body, input.Set.CoverAssetId, cancellationToken).ConfigureAwait(false);
        return await transport.SendJsonAsync(
            HttpMethod.Patch,
            EventRoute(invocation.ResourceId),
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonElement?> WriteEventStatusAsync(CatalogMutationInvocation invocation, int status, CancellationToken cancellationToken) =>
        transport.SendJsonAsync(
            HttpMethod.Patch,
            EventRoute(invocation.ResourceId),
            JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["status"] = status }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);

    private Task<JsonElement?> WriteStartStageAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<StartStageInstanceInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Post,
            "stage-instances",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["channel_id"] = NormalizeSnowflake(invocation.ResourceId),
                ["topic"] = NormalizeText(input.Topic, 120, false),
                ["privacy_level"] = 2,
                ["send_start_notification"] = input.SendStartNotification,
                ["guild_scheduled_event_id"] = input.ScheduledEventId is null ? null : NormalizeSnowflake(input.ScheduledEventId)
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteUpdateStageAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateStageInstanceInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            StageRoute(invocation.ResourceId),
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["privacy_level"] = NormalizePrivacy(input.PrivacyLevel) == "GuildOnly" ? 2 : 1
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteStageSpeakerAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<SetStageSpeakerInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/voice-states/{NormalizeSnowflake(input.UserId)}",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["channel_id"] = NormalizeSnowflake(invocation.ResourceId),
                ["suppress"] = !input.IsSpeaker
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteRequestSpeakingAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<RequestStageSpeakingInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/voice-states/@me",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["channel_id"] = NormalizeSnowflake(invocation.ResourceId),
                ["request_to_speak_timestamp"] = input.Requesting ? timeProvider.GetUtcNow() : null
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private async Task<CreateScheduledEventInput> NormalizeCreateEventAsync(
        CreateScheduledEventInput input,
        CancellationToken cancellationToken)
    {
        var entityType = NormalizeEntityTypeName(input.EntityType);
        await ValidateCoverAssetAsync(input.CoverAssetId, cancellationToken).ConfigureAwait(false);
        var normalized = input with
        {
            ChannelId = input.ChannelId is null ? null : NormalizeSnowflake(input.ChannelId),
            Name = NormalizeText(input.Name, 100, false),
            Description = input.Description is null ? null : NormalizeText(input.Description, 1_000, true, allowFormatting: true),
            EntityType = entityType,
            Location = input.Location is null ? null : NormalizeText(input.Location, 100, false),
            Recurrence = input.Recurrence is null ? null : ToInput(NormalizeRecurrence(input.Recurrence))
        };
        var state = EventApiState.FromCreate(normalized);
        ValidateEventShape(state);
        if (normalized.StartTime <= timeProvider.GetUtcNow()) throw InvalidArgument("Scheduled event start must be in the future.");
        return normalized;
    }

    private static void ValidateEventShape(EventApiState state)
    {
        var valid = state.EntityType switch
        {
            1 or 2 => state.ChannelId is not null && state.Location is null,
            3 => state.ChannelId is null && state.Location is not null && state.EndTime.HasValue,
            _ => false
        };
        if (!valid || state.EndTime.HasValue && state.EndTime <= state.StartTime)
        {
            throw InvalidArgument("Scheduled event channel/location/time fields do not match its entity type.");
        }
    }

    private static EventRecurrenceState NormalizeRecurrence(ScheduledEventRecurrenceInput input)
    {
        var frequency = input.Frequency.Trim().ToLowerInvariant() switch
        {
            "yearly" => 0,
            "monthly" => 1,
            "weekly" => 2,
            "daily" => 3,
            _ => throw InvalidArgument("Recurrence frequency is invalid.")
        };
        if (input.Interval is < 1 or > 2 || input.Interval == 2 && frequency != 2 ||
            input.ByWeekday.Any(day => day is < 0 or > 6) ||
            input.ByNWeekday.Any(item => item.Week is < 1 or > 5 || item.Day is < 0 or > 6) ||
            input.ByMonth.Any(month => month is < 1 or > 12) ||
            input.ByMonthDay.Any(day => day is < 1 or > 31))
        {
            throw InvalidArgument("Recurrence fields are outside Discord's documented limits.");
        }

        if (!input.ByWeekday.IsEmpty && !input.ByNWeekday.IsEmpty ||
            !input.ByWeekday.IsEmpty && (!input.ByMonth.IsEmpty || !input.ByMonthDay.IsEmpty) ||
            !input.ByNWeekday.IsEmpty && (!input.ByMonth.IsEmpty || !input.ByMonthDay.IsEmpty))
        {
            throw InvalidArgument("Recurrence selectors use mutually exclusive modes.");
        }

        return new EventRecurrenceState(
            input.Start,
            frequency,
            input.Interval,
            input.ByWeekday.Distinct().Order().ToImmutableArray(),
            input.ByNWeekday.Select(item => new EventNWeekdayState(item.Week, item.Day)).ToImmutableArray(),
            input.ByMonth.Distinct().Order().ToImmutableArray(),
            input.ByMonthDay.Distinct().Order().ToImmutableArray());
    }

    private static ScheduledEventRecurrenceInput ToInput(EventRecurrenceState state) => new(
        state.Start,
        state.Frequency switch { 0 => "yearly", 1 => "monthly", 2 => "weekly", _ => "daily" },
        state.Interval,
        state.ByWeekday,
        state.ByNWeekday.Select(item => new RecurrenceNWeekdayInput(item.Week, item.Day)).ToImmutableArray(),
        state.ByMonth,
        state.ByMonthDay);

    private static Dictionary<string, object?> EventPayload(CreateScheduledEventInput input) => new()
    {
        ["channel_id"] = input.ChannelId,
        ["entity_metadata"] = input.Location is null ? null : new Dictionary<string, object?> { ["location"] = input.Location },
        ["name"] = input.Name,
        ["privacy_level"] = 2,
        ["scheduled_start_time"] = input.StartTime,
        ["scheduled_end_time"] = input.EndTime,
        ["description"] = input.Description,
        ["entity_type"] = NormalizeEntityType(input.EntityType),
        ["recurrence_rule"] = input.Recurrence is null ? null : RecurrencePayload(NormalizeRecurrence(input.Recurrence))
    };

    private static Dictionary<string, object?> RecurrencePayload(EventRecurrenceState state) => new()
    {
        ["start"] = state.Start,
        ["frequency"] = state.Frequency,
        ["interval"] = state.Interval,
        ["by_weekday"] = state.ByWeekday,
        ["by_n_weekday"] = state.ByNWeekday.Select(item => new { n = item.Week, day = item.Day }).ToArray(),
        ["by_month"] = state.ByMonth,
        ["by_month_day"] = state.ByMonthDay
    };

    private async Task AddCoverAsync(Dictionary<string, object?> body, string? assetId, CancellationToken cancellationToken)
    {
        if (assetId is null) return;
        var asset = await assetVault.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        var path = await assetVault.GetContentPathAsync(assetId, cancellationToken).ConfigureAwait(false);
        body["image"] = $"data:{asset.MediaType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false))}";
    }

    private async Task ValidateCoverAssetAsync(string? assetId, CancellationToken cancellationToken)
    {
        if (assetId is null) return;
        var asset = await assetVault.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (!asset.MediaType.StartsWith("image/", StringComparison.Ordinal) || asset.ByteLength > 10 * 1024 * 1024)
            throw InvalidArgument("Scheduled event cover must be a bounded image asset.");
    }

    private async Task<EventApiState> GetEventStateAsync(string eventId, CancellationToken cancellationToken) =>
        EventApiState.Parse(await transport.GetJsonAsync(EventRoute(eventId), cancellationToken).ConfigureAwait(false));

    private async Task<EventApiState?> TryGetEventStateAsync(string eventId, CancellationToken cancellationToken)
    {
        var root = await transport.TryGetJsonAsync(EventRoute(eventId), cancellationToken).ConfigureAwait(false);
        return root.HasValue ? EventApiState.Parse(root.Value) : null;
    }

    private async Task<StageInstanceView?> TryGetStageAsync(string channelId, CancellationToken cancellationToken)
    {
        try { return await catalogReader.GetStageInstanceAsync(channelId, cancellationToken).ConfigureAwait(false); }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound) { return null; }
    }

    private string EventRoute(string id) => $"guilds/{GuildId()}/scheduled-events/{NormalizeSnowflake(id)}";
    private static string StageRoute(string id) => $"stage-instances/{NormalizeSnowflake(id)}";
    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);
    private static string NormalizeSnowflake(string value) => ChannelApiState.NormalizeSnowflake(value);

    private static int NormalizeEntityType(string value) => NormalizeEntityTypeName(value) switch
    {
        "StageInstance" => 1,
        "Voice" => 2,
        _ => 3
    };

    private static string NormalizeEntityTypeName(string value) => value.Trim().ToLowerInvariant() switch
    {
        "stage" or "stageinstance" or "stage_instance" => "StageInstance",
        "voice" => "Voice",
        "external" => "External",
        _ => throw InvalidArgument("Scheduled event entity type is invalid.")
    };

    private static string NormalizePrivacy(string value) => value.Trim().ToLowerInvariant() switch
    {
        "guildonly" or "guild_only" => "GuildOnly",
        "public" => "Public",
        _ => throw InvalidArgument("Stage privacy level is invalid.")
    };

    private static ImmutableHashSet<string> NormalizeClear(ImmutableArray<string> clear, IEnumerable<string> allowed)
    {
        if (clear.IsDefault) return ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
        var result = clear.Select(value => value.Trim().ToLowerInvariant()).ToImmutableHashSet(StringComparer.Ordinal);
        return result.IsSubsetOf(allowed) ? result : throw InvalidArgument("Scheduled event clear list is invalid.");
    }

    internal static string NormalizeText(string value, int maximum, bool allowEmpty, bool allowFormatting = false)
    {
        var text = value ?? string.Empty;
        var hasInvalidControl = text.Any(character =>
            char.IsControl(character) &&
            (!allowFormatting || character is not '\n' and not '\r' and not '\t'));
        return (allowEmpty || !string.IsNullOrWhiteSpace(text)) && text.Length <= maximum && !hasInvalidControl
            ? text
            : throw InvalidArgument("Event or stage text is empty, too long, or invalid.");
    }

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

    private static string CreatedId(string? receipt)
    {
        if (receipt is null) throw InvalidResponse();
        using var document = JsonDocument.Parse(receipt);
        return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(id.GetString() ?? string.Empty)
            : throw InvalidResponse();
    }

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options) ?? throw InvalidArgument("Mutation arguments are invalid.");
    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options) ?? throw InvalidResponse();
    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static void EnsureDigest(string? supplied, string observed)
    {
        var value = supplied?.Trim().ToLowerInvariant();
        if (value is not { Length: 64 } || !value.All(char.IsAsciiHexDigit)) throw InvalidArgument("Expected digest is invalid.");
        if (value != observed) throw new StewardException(StewardErrorCodes.StateConflict, "Event or stage state changed after approval.");
    }

    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);
    private static StewardException InvalidResponse() => new(StewardErrorCodes.DiscordUnavailable, "Discord returned invalid event or stage state.");
    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "Event or stage mutation is not supported.");

    private sealed record EventCollectionState(string StateDigest);
    private sealed record ResourceExistenceState(string ResourceId, bool Exists);
    private sealed record EventCreationExpectation(CreateScheduledEventInput Input)
    {
        internal bool Matches(EventApiState state) => EventApiState.FromCreate(Input).MatchesCreate(state);
    }

    private sealed record StageCreationExpectation(
        string ChannelId,
        string Topic,
        string PrivacyLevel,
        string? ScheduledEventId)
    {
        internal bool Matches(StageInstanceView observed) =>
            observed.ChannelId == ChannelId && observed.Topic == Topic &&
            observed.PrivacyLevel.Equals(PrivacyLevel, StringComparison.OrdinalIgnoreCase) &&
            observed.ScheduledEventId == ScheduledEventId;
    }

    private sealed record StageVoiceState(
        string UserId,
        string? ChannelId,
        bool Suppressed,
        DateTimeOffset? RequestToSpeakTimestamp)
    {
        internal static StageVoiceState FromMember(MemberView member) => new(
            member.UserId,
            member.VoiceState.ChannelId,
            member.VoiceState.IsSuppressed,
            member.VoiceState.RequestToSpeakTimestamp);
    }
}

internal sealed record EventNWeekdayState(int Week, int Day);

internal sealed record EventRecurrenceState(
    DateTimeOffset Start,
    int Frequency,
    int Interval,
    ImmutableArray<int> ByWeekday,
    ImmutableArray<EventNWeekdayState> ByNWeekday,
    ImmutableArray<int> ByMonth,
    ImmutableArray<int> ByMonthDay);

internal sealed record EventApiState(
    string Id,
    string GuildId,
    string? ChannelId,
    string Name,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    int Status,
    int EntityType,
    string? Location,
    string? CoverImageHash,
    EventRecurrenceState? Recurrence)
{
    internal static EventApiState Parse(JsonElement root) => new(
        Snowflake(root, "id"),
        Snowflake(root, "guild_id"),
        OptionalSnowflake(root, "channel_id"),
        Text(root, "name", 100) ?? string.Empty,
        Text(root, "description", 1_000),
        Timestamp(root, "scheduled_start_time") ?? throw Invalid(),
        Timestamp(root, "scheduled_end_time"),
        Number(root, "status") ?? throw Invalid(),
        Number(root, "entity_type") ?? throw Invalid(),
        root.TryGetProperty("entity_metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
            ? Text(metadata, "location", 100)
            : null,
        Text(root, "image", 256),
        root.TryGetProperty("recurrence_rule", out var recurrence) && recurrence.ValueKind == JsonValueKind.Object
            ? ParseRecurrence(recurrence)
            : null);

    internal static EventApiState FromCreate(CreateScheduledEventInput input) => new(
        Id: string.Empty,
        GuildId: string.Empty,
        input.ChannelId,
        input.Name,
        input.Description,
        input.StartTime,
        input.EndTime,
        Status: 1,
        EntityType: input.EntityType switch { "StageInstance" => 1, "Voice" => 2, _ => 3 },
        input.Location,
        input.CoverAssetId is null ? null : "__present__",
        input.Recurrence is null ? null : new EventRecurrenceState(
            input.Recurrence.Start,
            input.Recurrence.Frequency switch { "yearly" => 0, "monthly" => 1, "weekly" => 2, _ => 3 },
            input.Recurrence.Interval,
            input.Recurrence.ByWeekday,
            input.Recurrence.ByNWeekday.Select(item => new EventNWeekdayState(item.Week, item.Day)).ToImmutableArray(),
            input.Recurrence.ByMonth,
            input.Recurrence.ByMonthDay));

    internal bool MatchesCreate(EventApiState actual) =>
        ChannelId == actual.ChannelId && Name == actual.Name && Description == actual.Description &&
        StartTime == actual.StartTime && EndTime == actual.EndTime && EntityType == actual.EntityType &&
        Location == actual.Location && (CoverImageHash != "__present__" || actual.CoverImageHash is not null) &&
        RecurrenceMatches(Recurrence, actual.Recurrence);

    internal bool MatchesState(EventApiState desired) =>
        Id == desired.Id && GuildId == desired.GuildId && ChannelId == desired.ChannelId &&
        Name == desired.Name && Description == desired.Description && StartTime == desired.StartTime &&
        EndTime == desired.EndTime && Status == desired.Status && EntityType == desired.EntityType &&
        Location == desired.Location &&
        (desired.CoverImageHash == "__present__" ? CoverImageHash is not null : CoverImageHash == desired.CoverImageHash) &&
        RecurrenceMatches(Recurrence, desired.Recurrence);

    private static bool RecurrenceMatches(EventRecurrenceState? left, EventRecurrenceState? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Start == right.Start && left.Frequency == right.Frequency && left.Interval == right.Interval &&
            left.ByWeekday.SequenceEqual(right.ByWeekday) &&
            left.ByNWeekday.SequenceEqual(right.ByNWeekday) &&
            left.ByMonth.SequenceEqual(right.ByMonth) &&
            left.ByMonthDay.SequenceEqual(right.ByMonthDay);
    }

    private static EventRecurrenceState ParseRecurrence(JsonElement root) => new(
        Timestamp(root, "start") ?? throw Invalid(),
        Number(root, "frequency") ?? throw Invalid(),
        Number(root, "interval") ?? 1,
        IntArray(root, "by_weekday"),
        root.TryGetProperty("by_n_weekday", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(item => new EventNWeekdayState(
                Number(item, "n") ?? throw Invalid(),
                Number(item, "day") ?? throw Invalid())).ToImmutableArray()
            : [],
        IntArray(root, "by_month"),
        IntArray(root, "by_month_day"));

    private static ImmutableArray<int> IntArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(item => item.GetInt32()).ToImmutableArray()
            : [];

    private static string Snowflake(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? ChannelApiState.NormalizeSnowflake(value.GetString() ?? string.Empty)
            : throw Invalid();

    private static string? OptionalSnowflake(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null :
            value.ValueKind == JsonValueKind.String ? ChannelApiState.NormalizeSnowflake(value.GetString() ?? string.Empty) : throw Invalid();

    private static string? Text(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw Invalid();
        var text = value.GetString() ?? string.Empty;
        return text.Length <= maximum ? text : text[..maximum];
    }

    private static int? Number(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null :
            value.TryGetInt32(out var parsed) ? parsed : throw Invalid();

    private static DateTimeOffset? Timestamp(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null :
            value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var parsed) ? parsed : throw Invalid();

    private static StewardException Invalid() => new(StewardErrorCodes.DiscordUnavailable, "Discord returned invalid scheduled event state.");
}
