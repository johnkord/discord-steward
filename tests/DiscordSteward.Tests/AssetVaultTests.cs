using DiscordSteward.Assets;
using DiscordSteward.Errors;

namespace DiscordSteward.Tests;

public sealed class AssetVaultTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"discord-steward-assets-{Guid.NewGuid():N}");

    [Fact]
    public async Task Register_RequiresExactDigestAndReturnsImmutableAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var inbox = Path.Combine(_root, "inbox");
        var vaultPath = Path.Combine(_root, "vault");
        Directory.CreateDirectory(inbox);
        await File.WriteAllBytesAsync(Path.Combine(inbox, "icon.png"), [1, 2, 3, 4], cancellationToken);
        using var vault = new AssetVault(
            new AssetVaultOptions(inbox, vaultPath, 1024),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var candidate = Assert.Single((await vault.ListAsync(cancellationToken)).Candidates);
        var asset = await vault.RegisterAsync(
            candidate.CandidateId,
            candidate.Sha256Digest,
            "image/png",
            candidate.ByteLength,
            cancellationToken);

        Assert.Equal($"asset_{candidate.Sha256Digest}", asset.AssetId);
        Assert.Equal(asset, await vault.GetAsync(asset.AssetId, cancellationToken));
        Assert.True(File.Exists(await vault.GetContentPathAsync(asset.AssetId, cancellationToken)));
    }

    [Fact]
    public async Task Register_RejectsChangedCandidateWithoutImporting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var inbox = Path.Combine(_root, "inbox");
        var vaultPath = Path.Combine(_root, "vault");
        Directory.CreateDirectory(inbox);
        var path = Path.Combine(inbox, "icon.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4], cancellationToken);
        using var vault = new AssetVault(
            new AssetVaultOptions(inbox, vaultPath, 1024),
            TimeProvider.System);
        var candidate = Assert.Single((await vault.ListAsync(cancellationToken)).Candidates);
        await File.WriteAllBytesAsync(path, [5, 6, 7, 8], cancellationToken);

        var exception = await Assert.ThrowsAsync<StewardException>(() => vault.RegisterAsync(
            candidate.CandidateId,
            candidate.Sha256Digest,
            "image/png",
            candidate.ByteLength,
            cancellationToken));

        Assert.Equal(StewardErrorCodes.StateConflict, exception.Code);
        Assert.Empty((await vault.ListAsync(cancellationToken)).Assets);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
