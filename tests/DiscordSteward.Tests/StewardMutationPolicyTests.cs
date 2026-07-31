using DiscordSteward.Configuration;

namespace DiscordSteward.Tests;

public sealed class StewardMutationPolicyTests
{
    [Fact]
    public void DefaultPolicy_ContainsStaticProtectionsOnly()
    {
        var policy = StewardPolicy.CreateDefault();

        Assert.Empty(policy.ProtectedResourceIds);
        Assert.Equal(2, policy.ProtectedNamePrefixes.Length);
        Assert.Equal(["Administrator"], policy.DeniedPermissions);
        Assert.Equal(64, policy.Digest.Length);
    }

    [Fact]
    public void Fingerprint_ChangesWhenSecuritySettingChanges()
    {
        var baseline = StewardPolicy.Create([], ["discsteward"], ["Administrator"]);
        var protectedPolicy = StewardPolicy.Create(["42"], ["discsteward"], ["Administrator"]);
        var deniedPolicy = StewardPolicy.Create([], ["discsteward"], ["Administrator", "ManageWebhooks"]);

        Assert.NotEqual(baseline.Digest, protectedPolicy.Digest);
        Assert.NotEqual(baseline.Digest, deniedPolicy.Digest);
    }

    [Fact]
    public void Create_NormalizesIdsPrefixesAndPermissions()
    {
        var policy = StewardPolicy.Create(
            ["00042", "invalid", null],
            [" DiscSteward ", "discsteward", null],
            [" Administrator ", null]);

        Assert.Equal(["42"], policy.ProtectedResourceIds);
        Assert.Equal("DiscSteward", Assert.Single(policy.ProtectedNamePrefixes));
        Assert.Equal(["Administrator"], policy.DeniedPermissions);
        Assert.True(policy.GetProtection("42", "general").IsProtected);
        Assert.True(policy.GetProtection("43", "discsteward-control").IsProtected);
    }

    [Fact]
    public void UnrestrictedAutonomy_DropsLocalProtectionsAndDeniedPermissions()
    {
        var policy = StewardPolicy.Create(
            ["42"],
            ["discsteward"],
            ["Administrator"],
            StewardAuthorizationMode.UnrestrictedAutonomy);

        Assert.True(policy.IsUnrestricted);
        Assert.Empty(policy.ProtectedResourceIds);
        Assert.Empty(policy.ProtectedNamePrefixes);
        Assert.Empty(policy.DeniedPermissions);
        Assert.False(policy.GetProtection("42", "discsteward-control").IsProtected);
    }

    [Theory]
    [InlineData(3, null, StewardAuthorizationMode.InteractiveApproval)]
    [InlineData(4, "InteractiveApproval", StewardAuthorizationMode.InteractiveApproval)]
    [InlineData(4, "UnrestrictedAutonomy", StewardAuthorizationMode.UnrestrictedAutonomy)]
    public void AuthorizationModeParser_AcceptsSupportedSchemaModes(
        int schemaVersion,
        string? configuredMode,
        StewardAuthorizationMode expected)
    {
        Assert.Equal(expected, StewardAuthorizationModeParser.Parse(schemaVersion, configuredMode));
    }

    [Fact]
    public void AuthorizationModeParser_RejectsUnspecifiedSchemaFourMode()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StewardAuthorizationModeParser.Parse(4, configuredMode: null));

        Assert.Contains("authorization.mode", exception.Message, StringComparison.Ordinal);
    }
}
