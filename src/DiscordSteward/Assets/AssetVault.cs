using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiscordSteward.Errors;

namespace DiscordSteward.Assets;

public sealed record AssetVaultOptions(
    string InboxPath,
    string VaultPath,
    long MaximumAssetBytes);

public sealed record AssetCandidateView(
    string CandidateId,
    string Filename,
    long ByteLength,
    string Sha256Digest);

public sealed record AssetView(
    string AssetId,
    string Filename,
    string MediaType,
    long ByteLength,
    string Sha256Digest,
    DateTimeOffset RegisteredAt);

public sealed record AssetCatalogView(
    ImmutableArray<AssetCandidateView> Candidates,
    ImmutableArray<AssetView> Assets);

public interface IAssetVault
{
    Task<AssetCatalogView> ListAsync(CancellationToken cancellationToken);

    Task<AssetView> RegisterAsync(
        string candidateId,
        string expectedDigest,
        string mediaType,
        long byteLength,
        CancellationToken cancellationToken);

    Task<AssetView> GetAsync(string assetId, CancellationToken cancellationToken);

    Task<AssetView> DeleteAsync(string assetId, CancellationToken cancellationToken);

    Task<string> GetContentPathAsync(string assetId, CancellationToken cancellationToken);
}

internal sealed class AssetVault(AssetVaultOptions options, TimeProvider timeProvider) : IAssetVault, IDisposable
{
    private static readonly ImmutableHashSet<string> AllowedMediaTypes = new[]
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "audio/ogg",
        "audio/mpeg"
    }.ToImmutableHashSet(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AssetCatalogView> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectories();
            return new AssetCatalogView(
                await ListCandidatesCoreAsync(cancellationToken).ConfigureAwait(false),
                await ListAssetsCoreAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AssetView> RegisterAsync(
        string candidateId,
        string expectedDigest,
        string mediaType,
        long byteLength,
        CancellationToken cancellationToken)
    {
        var normalizedCandidateId = NormalizeCandidateId(candidateId);
        var normalizedDigest = NormalizeDigest(expectedDigest);
        var normalizedMediaType = NormalizeMediaType(mediaType);
        if (byteLength is < 1 || byteLength > options.MaximumAssetBytes)
        {
            throw InvalidArgument("Asset byte length is outside the configured limit.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectories();
            var candidate = (await ListCandidateFilesAsync(cancellationToken).ConfigureAwait(false))
                .SingleOrDefault(item => string.Equals(item.View.CandidateId, normalizedCandidateId, StringComparison.Ordinal))
                ?? throw NotFound("Asset candidate was not found.");
            if (candidate.View.ByteLength != byteLength ||
                !string.Equals(candidate.View.Sha256Digest, normalizedDigest, StringComparison.Ordinal))
            {
                throw new StewardException(
                    StewardErrorCodes.StateConflict,
                    "Asset candidate no longer matches the approved digest and byte length.");
            }

            var assetId = $"asset_{normalizedDigest}";
            var metadataPath = MetadataPath(assetId);
            if (File.Exists(metadataPath))
            {
                var existing = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(existing.MediaType, normalizedMediaType, StringComparison.Ordinal) ||
                    !string.Equals(existing.Filename, candidate.View.Filename, StringComparison.Ordinal))
                {
                    throw new StewardException(
                        StewardErrorCodes.IdempotencyConflict,
                        "This asset digest is already registered with different immutable metadata.");
                }

                return existing;
            }

            var contentPath = ContentPath(assetId);
            var temporaryPath = $"{contentPath}.{Guid.CreateVersion7():N}.tmp";
            try
            {
                await using (var source = OpenCandidate(candidate.Path))
                await using (var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                var copiedDigest = await ComputeDigestAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(copiedDigest, normalizedDigest, StringComparison.Ordinal))
                {
                    throw new StewardException(
                        StewardErrorCodes.StateConflict,
                        "Asset candidate changed while it was being imported.");
                }

                File.Move(temporaryPath, contentPath);
                var asset = new AssetView(
                    assetId,
                    candidate.View.Filename,
                    normalizedMediaType,
                    byteLength,
                    normalizedDigest,
                    timeProvider.GetUtcNow());
                await WriteMetadataAsync(metadataPath, asset, cancellationToken).ConfigureAwait(false);
                return asset;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AssetView> GetAsync(string assetId, CancellationToken cancellationToken)
    {
        var normalizedAssetId = NormalizeAssetId(assetId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectories();
            return await ReadMetadataAsync(MetadataPath(normalizedAssetId), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AssetView> DeleteAsync(string assetId, CancellationToken cancellationToken)
    {
        var normalizedAssetId = NormalizeAssetId(assetId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectories();
            var referencePath = Path.Combine(options.VaultPath, $"{normalizedAssetId}.refs");
            if (File.Exists(referencePath) && new FileInfo(referencePath).Length > 0)
            {
                throw new StewardException(
                    StewardErrorCodes.PolicyDenied,
                    "The asset has durable references and cannot be deleted.");
            }

            var metadataPath = MetadataPath(normalizedAssetId);
            var asset = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            File.Delete(ContentPath(normalizedAssetId));
            File.Delete(metadataPath);
            if (File.Exists(referencePath))
            {
                File.Delete(referencePath);
            }

            return asset;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetContentPathAsync(string assetId, CancellationToken cancellationToken)
    {
        var normalizedAssetId = NormalizeAssetId(assetId);
        await GetAsync(normalizedAssetId, cancellationToken).ConfigureAwait(false);
        var path = ContentPath(normalizedAssetId);
        return File.Exists(path) ? path : throw NotFound("Asset content was not found.");
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<ImmutableArray<AssetCandidateView>> ListCandidatesCoreAsync(
        CancellationToken cancellationToken) =>
        (await ListCandidateFilesAsync(cancellationToken).ConfigureAwait(false))
            .Select(item => item.View)
            .OrderBy(item => item.CandidateId, StringComparer.Ordinal)
            .ToImmutableArray();

    private async Task<ImmutableArray<CandidateFile>> ListCandidateFilesAsync(
        CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(options.InboxPath, "*", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Take(101)
            .ToArray();
        if (files.Length > 100)
        {
            throw TooLarge("Asset inbox contains more than 100 candidate files.");
        }

        var results = ImmutableArray.CreateBuilder<CandidateFile>();
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !IsInside(options.InboxPath, info.FullName))
            {
                continue;
            }

            if (info.Length is < 1 || info.Length > options.MaximumAssetBytes)
            {
                continue;
            }

            var digest = await ComputeDigestAsync(info.FullName, cancellationToken).ConfigureAwait(false);
            var candidateId = $"candidate_{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(info.Name)))[..24]}";
            results.Add(new CandidateFile(
                info.FullName,
                new AssetCandidateView(candidateId, Bound(info.Name, 255), info.Length, digest)));
        }

        return results.ToImmutable();
    }

    private async Task<ImmutableArray<AssetView>> ListAssetsCoreAsync(CancellationToken cancellationToken)
    {
        var metadataFiles = Directory.EnumerateFiles(options.VaultPath, "asset_*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Take(101)
            .ToArray();
        if (metadataFiles.Length > 100)
        {
            throw TooLarge("Asset vault contains more than 100 assets.");
        }

        var assets = ImmutableArray.CreateBuilder<AssetView>();
        foreach (var path in metadataFiles)
        {
            assets.Add(await ReadMetadataAsync(path, cancellationToken).ConfigureAwait(false));
        }

        return assets.OrderBy(item => item.AssetId, StringComparer.Ordinal).ToImmutableArray();
    }

    private static FileStream OpenCandidate(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 81920,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<string> ComputeDigestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = OpenCandidate(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task WriteMetadataAsync(
        string path,
        AssetView asset,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.CreateVersion7():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, asset, cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<AssetView> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw NotFound("Asset was not found.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var asset = await JsonSerializer.DeserializeAsync<AssetView>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new StewardException(StewardErrorCodes.AuditUnavailable, "Asset metadata is invalid.");
        return asset with
        {
            AssetId = NormalizeAssetId(asset.AssetId),
            Filename = Bound(asset.Filename, 255),
            MediaType = NormalizeMediaType(asset.MediaType),
            Sha256Digest = NormalizeDigest(asset.Sha256Digest)
        };
    }

    private void EnsureDirectories()
    {
        if (options.MaximumAssetBytes is < 1 or > 25 * 1024 * 1024)
        {
            throw new InvalidOperationException("Asset vault maximum size must be between 1 byte and 25 MiB.");
        }

        Directory.CreateDirectory(options.InboxPath);
        Directory.CreateDirectory(options.VaultPath);
    }

    private string ContentPath(string assetId) => Path.Combine(options.VaultPath, $"{assetId}.bin");

    private string MetadataPath(string assetId) => Path.Combine(options.VaultPath, $"{assetId}.json");

    private static bool IsInside(string directory, string path)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal);
    }

    private static string NormalizeCandidateId(string value) =>
        value is { Length: 34 } && value.StartsWith("candidate_", StringComparison.Ordinal) && value[10..].All(char.IsAsciiHexDigit)
            ? value.ToLowerInvariant()
            : throw InvalidArgument("Asset candidate ID is invalid.");

    private static string NormalizeAssetId(string value) =>
        value is { Length: 70 } && value.StartsWith("asset_", StringComparison.Ordinal) && value[6..].All(char.IsAsciiHexDigit)
            ? value.ToLowerInvariant()
            : throw InvalidArgument("Asset ID is invalid.");

    private static string NormalizeDigest(string value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit)
            ? value.ToLowerInvariant()
            : throw InvalidArgument("Asset digest must be a lowercase SHA-256 digest.");

    private static string NormalizeMediaType(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && AllowedMediaTypes.Contains(normalized)
            ? normalized
            : throw InvalidArgument("Asset media type is not allowed.");
    }

    private static string Bound(string? value, int maximum) =>
        (value ?? string.Empty).Length <= maximum ? value ?? string.Empty : (value ?? string.Empty)[..maximum];

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException NotFound(string message) => new(StewardErrorCodes.ResourceNotFound, message);

    private static StewardException TooLarge(string message) => new(StewardErrorCodes.ResultTooLarge, message);

    private sealed record CandidateFile(string Path, AssetCandidateView View);
}
