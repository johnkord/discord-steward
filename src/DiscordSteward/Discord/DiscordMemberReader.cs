using System.Collections.Immutable;
using System.Globalization;
using DiscordSteward.Errors;
using global::Discord;
using global::Discord.Rest;

namespace DiscordSteward.Discord;

internal sealed class DiscordMemberReader(
    DiscordRestSession session,
    IDiscordHttpApi httpApi) : IDiscordMemberReader
{
    public async Task<MemberView> GetMemberAsync(
        string userId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var member = await guild.GetUserAsync(ParseSnowflake(userId), options).ConfigureAwait(false)
            ?? throw NotFound();
        var voiceState = await guild.GetUserVoiceStateAsync(member.Id, options).ConfigureAwait(false);
        return Normalize(member, voiceState);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<MemberPage> SearchMembersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 100 || query.Any(char.IsControl))
        {
            throw InvalidArgument("Member search query must contain 1 to 100 printable characters.");
        }

        ValidateLimit(limit);
        var members = await guild.SearchUsersAsync(query.Trim(), limit, options).ConfigureAwait(false);
        return CreatePage(members, limit);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<MemberPage> ListMembersAsync(
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        ValidateLimit(limit);
        var after = afterUserId is null ? 0UL : ParseSnowflake(afterUserId);
        var members = await CollectMembersAsync(guild, limit + 1, options, member => member.Id > after).ConfigureAwait(false);
        return CreatePage(
            members.OrderBy(member => member.Id),
            limit);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<MemberPage> ListMembersByRoleAsync(
        string roleId,
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        ValidateLimit(limit);
        var normalizedRoleId = ParseSnowflake(roleId);
        if (!guild.Roles.Any(role => role.Id == normalizedRoleId))
        {
            throw NotFound();
        }

        var after = afterUserId is null ? 0UL : ParseSnowflake(afterUserId);
        var members = await CollectMembersAsync(
            guild,
            limit + 1,
            options,
            member => member.Id > after && member.RoleIds.Contains(normalizedRoleId)).ConfigureAwait(false);
        return CreatePage(
            members.OrderBy(member => member.Id),
            limit);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<MemberVoiceStateView> GetMemberVoiceStateAsync(
        string userId,
        CancellationToken cancellationToken) => await ExecuteAsync(async (guild, options) =>
    {
        var id = ParseSnowflake(userId);
        var voiceState = await guild.GetUserVoiceStateAsync(id, options).ConfigureAwait(false);
        return NormalizeVoice(id, voiceState, isMuted: false, isDeafened: false);
    }, cancellationToken).ConfigureAwait(false);

    public async Task<ImmutableArray<RoleMemberCountView>> GetRoleMemberCountsAsync(
        CancellationToken cancellationToken)
    {
        var counts = await httpApi.GetRoleMemberCountsAsync(cancellationToken).ConfigureAwait(false);
        return counts
            .Select(pair => new RoleMemberCountView(pair.Key, pair.Value))
            .OrderBy(item => item.RoleId, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public async Task<MemberPrunePreview> PreviewMemberPruneAsync(
        int inactivityDays,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        if (roleIds.Count > 100)
        {
            throw InvalidArgument("Prune preview accepts at most 100 included role IDs.");
        }

        var normalizedRoleIds = roleIds
            .Select(ParseSnowflake)
            .Distinct()
            .Order()
            .Select(FormatId)
            .ToImmutableArray();
        var count = await httpApi.PreviewMemberPruneAsync(
            inactivityDays,
            normalizedRoleIds,
            cancellationToken).ConfigureAwait(false);
        return new MemberPrunePreview(
            inactivityDays,
            normalizedRoleIds,
            count,
            "This is a count-only preview. Steward does not expose Discord's dynamic-target prune write.");
    }

    private async Task<T> ExecuteAsync<T>(
        Func<RestGuild, RequestOptions, Task<T>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            var guild = await session.GetGuildAsync(cancellationToken).ConfigureAwait(false);
            var result = await read(guild, new RequestOptions { CancelToken = cancellationToken }).ConfigureAwait(false);
            session.ReportSuccessfulRead();
            return result;
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
            var mapped = DiscordErrorMapper.Map(exception, StewardErrorCodes.ResourceNotFound);
            session.ReportFailure(mapped.Code);
            throw mapped;
        }
    }

    private static MemberPage CreatePage(IEnumerable<RestGuildUser> members, int limit)
    {
        var selected = members.Take(limit + 1).ToArray();
        var page = selected.Take(limit).Select(member => Normalize(member, voiceState: null)).ToImmutableArray();
        return new MemberPage(
            page,
            selected.Length > limit && !page.IsEmpty ? page[^1].UserId : null);
    }

    private static MemberView Normalize(RestGuildUser member, IVoiceState? voiceState)
    {
        var normalized = new MemberView(
            FormatId(member.Id),
            Bound(member.Username, 100),
            Bound(member.DisplayName, 100),
            BoundOptional(member.Nickname, 100),
            member.RoleIds.Select(FormatId).Order(StringComparer.Ordinal).ToImmutableArray(),
            member.JoinedAt,
            member.TimedOutUntil,
            member.Flags.ToString(),
            (int)member.Flags,
            member.Hierarchy,
            member.IsPending,
            NormalizeVoice(member.Id, voiceState, member.IsMuted, member.IsDeafened));
        return normalized with { StateDigest = Operations.ResourceStateCanonicalizer.ComputeMemberDigest(normalized) };
    }

    private static MemberVoiceStateView NormalizeVoice(
        ulong userId,
        IVoiceState? voiceState,
        bool isMuted,
        bool isDeafened) => new(
            FormatId(userId),
            voiceState?.VoiceChannelId is ulong channelId ? FormatId(channelId) : null,
            voiceState?.IsMuted ?? isMuted,
            voiceState?.IsDeafened ?? isDeafened,
            voiceState?.IsSelfMuted ?? false,
            voiceState?.IsSelfDeafened ?? false,
            voiceState?.IsStreaming ?? false,
            voiceState?.IsVideoing ?? false,
            voiceState?.IsSuppressed ?? false,
            voiceState?.RequestToSpeakTimestamp);

    private static async Task<IReadOnlyList<RestGuildUser>> CollectMembersAsync(
        RestGuild guild,
        int maximum,
        RequestOptions options,
        Func<RestGuildUser, bool> include)
    {
        var members = new List<RestGuildUser>(maximum);
        await foreach (var page in guild.GetUsersAsync(options).ConfigureAwait(false))
        {
            foreach (var member in page)
            {
                if (include(member))
                {
                    members.Add(member);
                    if (members.Count == maximum)
                    {
                        return members;
                    }
                }
            }
        }

        return members;
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 1_000)
        {
            throw InvalidArgument("Member page limit must be between 1 and 1000.");
        }
    }

    private static ulong ParseSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw InvalidArgument("Discord ID must be a positive snowflake string.");

    private static string FormatId(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bound(string? value, int maximum) =>
        (value ?? string.Empty).Length <= maximum ? value ?? string.Empty : (value ?? string.Empty)[..maximum];

    private static string? BoundOptional(string? value, int maximum) => value is null ? null : Bound(value, maximum);

    private static StewardException NotFound() => new(
        StewardErrorCodes.ResourceNotFound,
        "The requested guild member or role was not found.");

    private static StewardException InvalidArgument(string message) => new(
        StewardErrorCodes.InvalidArgument,
        message);
}
