using System.Collections.Immutable;

namespace DiscordSteward.Discord;

public sealed record MemberVoiceStateView(
    string UserId,
    string? ChannelId,
    bool IsMuted,
    bool IsDeafened,
    bool IsSelfMuted,
    bool IsSelfDeafened,
    bool IsStreaming,
    bool IsVideoing,
    bool IsSuppressed,
    DateTimeOffset? RequestToSpeakTimestamp);

public sealed record MemberView(
    string UserId,
    string Username,
    string DisplayName,
    string? Nickname,
    ImmutableArray<string> RoleIds,
    DateTimeOffset? JoinedAt,
    DateTimeOffset? TimedOutUntil,
    string Flags,
    int FlagsValue,
    int Hierarchy,
    bool? IsPending,
    MemberVoiceStateView VoiceState,
    string StateDigest = "");

public sealed record MemberPage(
    ImmutableArray<MemberView> Items,
    string? NextAfterUserId);

public sealed record RoleMemberCountView(
    string RoleId,
    int MemberCount);

public sealed record MemberPrunePreview(
    int InactivityDays,
    ImmutableArray<string> IncludedRoleIds,
    int? EstimatedMemberCount,
    string Warning);

public interface IDiscordMemberReader
{
    Task<MemberView> GetMemberAsync(string userId, CancellationToken cancellationToken);

    Task<MemberPage> SearchMembersAsync(string query, int limit, CancellationToken cancellationToken);

    Task<MemberPage> ListMembersAsync(int limit, string? afterUserId, CancellationToken cancellationToken);

    Task<MemberPage> ListMembersByRoleAsync(
        string roleId,
        int limit,
        string? afterUserId,
        CancellationToken cancellationToken);

    Task<MemberVoiceStateView> GetMemberVoiceStateAsync(string userId, CancellationToken cancellationToken);

    Task<ImmutableArray<RoleMemberCountView>> GetRoleMemberCountsAsync(CancellationToken cancellationToken);

    Task<MemberPrunePreview> PreviewMemberPruneAsync(
        int inactivityDays,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken);
}
