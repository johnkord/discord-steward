using DiscordSteward.Discord;
using DiscordSteward.Domain;

namespace DiscordSteward.Tests;

internal sealed class InMemoryDiscordGuildReader(GuildSnapshot snapshot) : IDiscordGuildReader
{
    public int ReadCount { get; private set; }

    public Task<GuildSnapshot> GetGuildSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        return Task.FromResult(snapshot);
    }
}
