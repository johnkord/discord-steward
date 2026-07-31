using System.Runtime.CompilerServices;
using System.Text.Json;
using DiscordSteward.Configuration;

namespace DiscordSteward.Tests;

public sealed class StewardServerProfileTests
{
    [Theory]
    [InlineData("all-tools.example.json", "example-all-tools", "100000000000000001", 173)]
    [InlineData("observer.example.json", "example-observer", "100000000000000001", 29)]
    [InlineData("channel-operator.example.json", "example-channel-operator", "100000000000000001", 11)]
    [InlineData("community-operator.example.json", "example-community-operator", "100000000000000001", 31)]
    [InlineData("disposable-operator.example.json", "example-disposable-operator", "100000000000000004", 32)]
    public void CheckedInProfile_IsValidAndContainsNoCredential(
        string filename,
        string expectedName,
        string expectedGuildId,
        int expectedRegisteredTools)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ProfilePath(filename)));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("profileVersion").GetInt32());
        Assert.Equal(expectedName, root.GetProperty("Steward").GetProperty("ProfileName").GetString());
        var discord = root.GetProperty("Discord");
        Assert.Equal(expectedGuildId, discord.GetProperty("GuildId").GetString());
        Assert.False(discord.TryGetProperty("BotToken", out _));

        var names = root.GetProperty("toolAllowlist").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        var gates = root.GetProperty("riskGates");
        var manifest = StewardToolManifest.Create(
            names,
            new StewardRiskGates(
                gates.GetProperty("sensitiveReads").GetBoolean(),
                gates.GetProperty("reversibleWrites").GetBoolean(),
                gates.GetProperty("personImpactingWrites").GetBoolean(),
                gates.GetProperty("irreversibleWrites").GetBoolean()));

        Assert.Equal(expectedRegisteredTools, manifest.RegisteredNames.Length);
        Assert.DoesNotContain(
            manifest.RegisteredNames,
            name => !StewardToolManifest.DefinedNames.Contains(name, StringComparer.Ordinal));
        Assert.Equal(
            ["Administrator"],
            root.GetProperty("deniedPermissions").EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    [Fact]
    public void ExampleChannelOperator_RequiresOnlyChannelManagementPermissions()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ProfilePath("channel-operator.example.json")));
        var root = document.RootElement;
        var names = root.GetProperty("toolAllowlist").EnumerateArray().Select(value => value.GetString()!).ToArray();
        var manifest = StewardToolManifest.Create(
            names,
            new StewardRiskGates(
                SensitiveReads: true,
                ReversibleWrites: true,
                PersonImpactingWrites: false,
                IrreversibleWrites: false));

        Assert.Equal(["ManageChannels", "ViewChannel"], manifest.RequiredDiscordPermissions.ToArray());
    }

    [Fact]
    public void UnrestrictedAutonomyProfile_EnablesTheCompleteCatalogWithoutLocalProtections()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ProfilePath("unrestricted-autonomy.example.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("profileVersion").GetInt32());
        Assert.Equal(4, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("example-unrestricted-autonomy", root.GetProperty("Steward").GetProperty("ProfileName").GetString());
        Assert.Equal("UnrestrictedAutonomy", root.GetProperty("authorization").GetProperty("mode").GetString());

        var mode = StewardAuthorizationModeParser.Parse(
            root.GetProperty("schemaVersion").GetInt32(),
            root.GetProperty("authorization").GetProperty("mode").GetString());
        var names = root.GetProperty("toolAllowlist").EnumerateArray().Select(value => value.GetString()!).ToArray();
        var manifest = StewardToolManifest.Create(
            names,
            new StewardRiskGates(false, false, false, false),
            mode);
        var policy = StewardPolicy.Create(
            ["100000000000000002"],
            ["example-protected-"],
            ["Administrator"],
            mode);

        Assert.True(manifest.IsCompleteCatalog);
        Assert.Equal(StewardToolManifest.DefinedNames.ToArray(), manifest.RegisteredNames.ToArray());
        Assert.Equal(StewardRiskGates.AllOpen, manifest.RiskGates);
        Assert.True(policy.IsUnrestricted);
        Assert.Empty(policy.ProtectedResourceIds);
        Assert.Empty(policy.ProtectedNamePrefixes);
        Assert.Empty(policy.DeniedPermissions);
    }

    private static string ProfilePath(
        string filename,
        [CallerFilePath] string? sourceFile = null)
    {
        var root = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty).Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
        return Path.Combine(root, "config", "profiles", filename);
    }
}
