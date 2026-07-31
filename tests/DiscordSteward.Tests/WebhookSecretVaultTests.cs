using DiscordSteward.Secrets;

namespace DiscordSteward.Tests;

public sealed class WebhookSecretVaultTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"discord-steward-webhooks-{Guid.NewGuid():N}");

    [Fact]
    public async Task Store_ReturnsOpaqueIdAndPersistsOwnerOnlySecret()
    {
        using var vault = new WebhookSecretVault(
            new WebhookSecretVaultOptions(_root),
            TimeProvider.System);

        var stored = await vault.StoreAsync("123", "456", new string('x', 32), CancellationToken.None);
        var loaded = await vault.GetAsync(stored.VaultId, CancellationToken.None);

        Assert.StartsWith("whsec_", stored.VaultId, StringComparison.Ordinal);
        Assert.Equal(stored, loaded);
        var path = Assert.Single(Directory.GetFiles(_root, "*.json"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public async Task Delete_RemovesCredentialWithoutExposingToken()
    {
        using var vault = new WebhookSecretVault(
            new WebhookSecretVaultOptions(_root),
            TimeProvider.System);
        var stored = await vault.StoreAsync("123", "456", new string('y', 32), CancellationToken.None);

        await vault.DeleteAsync(stored.VaultId, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(_root, "*.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
