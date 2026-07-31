using DiscordSteward.Configuration;

namespace DiscordSteward.Tests;

public sealed class StewardToolManifestTests
{
    [Fact]
    public void DefaultManifest_IsObserverOnlyAndDeterministic()
    {
        var first = StewardToolManifest.Create([], ClosedGates());
        var second = StewardToolManifest.Create([], ClosedGates());

        Assert.Contains("get_steward_capabilities", first.RegisteredNames);
        Assert.Contains("get_channel", first.RegisteredNames);
        Assert.DoesNotContain("get_operation", first.RegisteredNames);
        Assert.DoesNotContain("update_channel", first.RegisteredNames);
        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(64, first.Digest.Length);
    }

    [Fact]
    public void Gates_RemoveDisabledSchemasAndReportMissingCompanions()
    {
        var manifest = StewardToolManifest.Create(
            ["get_steward_capabilities", "get_channel", "get_operation", "update_channel"],
            new StewardRiskGates(
                SensitiveReads: false,
                ReversibleWrites: true,
                PersonImpactingWrites: false,
                IrreversibleWrites: false));

        Assert.Contains("update_channel", manifest.RegisteredNames);
        Assert.DoesNotContain("get_operation", manifest.RegisteredNames);
        Assert.Contains(manifest.Warnings, warning => warning.Contains("get_operation", StringComparison.Ordinal));
        Assert.Contains(manifest.Warnings, warning => warning.Contains("reconcile_operation", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_RejectsUnknownNames()
    {
        var exception = Assert.Throws<ArgumentException>(() => StewardToolManifest.Create(
            ["generic_discord_request"],
            ClosedGates()));

        Assert.Contains("generic_discord_request", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllToolsSelector_WithOpenGatesRegistersCompleteCatalog()
    {
        var manifest = StewardToolManifest.Create(
            ["*"],
            new StewardRiskGates(
                SensitiveReads: true,
                ReversibleWrites: true,
                PersonImpactingWrites: true,
                IrreversibleWrites: true));

        Assert.Equal(StewardToolManifest.DefinedNames.ToArray(), manifest.RegisteredNames.ToArray());
        Assert.Equal(173, manifest.RegisteredNames.Length);
        Assert.Empty(manifest.Warnings);
    }

    [Fact]
    public void AllToolsSelector_CannotBeCombinedWithExactNames()
    {
        var exception = Assert.Throws<ArgumentException>(() => StewardToolManifest.Create(
            ["*", "get_guild"],
            ClosedGates()));

        Assert.Contains("cannot be combined", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrestrictedAutonomy_RequiresAllToolsAndForcesEveryRiskGateOpen()
    {
        var manifest = StewardToolManifest.Create(
            ["*"],
            ClosedGates(),
            StewardAuthorizationMode.UnrestrictedAutonomy);

        Assert.Equal(StewardAuthorizationMode.UnrestrictedAutonomy, manifest.AuthorizationMode);
        Assert.True(manifest.RiskGates.SensitiveReads);
        Assert.True(manifest.RiskGates.ReversibleWrites);
        Assert.True(manifest.RiskGates.PersonImpactingWrites);
        Assert.True(manifest.RiskGates.IrreversibleWrites);
        Assert.True(manifest.IsCompleteCatalog);
        Assert.True(manifest.RepresentsEveryRiskClass);
        Assert.Equal(StewardToolManifest.DefinedNames.ToArray(), manifest.RegisteredNames.ToArray());
        Assert.Empty(manifest.Warnings);
    }

    [Fact]
    public void UnrestrictedAutonomy_RejectsPartialToolSelection()
    {
        var exception = Assert.Throws<ArgumentException>(() => StewardToolManifest.Create(
            ["get_guild"],
            StewardRiskGates.AllOpen,
            StewardAuthorizationMode.UnrestrictedAutonomy));

        Assert.Contains("requires toolAllowlist", exception.Message, StringComparison.Ordinal);
    }

    private static StewardRiskGates ClosedGates() => new(
        SensitiveReads: false,
        ReversibleWrites: false,
        PersonImpactingWrites: false,
        IrreversibleWrites: false);
}
