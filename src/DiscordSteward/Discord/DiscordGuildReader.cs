using DiscordSteward.Configuration;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using global::Discord;
using System.Text.Json;

namespace DiscordSteward.Discord;

internal sealed class DiscordGuildReader(
    DiscordRestSession session,
    DiscordApiTransport transport,
    StewardPolicy policy) : IDiscordGuildReader
{
    public async Task<GuildSnapshot> GetGuildSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var guild = await session.GetGuildAsync(cancellationToken).ConfigureAwait(false);
            var channelRoot = await transport.GetJsonAsync(
                $"guilds/{guild.Id}/channels",
                cancellationToken).ConfigureAwait(false);
            if (channelRoot.ValueKind != JsonValueKind.Array || channelRoot.GetArrayLength() > 500)
            {
                throw new StewardException(
                    StewardErrorCodes.DiscordUnavailable,
                    "Discord returned invalid guild channel state.");
            }

            var channels = channelRoot.EnumerateArray().Select(ChannelApiState.Parse).Select(state =>
            {
                if (!string.Equals(state.GuildId, guild.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    throw new StewardException(
                        StewardErrorCodes.GuildMismatch,
                        "Discord returned a channel outside the configured guild.");
                }

                return DiscordChannelStateReader.CreateExactSnapshot(state);
            }).ToArray();
            var snapshot = DiscordSnapshotNormalizer.Normalize(guild, channels, policy);
            session.ReportSuccessfulRead();
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException exception)
        {
            session.ReportFailure(exception.Code);
            throw;
        }
        catch (Exception exception)
        {
            var mapped = DiscordErrorMapper.Map(exception, StewardErrorCodes.GuildMismatch);
            session.ReportFailure(mapped.Code);
            throw mapped;
        }
    }
}
