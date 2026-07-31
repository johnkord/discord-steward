using System.Globalization;
using System.Collections.Immutable;
using DiscordSteward.Errors;
using DiscordSteward.Operations;
using global::Discord;
using global::Discord.Rest;

namespace DiscordSteward.Discord;

internal sealed class DiscordGuildWriter(DiscordRestSession session) : IDiscordGuildWriter
{
    public async Task<DiscordChannelWritePreflight> PreflightChannelUpdateAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        try
        {
            var guild = await session.GetGuildAsync(cancellationToken).ConfigureAwait(false);
            var channel = await guild.GetTextChannelAsync(
                ParseSnowflake(channelId),
                new RequestOptions { CancelToken = cancellationToken }).ConfigureAwait(false);
            if (channel is null || channel.GuildId != guild.Id)
            {
                throw ResourceNotFound();
            }

            var currentBot = await guild.GetCurrentUserAsync(
                new RequestOptions { CancelToken = cancellationToken }).ConfigureAwait(false);
            return new DiscordChannelWritePreflight(
                currentBot.GuildPermissions.ManageChannels,
                currentBot.GuildPermissions.Administrator,
                currentBot.GuildPermissions
                    .ToList()
                    .Select(permission => permission.ToString())
                    .Order(StringComparer.Ordinal)
                    .ToImmutableArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw DiscordErrorMapper.Map(exception, StewardErrorCodes.ResourceNotFound);
        }
    }

    public async Task UpdateTextChannelAsync(
        string planId,
        string channelId,
        ChannelMutableState desired,
        CancellationToken cancellationToken)
    {
        try
        {
            var guild = await session.GetGuildAsync(cancellationToken).ConfigureAwait(false);
            var channel = await guild.GetTextChannelAsync(
                ParseSnowflake(channelId),
                new RequestOptions { CancelToken = cancellationToken }).ConfigureAwait(false);
            if (channel is null || channel.GuildId != guild.Id)
            {
                throw ResourceNotFound();
            }

            await channel.ModifyAsync(
                properties =>
                {
                    properties.Name = desired.Name;
                    properties.Topic = TopicForDiscord(desired.Topic);
                    properties.SlowModeInterval = desired.SlowModeSeconds;
                },
                new RequestOptions
                {
                    AuditLogReason = $"Discord Steward {planId}",
                    CancelToken = cancellationToken
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw DiscordErrorMapper.Map(exception, StewardErrorCodes.ResourceNotFound);
        }
    }

    private static ulong ParseSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw ResourceNotFound();

    internal static string TopicForDiscord(string? topic) => topic ?? string.Empty;

    private static StewardException ResourceNotFound() => new(
        StewardErrorCodes.ResourceNotFound,
        "The requested channel was not found in the configured guild.");
}
