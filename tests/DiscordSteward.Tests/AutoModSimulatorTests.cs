using DiscordSteward.Discord;
using DiscordSteward.Permissions;

namespace DiscordSteward.Tests;

public sealed class AutoModSimulatorTests
{
    [Fact]
    public void Simulate_MatchesKeywordRegexAndMentionLimit()
    {
        var rule = Rule(
            keywords: ["bad*word"],
            regex: ["danger\\d+"],
            mentionLimit: 1);

        var result = AutoModSimulator.Simulate(
            rule,
            "message",
            "bad-long-word danger42 <@123> @here");

        Assert.True(result.WouldTrigger);
        Assert.Equal(
            ["keyword", "mention_total_limit", "regex"],
            result.Matches.Select(match => match.Source));
    }

    [Fact]
    public void Simulate_AllowListSuppressesTextMatches()
    {
        var rule = Rule(
            keywords: ["bad*"],
            allowList: ["*allowed phrase*"],
            regex: ["allowed"]);

        var result = AutoModSimulator.Simulate(rule, "profile", "bad allowed phrase");

        Assert.False(result.WouldTrigger);
        Assert.Empty(result.Matches);
        Assert.Contains(result.Limitations, value => value.Contains("allow-list", StringComparison.Ordinal));
    }

    private static AutoModRuleView Rule(
        string[]? keywords = null,
        string[]? regex = null,
        string[]? allowList = null,
        int? mentionLimit = null) => new(
            "1",
            "2",
            "rule",
            "MessageSend",
            "Keyword",
            [.. keywords ?? []],
            [.. regex ?? []],
            [.. allowList ?? []],
            Presets: [],
            mentionLimit,
            MentionRaidProtectionEnabled: false,
            Actions: [],
            Enabled: true,
            ExemptRoleIds: [],
            ExemptChannelIds: []);
}
