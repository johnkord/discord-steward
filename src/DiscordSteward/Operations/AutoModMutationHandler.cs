using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

internal sealed class AutoModMutationHandler(
    DiscordApiTransport transport,
    IDiscordExtendedReader reader,
    StewardRuntimeSettings settings) : ICatalogMutationFamilyHandler
{
    private static readonly ImmutableHashSet<string> ToolNames = new[]
    {
        "create_automod_rule",
        "update_automod_rule",
        "set_automod_rule_enabled",
        "delete_automod_rule"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.ToolName == "create_automod_rule")
        {
            var input = Arguments<CreateAutoModRuleInput>(invocation);
            var rules = await reader.ListAutoModRulesAsync(cancellationToken).ConfigureAwait(false);
            EnsureDigest(input.ExpectedRulesStateDigest, ResourceStateCanonicalizer.ComputeObjectDigest(rules));
            var config = NormalizeConfiguration(input.Configuration, NormalizeTriggerType(input.TriggerType));
            return new CatalogMutationPreparation(
                State(new AutoModCollectionState(ResourceStateCanonicalizer.ComputeObjectDigest(rules))),
                State(new AutoModCreationExpectation(NormalizeTriggerType(input.TriggerType), config)));
        }

        var current = await reader.GetAutoModRuleAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (current.GuildId != GuildId())
        {
            throw new StewardException(StewardErrorCodes.GuildMismatch, "The AutoMod rule belongs to another guild.");
        }

        return invocation.ToolName switch
        {
            "update_automod_rule" => PrepareUpdate(invocation, current),
            "set_automod_rule_enabled" => PrepareEnabled(invocation, current),
            "delete_automod_rule" => PrepareDelete(invocation, current),
            _ => throw Unsupported()
        };
    }

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response = invocation.ToolName switch
        {
            "create_automod_rule" => await WriteCreateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_automod_rule" => await WriteUpdateAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
            "set_automod_rule_enabled" => await WriteEnabledAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_automod_rule" => await transport.SendJsonAsync(
                HttpMethod.Delete,
                RuleRoute(invocation.ResourceId),
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
        if (invocation.ToolName == "create_automod_rule")
        {
            var id = CreatedId(priorReceiptJson);
            var observed = await TryGetRuleAsync(id, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new AutoModExistenceState(id, Exists: false)));
            }

            var desired = DeserializeState<AutoModCreationExpectation>(desiredStateJson);
            return new CatalogMutationObservation(
                desired.Matches(observed)
                    ? CatalogMutationObservationKind.DesiredState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        var current = await TryGetRuleAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (invocation.ToolName == "delete_automod_rule")
        {
            if (current is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.DesiredState,
                    State(new AutoModExistenceState(invocation.ResourceId, Exists: false)));
            }

            var before = DeserializeState<AutoModRuleView>(beforeStateJson);
            return new CatalogMutationObservation(
                current == before
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(current));
        }

        if (current is null)
        {
            return new CatalogMutationObservation(
                CatalogMutationObservationKind.UnexpectedState,
                State(new AutoModExistenceState(invocation.ResourceId, Exists: false)));
        }

        return Observation(current, beforeStateJson, desiredStateJson);
    }

    private static CatalogMutationPreparation PrepareUpdate(
        CatalogMutationInvocation invocation,
        AutoModRuleView current)
    {
        var input = Arguments<UpdateAutoModRuleInput>(invocation);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        var desired = ToView(
            current.Id,
            current.GuildId,
            current.TriggerType,
            NormalizeConfiguration(input.Configuration, current.TriggerType));
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private static CatalogMutationPreparation PrepareEnabled(
        CatalogMutationInvocation invocation,
        AutoModRuleView current)
    {
        var input = Arguments<SetAutoModRuleEnabledInput>(invocation);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        var desiredBase = current with { Enabled = input.Enabled, StateDigest = string.Empty };
        var desired = desiredBase with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(desiredBase) };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private static CatalogMutationPreparation PrepareDelete(
        CatalogMutationInvocation invocation,
        AutoModRuleView current)
    {
        var input = Arguments<DeleteAutoModRuleInput>(invocation);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        return new CatalogMutationPreparation(
            State(current),
            State(new AutoModExistenceState(current.Id, Exists: false)));
    }

    private Task<JsonElement?> WriteCreateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateAutoModRuleInput>(invocation);
        var trigger = NormalizeTriggerType(input.TriggerType);
        var config = NormalizeConfiguration(input.Configuration, trigger);
        var payload = ConfigurationPayload(config, trigger, includeTrigger: true);
        return transport.SendJsonAsync(
            HttpMethod.Post,
            $"guilds/{GuildId()}/auto-moderation/rules",
            JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteUpdateAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        _ = Arguments<UpdateAutoModRuleInput>(invocation);
        var desired = DeserializeState<AutoModRuleView>(preparation.DesiredStateJson);
        var configuration = new AutoModRuleConfigurationInput(
            desired.Name,
            desired.EventType,
            desired.KeywordFilter,
            desired.RegexPatterns,
            desired.AllowList,
            desired.Presets,
            desired.MentionTotalLimit,
            desired.MentionRaidProtectionEnabled,
            desired.Actions.Select(action => new AutoModActionInput(
                action.Type,
                action.ChannelId,
                action.CustomMessage,
                action.TimeoutSeconds)).ToImmutableArray(),
            desired.Enabled,
            desired.ExemptRoleIds,
            desired.ExemptChannelIds);
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            RuleRoute(invocation.ResourceId),
            JsonSerializer.SerializeToElement(
                ConfigurationPayload(configuration, desired.TriggerType, includeTrigger: false),
                CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteEnabledAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetAutoModRuleEnabledInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            RuleRoute(invocation.ResourceId),
            JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["enabled"] = input.Enabled }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private async Task<AutoModRuleView?> TryGetRuleAsync(
        string ruleId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.GetAutoModRuleAsync(NormalizeSnowflake(ruleId), cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
            return null;
        }
    }

    private static AutoModRuleConfigurationInput NormalizeConfiguration(
        AutoModRuleConfigurationInput input,
        string triggerType)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = NormalizeText(input.Name, 100, allowEmpty: false);
        var eventType = NormalizeEventType(input.EventType);
        if (triggerType == "MemberProfile" && eventType != "MemberUpdate" ||
            triggerType != "MemberProfile" && eventType != "MessageSend")
        {
            throw InvalidArgument("AutoMod event type does not match the selected trigger type.");
        }
        var keywords = NormalizeTexts(input.KeywordFilter, 1_000, 60);
        var regex = NormalizeTexts(input.RegexPatterns, 10, 260);
        var allowMaximum = triggerType == "KeywordPreset" ? 1_000 : 100;
        var allow = NormalizeTexts(input.AllowList, allowMaximum, 60);
        var presets = input.Presets.Select(NormalizePreset).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        if (presets.Length > 3)
        {
            throw InvalidArgument("AutoMod presets cannot exceed three documented values.");
        }

        if (input.MentionTotalLimit is < 1 or > 50)
        {
            throw InvalidArgument("AutoMod mention limit must be between 1 and 50.");
        }

        ValidateTriggerMetadata(triggerType, keywords, regex, allow, presets, input.MentionTotalLimit, input.MentionRaidProtectionEnabled);
        var actions = input.Actions.Select(NormalizeAction).ToImmutableArray();
        if (actions.IsEmpty || actions.Length > 3)
        {
            throw InvalidArgument("AutoMod rules require 1 to 3 explicit actions.");
        }

        if (actions.Any(action => action.Type == "Timeout") && triggerType is not "Keyword" and not "MentionSpam")
        {
            throw InvalidArgument("Timeout actions are valid only for keyword and mention-spam rules.");
        }

        var roles = NormalizeIds(input.ExemptRoleIds, 20);
        var channels = NormalizeIds(input.ExemptChannelIds, 50);
        return new AutoModRuleConfigurationInput(
            name,
            eventType,
            keywords,
            regex,
            allow,
            presets,
            input.MentionTotalLimit,
            input.MentionRaidProtectionEnabled,
            actions,
            input.Enabled,
            roles,
            channels);
    }

    private static void ValidateTriggerMetadata(
        string trigger,
        ImmutableArray<string> keywords,
        ImmutableArray<string> regex,
        ImmutableArray<string> allow,
        ImmutableArray<string> presets,
        int? mentionLimit,
        bool? mentionRaid)
    {
        var valid = trigger switch
        {
            "Keyword" or "MemberProfile" => presets.IsEmpty && !mentionLimit.HasValue && !mentionRaid.HasValue,
            "Spam" => keywords.IsEmpty && regex.IsEmpty && allow.IsEmpty && presets.IsEmpty && !mentionLimit.HasValue && !mentionRaid.HasValue,
            "KeywordPreset" => keywords.IsEmpty && regex.IsEmpty && !presets.IsEmpty && !mentionLimit.HasValue && !mentionRaid.HasValue,
            "MentionSpam" => keywords.IsEmpty && regex.IsEmpty && allow.IsEmpty && presets.IsEmpty && mentionLimit.HasValue,
            _ => false
        };
        if (!valid)
        {
            throw InvalidArgument("AutoMod trigger metadata does not match the selected immutable trigger type.");
        }
    }

    private static AutoModActionInput NormalizeAction(AutoModActionInput action)
    {
        var type = action.Type.Trim().ToLowerInvariant() switch
        {
            "blockmessage" or "block_message" => "BlockMessage",
            "sendalertmessage" or "send_alert_message" => "SendAlertMessage",
            "timeout" => "Timeout",
            "blockmemberinteraction" or "block_member_interaction" => "BlockMemberInteraction",
            _ => throw InvalidArgument("AutoMod action type is invalid.")
        };
        return type switch
        {
            "BlockMessage" => new AutoModActionInput(type, null, action.CustomMessage is null ? null : NormalizeText(action.CustomMessage, 150, true), null),
            "SendAlertMessage" => new AutoModActionInput(type, NormalizeSnowflake(action.ChannelId ?? string.Empty), null, null),
            "Timeout" when action.TimeoutSeconds is >= 1 and <= 2_419_200 => new AutoModActionInput(type, null, null, action.TimeoutSeconds),
            "BlockMemberInteraction" => new AutoModActionInput(type, null, null, null),
            _ => throw InvalidArgument("AutoMod action metadata is invalid.")
        };
    }

    private static AutoModRuleView ToView(
        string id,
        string guildId,
        string triggerType,
        AutoModRuleConfigurationInput config)
    {
        var result = new AutoModRuleView(
            id,
            guildId,
            config.Name,
            config.EventType,
            triggerType,
            config.KeywordFilter,
            config.RegexPatterns,
            config.AllowList,
            config.Presets,
            config.MentionTotalLimit,
            config.MentionRaidProtectionEnabled,
            config.Actions.Select(action => new AutoModActionView(
                action.Type,
                action.ChannelId,
                action.CustomMessage,
                action.TimeoutSeconds)).ToImmutableArray(),
            config.Enabled,
            config.ExemptRoleIds,
            config.ExemptChannelIds);
        return result with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(result) };
    }

    private static Dictionary<string, object?> ConfigurationPayload(
        AutoModRuleConfigurationInput config,
        string trigger,
        bool includeTrigger)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = config.Name,
            ["event_type"] = config.EventType == "MemberUpdate" ? 2 : 1,
            ["trigger_metadata"] = new Dictionary<string, object?>
            {
                ["keyword_filter"] = config.KeywordFilter,
                ["regex_patterns"] = config.RegexPatterns,
                ["allow_list"] = config.AllowList,
                ["presets"] = config.Presets.Select(PresetValue).ToArray(),
                ["mention_total_limit"] = config.MentionTotalLimit,
                ["mention_raid_protection_enabled"] = config.MentionRaidProtectionEnabled
            },
            ["actions"] = config.Actions.Select(ActionPayload).ToArray(),
            ["enabled"] = config.Enabled,
            ["exempt_roles"] = config.ExemptRoleIds,
            ["exempt_channels"] = config.ExemptChannelIds
        };
        if (includeTrigger)
        {
            payload["trigger_type"] = TriggerValue(trigger);
        }

        return payload;
    }

    private static Dictionary<string, object?> ActionPayload(AutoModActionInput action)
    {
        var metadata = new Dictionary<string, object?>();
        if (action.ChannelId is not null) metadata["channel_id"] = action.ChannelId;
        if (action.CustomMessage is not null) metadata["custom_message"] = action.CustomMessage;
        if (action.TimeoutSeconds.HasValue) metadata["duration_seconds"] = action.TimeoutSeconds.Value;
        return new Dictionary<string, object?>
        {
            ["type"] = action.Type switch
            {
                "BlockMessage" => 1,
                "SendAlertMessage" => 2,
                "Timeout" => 3,
                "BlockMemberInteraction" => 4,
                _ => throw InvalidArgument("AutoMod action type is invalid.")
            },
            ["metadata"] = metadata.Count == 0 ? null : metadata
        };
    }

    private static int TriggerValue(string trigger) => trigger switch
    {
        "Keyword" => 1,
        "Spam" => 3,
        "KeywordPreset" => 4,
        "MentionSpam" => 5,
        "MemberProfile" => 6,
        _ => throw InvalidArgument("AutoMod trigger type is invalid.")
    };

    private static int PresetValue(string preset) => preset switch
    {
        "Profanity" => 1,
        "SexualContent" => 2,
        "Slurs" => 3,
        _ => throw InvalidArgument("AutoMod preset is invalid.")
    };

    private static string NormalizeTriggerType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "keyword" => "Keyword",
        "spam" => "Spam",
        "keywordpreset" or "keyword_preset" => "KeywordPreset",
        "mentionspam" or "mention_spam" => "MentionSpam",
        "memberprofile" or "member_profile" => "MemberProfile",
        _ => throw InvalidArgument("AutoMod trigger type is invalid.")
    };

    private static string NormalizeEventType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "messagesend" or "message_send" => "MessageSend",
        "memberupdate" or "member_update" => "MemberUpdate",
        _ => throw InvalidArgument("AutoMod event type is invalid.")
    };

    private static string NormalizePreset(string value) => value.Trim().ToLowerInvariant() switch
    {
        "profanity" => "Profanity",
        "sexualcontent" or "sexual_content" => "SexualContent",
        "slurs" => "Slurs",
        _ => throw InvalidArgument("AutoMod preset is invalid.")
    };

    private static ImmutableArray<string> NormalizeTexts(ImmutableArray<string> values, int maximum, int maxLength)
    {
        if (values.IsDefault || values.Length > maximum) throw InvalidArgument("AutoMod text array exceeds its limit.");
        return values.Select(value => NormalizeText(value, maxLength, false)).Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableArray<string> NormalizeIds(ImmutableArray<string> values, int maximum)
    {
        if (values.IsDefault || values.Length > maximum) throw InvalidArgument("AutoMod exemption array exceeds its limit.");
        return values.Select(NormalizeSnowflake).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static string NormalizeText(string value, int maximum, bool allowEmpty)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return (allowEmpty || normalized.Length > 0) && normalized.Length <= maximum && !normalized.Any(char.IsControl)
            ? normalized
            : throw InvalidArgument("AutoMod text is empty, too long, or contains control characters.");
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

    private string RuleRoute(string ruleId) => $"guilds/{GuildId()}/auto-moderation/rules/{NormalizeSnowflake(ruleId)}";

    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);

    private static string NormalizeSnowflake(string value) => ChannelApiState.NormalizeSnowflake(value);

    private static void EnsureDigest(string? supplied, string observed)
    {
        var value = supplied?.Trim().ToLowerInvariant();
        if (value is not { Length: 64 } || !value.All(char.IsAsciiHexDigit)) throw InvalidArgument("Expected digest is invalid.");
        if (value != observed) throw new StewardException(StewardErrorCodes.StateConflict, "AutoMod state changed after approval.");
    }

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options) ?? throw InvalidArgument("Mutation arguments are invalid.");

    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options) ?? throw InvalidResponse();

    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);
    private static StewardException InvalidResponse() => new(StewardErrorCodes.DiscordUnavailable, "Discord returned invalid AutoMod state.");
    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "AutoMod mutation is not supported.");

    private sealed record AutoModCollectionState(string StateDigest);
    private sealed record AutoModExistenceState(string RuleId, bool Exists);
    private sealed record AutoModCreationExpectation(string TriggerType, AutoModRuleConfigurationInput Configuration)
    {
        internal bool Matches(AutoModRuleView rule) =>
            rule.TriggerType == TriggerType &&
            JsonEqual(
                State(ToView(rule.Id, rule.GuildId, TriggerType, Configuration)),
                State(rule));
    }
}
