using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DiscordSteward.Tests;

public sealed class StewardProbeTests
{
    [Fact]
    public async Task Probe_ValidatesAnUnrestrictedProfileWithoutConnectingToDiscord()
    {
        var repositoryRoot = GetRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        var assemblyPath = Path.Combine(
            repositoryRoot,
            "src",
            "DiscordSteward",
            "bin",
            configuration,
            "net10.0",
            "DiscordSteward.dll");
        Assert.True(File.Exists(assemblyPath), $"Build the server before running the probe test: {assemblyPath}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = repositoryRoot
            }
        };
        process.StartInfo.ArgumentList.Add(assemblyPath);
        process.StartInfo.ArgumentList.Add("--probe");
        process.StartInfo.Environment["Steward__ProfilePath"] = Path.Combine(
            repositoryRoot,
            "config",
            "profiles",
            "unrestricted-autonomy.example.json");

        using (process)
        {
            Assert.True(process.Start());
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            Assert.True(process.ExitCode == 0, stderr);
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            Assert.Equal("ready", root.GetProperty("status").GetString());
            Assert.Equal("UnrestrictedAutonomy", root.GetProperty("authorizationMode").GetString());
            Assert.Equal("unrestricted", root.GetProperty("mode").GetString());
            Assert.Equal("100000000000000001", root.GetProperty("guildId").GetString());
            Assert.True(root.GetProperty("registeredToolCount").GetInt32() > 0);
        }
    }

    private static string GetRepositoryRoot([CallerFilePath] string? sourceFile = null)
    {
        var testProjectDirectory = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException("Could not determine the test project directory.");
        return Path.GetFullPath(Path.Combine(testProjectDirectory, "..", ".."));
    }
}