using DiscordSteward.Operations;
using System.Collections.Immutable;

namespace DiscordSteward.Discord;

public interface IDiscordGuildWriter
{
    Task<DiscordChannelWritePreflight> PreflightChannelUpdateAsync(
        string channelId,
        CancellationToken cancellationToken);

    Task UpdateTextChannelAsync(
        string planId,
        string channelId,
        ChannelMutableState desired,
        CancellationToken cancellationToken);
}

public sealed record DiscordChannelWritePreflight(
    bool HasManageChannels,
    bool HasAdministrator,
    ImmutableArray<string> EffectivePermissions = default);
