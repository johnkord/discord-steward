using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record AutoModActionInput(
    string Type,
    string? ChannelId,
    string? CustomMessage,
    int? TimeoutSeconds);

public sealed record AutoModRuleConfigurationInput(
    string Name,
    string EventType,
    ImmutableArray<string> KeywordFilter,
    ImmutableArray<string> RegexPatterns,
    ImmutableArray<string> AllowList,
    ImmutableArray<string> Presets,
    int? MentionTotalLimit,
    bool? MentionRaidProtectionEnabled,
    ImmutableArray<AutoModActionInput> Actions,
    bool Enabled,
    ImmutableArray<string> ExemptRoleIds,
    ImmutableArray<string> ExemptChannelIds);

public sealed record CreateAutoModRuleInput(
    string ExpectedRulesStateDigest,
    string TriggerType,
    AutoModRuleConfigurationInput Configuration);

public sealed record UpdateAutoModRuleInput(
    string ExpectedStateDigest,
    AutoModRuleConfigurationInput Configuration);

public sealed record SetAutoModRuleEnabledInput(
    string ExpectedStateDigest,
    bool Enabled);

public sealed record DeleteAutoModRuleInput(
    string ExpectedStateDigest);
