using DiscordSteward.Domain;

namespace DiscordSteward.Discord;

public interface IDiscordGuildReader
{
    Task<GuildSnapshot> GetGuildSnapshotAsync(CancellationToken cancellationToken);
}

public interface IDiscordReadiness
{
    DiscordReadinessSnapshot GetReadiness();
}

public sealed record DiscordReadinessSnapshot(
    string State,
    string? GuildId,
    string? BotUserId,
    string? LastErrorCode,
    DateTimeOffset? LastSuccessfulReadAt);
