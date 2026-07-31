using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Tools;
using System.Collections.Immutable;

namespace DiscordSteward.Tests;

public sealed class StewardInfoToolsTests
{
    [Fact]
    public async Task GetStatus_ReportsReadOnlyCapabilitiesWithoutCredentials()
    {
        var options = DiscordOptions.FromValues("667956000757776386", "sensitive-token-value");
        await using var session = new Discord.DiscordRestSession(options);
        var tools = new StewardInfoTools(
            StewardRuntimeSettings.FromOptions(options),
            StewardProfileContext.Create("test-observer", isExplicit: true, botTokenSecretName: null),
            session,
            StewardPolicy.CreateDefault(),
            StewardToolManifest.Create(
                [],
                new StewardRiskGates(
                    SensitiveReads: false,
                    ReversibleWrites: false,
                    PersonImpactingWrites: false,
                    IrreversibleWrites: false)));

        var status = tools.GetCapabilities();

        Assert.Equal("observer", status.Mode);
        Assert.Equal("InteractiveApproval", status.AuthorizationMode);
        Assert.Equal("test-observer", status.Profile);
        Assert.Equal("667956000757776386", status.GuildId);
        Assert.Equal("not_checked", status.DiscordReadiness.State);
        Assert.Equal(0, status.Policy.ProtectedResourceCount);
        Assert.Equal(2, status.Policy.ProtectedNamePrefixCount);
        Assert.Equal(1, status.Policy.DeniedPermissionCount);
        Assert.Contains("get_guild_configuration_snapshot", status.RegisteredTools);
        Assert.DoesNotContain("update_channel", status.RegisteredTools);
        Assert.False(status.RiskGates.ReversibleWrites);
        Assert.Equal(64, status.ManifestDigest.Length);
        Assert.Contains(status.SafetyNotes, note => note.Contains("never read DMs", StringComparison.Ordinal));
        Assert.Contains(status.SafetyNotes, note => note.Contains("Standard MCP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStatus_ReportsUnrestrictedModeAndCompleteCatalog()
    {
        var options = DiscordOptions.FromValues("667956000757776386", "sensitive-token-value");
        await using var session = new Discord.DiscordRestSession(options);
        var manifest = StewardToolManifest.Create(
            ["*"],
            new StewardRiskGates(false, false, false, false),
            StewardAuthorizationMode.UnrestrictedAutonomy);
        var tools = new StewardInfoTools(
            StewardRuntimeSettings.FromOptions(options),
            StewardProfileContext.Create("test-unrestricted", isExplicit: true, botTokenSecretName: null),
            session,
            StewardPolicy.CreateDefault(StewardAuthorizationMode.UnrestrictedAutonomy),
            manifest);

        var status = tools.GetCapabilities();

        Assert.Equal("unrestricted", status.Mode);
        Assert.Equal("UnrestrictedAutonomy", status.AuthorizationMode);
        Assert.Equal(StewardToolManifest.DefinedNames.ToArray(), status.RegisteredTools.ToArray());
        Assert.Equal(0, status.Policy.ProtectedResourceCount);
        Assert.Equal(0, status.Policy.ProtectedNamePrefixCount);
        Assert.Equal(0, status.Policy.DeniedPermissionCount);
        Assert.Contains(status.SafetyNotes, note => note.StartsWith("DANGER:", StringComparison.Ordinal));
        Assert.Contains(status.SafetyNotes, note => note.Contains("Administrator refusal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetBotPermissions_UnconfiguredReturnsSafeErrorWithoutDiscordCall()
    {
        var inspector = new FakeBotInspector();
        var manifest = StewardToolManifest.Create(
            [],
            new StewardRiskGates(false, false, false, false));
        var tool = new GetBotEffectivePermissionsTool(
            inspector,
            manifest,
            new StewardRuntimeSettings(667956000757776386UL, DiscordTokenConfigured: false));

        var result = await tool.GetPermissions(CancellationToken.None);

        Assert.Equal("error", result.Outcome);
        Assert.Equal("not_configured", result.Error?.Code);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task GetBotPermissions_ReportsMissingAndExcessGrants()
    {
        var inspector = new FakeBotInspector
        {
            Result = new BotEffectivePermissions(
                "667956000757776386",
                "42",
                ["ManageWebhooks", "ViewChannel"],
                "10",
                "Steward",
                1,
                HasAdministrator: false)
        };
        var manifest = StewardToolManifest.Create(
            ["get_bot_effective_permissions", "get_channel", "update_channel"],
            new StewardRiskGates(false, true, false, false));
        var tool = new GetBotEffectivePermissionsTool(
            inspector,
            manifest,
            new StewardRuntimeSettings(667956000757776386UL, DiscordTokenConfigured: true));

        var result = await tool.GetPermissions(CancellationToken.None);

        Assert.Equal("ok", result.Outcome);
        Assert.Contains("ManageChannels", result.Data!.MissingPermissions);
        Assert.Contains("ManageWebhooks", result.Data.ExcessPermissions);
    }

    private sealed class FakeBotInspector : IDiscordBotInspector
    {
        public BotEffectivePermissions Result { get; set; } = new(
            "667956000757776386",
            "42",
            ImmutableArray<string>.Empty,
            HighestRoleId: null,
            HighestRoleName: null,
            HighestRolePosition: null,
            HasAdministrator: false);

        public int CallCount { get; private set; }

        public Task<BotEffectivePermissions> GetEffectivePermissionsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Result);
        }
    }
}
