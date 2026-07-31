using System.Runtime.CompilerServices;
using DiscordSteward.Configuration;
using DiscordSteward.Permissions;
using DiscordSteward.Tools;

namespace DiscordSteward.Tests;

public sealed class ToolCatalogCoverageTests
{
    [Fact]
    public void TargetCatalog_ExactlyMatchesManifestAndRegistry()
    {
        var documented = ReadDocumentedToolNames();

        Assert.Equal(173, documented.Length);
        Assert.Equal(documented, StewardToolManifest.DefinedNames);
        Assert.Equal(documented, ManifestToolRegistry.KnownToolNames);
    }

    [Fact]
    public void EveryCatalogTool_ResolvesIndividuallyWithAllGatesOpen()
    {
        var gates = new StewardRiskGates(
            SensitiveReads: true,
            ReversibleWrites: true,
            PersonImpactingWrites: true,
            IrreversibleWrites: true);

        foreach (var name in StewardToolManifest.DefinedNames)
        {
            var manifest = StewardToolManifest.Create([name], gates);
            var type = Assert.Single(ManifestToolRegistry.Resolve(manifest));
            Assert.NotNull(type);
        }
    }

    [Fact]
    public void EveryRequiredPermission_IsPinnedDiscordNetNameOrExplicitIntent()
    {
        var unknown = StewardToolManifest.DefinedNames
            .SelectMany(name => StewardToolManifest.GetRequiredDiscordPermissions(name))
            .Distinct(StringComparer.Ordinal)
            .Where(permission => !permission.EndsWith("Intent", StringComparison.Ordinal))
            .Where(permission => !PermissionCatalog.All.Contains(permission))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unknown);
    }

    [Fact]
    public void SendMessage_BaselinePermissionsDoNotRequireOptionalTtsCapability()
    {
        var permissions = StewardToolManifest.GetRequiredDiscordPermissions("send_message");

        Assert.DoesNotContain("SendTTSMessages", permissions);
        Assert.Contains("SendMessages", permissions);
    }

    private static string[] ReadDocumentedToolNames([CallerFilePath] string? sourceFile = null)
    {
        var root = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty).Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
        var lines = File.ReadAllLines(Path.Combine(root, "docs", "tool-surface-redesign.md"));
        var withinCatalog = false;
        var names = new List<string>();
        foreach (var line in lines)
        {
            if (line == "## Target tool catalog")
            {
                withinCatalog = true;
                continue;
            }

            if (line == "## Existing tool migration")
            {
                break;
            }

            if (!withinCatalog || !line.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }

            var end = line.IndexOf('`', 3);
            if (end > 3)
            {
                names.Add(line[3..end]);
            }
        }

        return names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
}
