using System.Collections.Immutable;
using System.Globalization;
using DiscordSteward.Errors;
using global::Discord;

namespace DiscordSteward.Discord;

internal sealed class DiscordBotInspector(DiscordRestSession session) : IDiscordBotInspector
{
    public async Task<BotEffectivePermissions> GetEffectivePermissionsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var guild = await session.GetGuildAsync(cancellationToken).ConfigureAwait(false);
            var currentBot = await guild.GetCurrentUserAsync(
                new RequestOptions { CancelToken = cancellationToken }).ConfigureAwait(false);
            var roleIds = currentBot.RoleIds.ToHashSet();
            var highestRole = guild.Roles
                .Where(role => roleIds.Contains(role.Id))
                .OrderByDescending(role => role.Position)
                .ThenByDescending(role => role.Id)
                .FirstOrDefault();
            var permissions = currentBot.GuildPermissions
                .ToList()
                .Select(permission => permission.ToString())
                .Order(StringComparer.Ordinal)
                .ToImmutableArray();

            return new BotEffectivePermissions(
                guild.Id.ToString(CultureInfo.InvariantCulture),
                currentBot.Id.ToString(CultureInfo.InvariantCulture),
                permissions,
                highestRole?.Id.ToString(CultureInfo.InvariantCulture),
                highestRole?.Name,
                highestRole?.Position,
                currentBot.GuildPermissions.Administrator);
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
            throw DiscordErrorMapper.Map(exception, StewardErrorCodes.DiscordUnavailable);
        }
    }
}
