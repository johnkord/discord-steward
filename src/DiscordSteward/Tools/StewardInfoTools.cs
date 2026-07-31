using System.Collections.Immutable;
using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;

namespace DiscordSteward.Tools;

public sealed class StewardInfoTools(
    StewardRuntimeSettings settings,
    StewardProfileContext profile,
    DiscordRestSession discordSession,
    StewardPolicy policy,
    StewardToolManifest manifest)
{
    public StewardCapabilities GetCapabilities() => new(
        Service: "discord-steward",
        Version: typeof(StewardInfoTools).Assembly.GetName().Version?.ToString() ?? "unknown",
        Profile: profile.Name,
        ProfileDigest: profile.Digest,
        AuthorizationMode: manifest.AuthorizationMode.ToString(),
        Mode: manifest.AuthorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy
            ? "unrestricted"
            : settings.IsConfigured && manifest.RegisteredNames.Any(name => StewardToolManifest.GetRisk(name) >= StewardToolRisk.R3)
                ? "operator"
                : settings.IsConfigured
                    ? "observer"
                    : "unconfigured-observer",
        GuildId: settings.GuildId?.ToString(CultureInfo.InvariantCulture),
        DiscordTokenConfigured: settings.DiscordTokenConfigured,
        DiscordReadiness: discordSession.GetReadiness(),
        ManifestDigest: manifest.Digest,
        ConfiguredTools: manifest.ConfiguredNames,
        RegisteredTools: manifest.RegisteredNames,
        RequiredDiscordPermissions: manifest.RequiredDiscordPermissions,
        RiskGates: manifest.RiskGates,
        Policy: new StewardPolicySummary(
            policy.ProtectedResourceIds.Count,
            policy.ProtectedNamePrefixes.Length,
            policy.DeniedPermissions.Count),
        Warnings: manifest.Warnings,
        SafetyNotes: GetSafetyNotes());

    private ImmutableArray<string> GetSafetyNotes()
    {
        var notes = ImmutableArray.CreateBuilder<string>();
        if (manifest.AuthorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy)
        {
            notes.Add("DANGER: UnrestrictedAutonomy trusts autonomous callers and disables protected resources, denied permissions, Administrator refusal, deletion blockers, and startup risk gates.");
            notes.Add("Guild binding, typed schemas, required Discord permissions, state preconditions, durable journaling, one-write execution, and verification remain active.");
        }
        else
        {
            notes.Add("InteractiveApproval requires the MCP host to obtain operator approval before every tool call.");
            notes.Add("Excess Discord permissions are advisory unless explicitly denied by the selected profile.");
            notes.Add("Standard MCP does not attest that the host displayed an approval prompt.");
        }

        notes.Add("The server is pinned to one configured Discord guild.");
        notes.Add("The registered tool manifest is immutable for this process.");
        notes.Add("Discord reads use REST only; explicit message tools read bounded guild-channel content and never read DMs.");
        notes.Add("The Discord bot token is never returned by this tool.");
        return notes.ToImmutable();
    }
}

public sealed record StewardCapabilities(
    string Service,
    string Version,
    string Profile,
    string ProfileDigest,
    string AuthorizationMode,
    string Mode,
    string? GuildId,
    bool DiscordTokenConfigured,
    DiscordReadinessSnapshot DiscordReadiness,
    string ManifestDigest,
    ImmutableArray<string> ConfiguredTools,
    ImmutableArray<string> RegisteredTools,
    ImmutableArray<string> RequiredDiscordPermissions,
    StewardRiskGates RiskGates,
    StewardPolicySummary Policy,
    ImmutableArray<string> Warnings,
    ImmutableArray<string> SafetyNotes);

public sealed record StewardPolicySummary(
    int ProtectedResourceCount,
    int ProtectedNamePrefixCount,
    int DeniedPermissionCount);

public sealed record BotPermissionComparison(
    BotEffectivePermissions Observed,
    ImmutableArray<string> RequiredPermissions,
    ImmutableArray<string> MissingPermissions,
    ImmutableArray<string> ExcessPermissions);
