using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordSteward.Operations;
using DiscordSteward.Tools;

namespace DiscordSteward.Snapshots;

public sealed record StoredConfigurationSnapshot(
    string SnapshotId,
    string GuildId,
    string Digest,
    DateTimeOffset CapturedAt,
    string SnapshotJson);

public sealed record ConfigurationSnapshotCapture(
    string SnapshotId,
    string GuildId,
    string Digest,
    DateTimeOffset CapturedAt,
    int RoleCount,
    int CategoryCount,
    int ChannelCount);

public sealed record ConfigurationResourceChanges(
    ImmutableArray<string> AddedIds,
    ImmutableArray<string> RemovedIds,
    ImmutableArray<string> ChangedIds);

public sealed record ConfigurationComparison(
    string SnapshotId,
    string GuildId,
    string BaselineDigest,
    string CurrentDigest,
    bool Changed,
    ConfigurationResourceChanges Roles,
    ConfigurationResourceChanges Categories,
    ConfigurationResourceChanges Channels);

public interface IConfigurationSnapshotStore
{
    Task SaveSnapshotAsync(
        StoredConfigurationSnapshot snapshot,
        CancellationToken cancellationToken);

    Task<StoredConfigurationSnapshot?> GetSnapshotAsync(
        string snapshotId,
        CancellationToken cancellationToken);
}

internal static class ConfigurationSnapshotCanonicalizer
{
    internal static string Serialize(GuildConfigurationSnapshotView snapshot) =>
        JsonSerializer.Serialize(snapshot, OperationJson.SerializerOptions);

    internal static GuildConfigurationSnapshotView Deserialize(string json) =>
        JsonSerializer.Deserialize<GuildConfigurationSnapshotView>(json, OperationJson.SerializerOptions)
        ?? throw new InvalidOperationException("Stored configuration snapshot is invalid.");

    internal static string ComputeDigest(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
}
