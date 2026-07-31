using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordSteward.Errors;

namespace DiscordSteward.Secrets;

public sealed record WebhookSecretVaultOptions(string Path);

internal sealed record StoredWebhookSecret(
    string VaultId,
    string WebhookId,
    string ChannelId,
    string Token,
    DateTimeOffset CreatedAt);

internal interface IWebhookSecretVault
{
    Task<StoredWebhookSecret> StoreAsync(
        string webhookId,
        string channelId,
        string token,
        CancellationToken cancellationToken);

    Task<StoredWebhookSecret> GetAsync(string vaultId, CancellationToken cancellationToken);

    Task UpdateChannelAsync(string vaultId, string channelId, CancellationToken cancellationToken);

    Task DeleteAsync(string vaultId, CancellationToken cancellationToken);
}

internal sealed class WebhookSecretVault(
    WebhookSecretVaultOptions options,
    TimeProvider timeProvider) : IWebhookSecretVault, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StoredWebhookSecret> StoreAsync(
        string webhookId,
        string channelId,
        string token,
        CancellationToken cancellationToken)
    {
        var normalizedWebhookId = NormalizeSnowflake(webhookId);
        var normalizedChannelId = NormalizeSnowflake(channelId);
        var normalizedToken = NormalizeToken(token);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var vaultId = $"whsec_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";
            var value = new StoredWebhookSecret(
                vaultId,
                normalizedWebhookId,
                normalizedChannelId,
                normalizedToken,
                timeProvider.GetUtcNow());
            await WriteAsync(value, cancellationToken).ConfigureAwait(false);
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StoredWebhookSecret> GetAsync(string vaultId, CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeVaultId(vaultId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            return await ReadAsync(normalizedId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateChannelAsync(
        string vaultId,
        string channelId,
        CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeVaultId(vaultId);
        var normalizedChannelId = NormalizeSnowflake(channelId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var current = await ReadAsync(normalizedId, cancellationToken).ConfigureAwait(false);
            await WriteAsync(current with { ChannelId = normalizedChannelId }, cancellationToken, replace: true).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string vaultId, CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeVaultId(vaultId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectory();
            var path = SecretPath(normalizedId);
            if (File.Exists(path))
            {
                EnsureRegularFile(path);
                File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task WriteAsync(
        StoredWebhookSecret value,
        CancellationToken cancellationToken,
        bool replace = false)
    {
        var path = SecretPath(value.VaultId);
        var temporary = $"{path}.{Guid.CreateVersion7():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            SetOwnerOnly(temporary);
            if (replace)
            {
                File.Move(temporary, path, overwrite: true);
            }
            else
            {
                File.Move(temporary, path);
            }

            SetOwnerOnly(path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<StoredWebhookSecret> ReadAsync(string vaultId, CancellationToken cancellationToken)
    {
        var path = SecretPath(vaultId);
        if (!File.Exists(path))
        {
            throw new StewardException(StewardErrorCodes.ResourceNotFound, "Stored webhook credential was not found.");
        }

        EnsureRegularFile(path);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var value = await JsonSerializer.DeserializeAsync<StoredWebhookSecret>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new StewardException(StewardErrorCodes.AuditUnavailable, "Stored webhook credential is invalid.");
        return value with
        {
            VaultId = NormalizeVaultId(value.VaultId),
            WebhookId = NormalizeSnowflake(value.WebhookId),
            ChannelId = NormalizeSnowflake(value.ChannelId),
            Token = NormalizeToken(value.Token)
        };
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(options.Path);
        var info = new DirectoryInfo(options.Path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new StewardException(StewardErrorCodes.PolicyDenied, "Webhook vault cannot be a symbolic link.");
        }

        SetDirectoryOwnerOnly(options.Path);
    }

    private string SecretPath(string vaultId) => Path.Combine(options.Path, $"{vaultId}.json");

    private static void EnsureRegularFile(string path)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new StewardException(StewardErrorCodes.PolicyDenied, "Webhook credential file cannot be a symbolic link.");
        }
    }

    private static void SetOwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void SetDirectoryOwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string NormalizeVaultId(string value) =>
        value is { Length: 38 } && value.StartsWith("whsec_", StringComparison.Ordinal) && value[6..].All(char.IsAsciiHexDigit)
            ? value.ToLowerInvariant()
            : throw new StewardException(StewardErrorCodes.InvalidArgument, "Stored webhook ID is invalid.");

    private static string NormalizeSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : throw new StewardException(StewardErrorCodes.InvalidArgument, "Discord ID must be a positive snowflake string.");

    private static string NormalizeToken(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length is >= 20 and <= 256 && !value.Any(char.IsControl)
            ? value
            : throw new StewardException(StewardErrorCodes.DiscordUnavailable, "Discord returned an invalid webhook credential.");
}
