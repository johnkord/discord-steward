using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;
using DiscordSteward.Operations;
using DiscordSteward.Tools;

namespace DiscordSteward.Snapshots;

public sealed class ConfigurationSnapshotService(
    IDiscordGuildReader guildReader,
    IConfigurationSnapshotStore store,
    StewardRuntimeSettings settings,
    TimeProvider timeProvider)
{
    public async Task<ConfigurationSnapshotCapture> CaptureAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
        var json = ConfigurationSnapshotCanonicalizer.Serialize(snapshot);
        var capturedAt = timeProvider.GetUtcNow();
        var stored = new StoredConfigurationSnapshot(
            Guid.CreateVersion7(capturedAt).ToString("D", CultureInfo.InvariantCulture),
            snapshot.Id,
            ConfigurationSnapshotCanonicalizer.ComputeDigest(json),
            capturedAt,
            json);
        try
        {
            await store.SaveSnapshotAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new StewardException(
                StewardErrorCodes.AuditUnavailable,
                "The configuration snapshot could not be persisted.");
        }

        return new ConfigurationSnapshotCapture(
            stored.SnapshotId,
            stored.GuildId,
            stored.Digest,
            stored.CapturedAt,
            snapshot.Roles.Length,
            snapshot.Categories.Length,
            snapshot.Channels.Length);
    }

    public async Task<ConfigurationComparison> CompareAsync(
        string snapshotId,
        CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeSnapshotId(snapshotId);
        StoredConfigurationSnapshot baseline;
        try
        {
            baseline = await store.GetSnapshotAsync(normalizedId, cancellationToken).ConfigureAwait(false)
                ?? throw new StewardException(
                    StewardErrorCodes.ResourceNotFound,
                    "The requested configuration snapshot was not found.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new StewardException(
                StewardErrorCodes.AuditUnavailable,
                "The configuration snapshot store is unavailable.");
        }

        var previous = ConfigurationSnapshotCanonicalizer.Deserialize(baseline.SnapshotJson);
        var current = await ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(previous.Id, current.Id, StringComparison.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.GuildMismatch,
                "The stored snapshot does not belong to the configured guild.");
        }

        var currentJson = ConfigurationSnapshotCanonicalizer.Serialize(current);
        var currentDigest = ConfigurationSnapshotCanonicalizer.ComputeDigest(currentJson);
        return new ConfigurationComparison(
            baseline.SnapshotId,
            current.Id,
            baseline.Digest,
            currentDigest,
            !string.Equals(baseline.Digest, currentDigest, StringComparison.Ordinal),
            CompareResources(previous.Roles, current.Roles, role => role.Id),
            CompareResources(previous.Categories, current.Categories, category => category.Id),
            CompareResources(previous.Channels, current.Channels, channel => channel.Id));
    }

    private async Task<GuildConfigurationSnapshotView> ReadCurrentAsync(
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            throw DiscordErrorMapper.NotConfigured();
        }

        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var configuredGuildId = settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(snapshot.Id, configuredGuildId, StringComparison.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.GuildMismatch,
                "Discord returned a resource outside the configured guild.");
        }

        return ConfigurationViewFactory.CreateSnapshot(snapshot);
    }

    private static ConfigurationResourceChanges CompareResources<T>(
        IEnumerable<T> previous,
        IEnumerable<T> current,
        Func<T, string> getId)
    {
        var previousById = previous.ToDictionary(getId, StringComparer.Ordinal);
        var currentById = current.ToDictionary(getId, StringComparer.Ordinal);
        var added = currentById.Keys.Except(previousById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var removed = previousById.Keys.Except(currentById.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var changed = previousById.Keys
            .Intersect(currentById.Keys, StringComparer.Ordinal)
            .Where(id => !string.Equals(
                JsonSerializer.Serialize(previousById[id], OperationJson.SerializerOptions),
                JsonSerializer.Serialize(currentById[id], OperationJson.SerializerOptions),
                StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        return new ConfigurationResourceChanges(added, removed, changed);
    }

    private static string NormalizeSnapshotId(string value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D", CultureInfo.InvariantCulture)
            : throw new StewardException(
                StewardErrorCodes.InvalidArgument,
                "Snapshot ID must be a non-empty UUID.");
}
