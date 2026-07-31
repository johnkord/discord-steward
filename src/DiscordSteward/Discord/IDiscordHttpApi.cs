using System.Collections.Immutable;

namespace DiscordSteward.Discord;

public interface IDiscordHttpApi
{
    Task<ImmutableArray<ThreadView>> ListActiveThreadsAsync(CancellationToken cancellationToken);

    Task<ImmutableArray<GuildExpressionView>> ListGuildExpressionsAsync(
        string expressionType,
        CancellationToken cancellationToken);

    Task<ImmutableDictionary<string, int>> GetRoleMemberCountsAsync(
        CancellationToken cancellationToken);

    Task<int?> PreviewMemberPruneAsync(
        int inactivityDays,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken);
}
