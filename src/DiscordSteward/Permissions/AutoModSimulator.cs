using System.Collections.Immutable;
using System.Text.RegularExpressions;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Permissions;

public sealed record AutoModSimulationMatch(
    string Source,
    string Pattern,
    string MatchedText);

public sealed record AutoModSimulationResult(
    bool WouldTrigger,
    string SampleType,
    ImmutableArray<AutoModSimulationMatch> Matches,
    ImmutableArray<string> Limitations);

public static partial class AutoModSimulator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static AutoModSimulationResult Simulate(
        AutoModRuleView rule,
        string sampleType,
        string sampleText)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (sampleText is null || sampleText.Length > 4_000)
        {
            throw new StewardException(
                StewardErrorCodes.InvalidArgument,
                "AutoMod sample text must contain at most 4000 characters.");
        }

        var normalizedType = sampleType?.Trim().ToLowerInvariant();
        if (normalizedType is not "message" and not "profile")
        {
            throw new StewardException(
                StewardErrorCodes.InvalidArgument,
                "AutoMod sample type must be message or profile.");
        }

        var matches = ImmutableArray.CreateBuilder<AutoModSimulationMatch>();
        var limitations = ImmutableArray.CreateBuilder<string>();
        var allowed = rule.AllowList.Any(pattern => WildcardMatch(sampleText, pattern, out _));
        if (!allowed)
        {
            foreach (var pattern in rule.KeywordFilter)
            {
                if (WildcardMatch(sampleText, pattern, out var matched))
                {
                    matches.Add(new AutoModSimulationMatch("keyword", pattern, matched));
                }
            }

            foreach (var pattern in rule.RegexPatterns)
            {
                try
                {
                    var match = Regex.Match(
                        sampleText,
                        pattern,
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                        RegexTimeout);
                    if (match.Success)
                    {
                        matches.Add(new AutoModSimulationMatch("regex", pattern, match.Value));
                    }
                }
                catch (ArgumentException)
                {
                    limitations.Add($"Regex pattern could not be evaluated locally: {pattern}");
                }
                catch (RegexMatchTimeoutException)
                {
                    limitations.Add($"Regex pattern exceeded the 100 ms local timeout: {pattern}");
                }
            }
        }

        if (rule.MentionTotalLimit.HasValue && normalizedType == "message")
        {
            var mentionCount = MentionRegex().Count(sampleText);
            if (mentionCount > rule.MentionTotalLimit.Value)
            {
                matches.Add(new AutoModSimulationMatch(
                    "mention_total_limit",
                    rule.MentionTotalLimit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    mentionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        if (!rule.Presets.IsEmpty)
        {
            limitations.Add("Discord managed keyword presets are not published and cannot be reproduced locally.");
        }

        if (rule.TriggerType.Contains("Spam", StringComparison.OrdinalIgnoreCase) ||
            rule.MentionRaidProtectionEnabled == true)
        {
            limitations.Add("Discord spam and raid heuristics depend on server-side context and are not simulated.");
        }

        if (allowed)
        {
            limitations.Add("An allow-list wildcard matched, so local keyword and regex matches were suppressed.");
        }

        return new AutoModSimulationResult(
            matches.Count > 0,
            normalizedType,
            matches
                .OrderBy(item => item.Source, StringComparer.Ordinal)
                .ThenBy(item => item.Pattern, StringComparer.Ordinal)
                .ToImmutableArray(),
            limitations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray());
    }

    private static bool WildcardMatch(string text, string pattern, out string matched)
    {
        matched = string.Empty;
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var expression = Regex.Escape(pattern).Replace("\\*", ".*?", StringComparison.Ordinal);
        try
        {
            var match = Regex.Match(
                text,
                expression,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                RegexTimeout);
            matched = match.Success ? match.Value : string.Empty;
            return match.Success;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"<@!?(?:&)?\d+>|@everyone|@here", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();
}
