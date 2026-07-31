using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record SetMemberNicknameInput(
    string ExpectedMemberStateDigest,
    string? Nickname);

public sealed record SetMemberTimeoutInput(
    string ExpectedMemberStateDigest,
    DateTimeOffset? TimedOutUntil);

public sealed record SetMemberVoiceChannelInput(
    string ExpectedMemberStateDigest,
    string? ChannelId);

public sealed record UpdateMemberVoiceStateInput(
    string ExpectedMemberStateDigest,
    bool? Muted,
    bool? Deafened);

public sealed record SetMemberVerificationBypassInput(
    string ExpectedMemberStateDigest,
    bool BypassesVerification);

public sealed record KickMemberInput(
    string ExpectedMemberStateDigest);

public sealed record BanMemberInput(
    string? ExpectedMemberStateDigest);

public sealed record BulkBanTargetInput(
    string UserId,
    string? ExpectedMemberStateDigest);

public sealed record BulkBanMembersInput(
    ImmutableArray<BulkBanTargetInput> Targets);

public sealed record UnbanMemberInput(
    string ExpectedBanStateDigest);
