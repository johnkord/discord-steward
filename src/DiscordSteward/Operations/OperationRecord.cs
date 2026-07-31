using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public static class OperationKinds
{
    public const string ChannelUpdate = "channel_update";
    public const string CreateTargetedInvite = "create_targeted_invite";
    public const string UpdateInviteTargetUsers = "update_invite_target_users";
}

public static class OperationStatuses
{
    public const string Reserved = "reserved";
    public const string Executing = "executing";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string PartialFailure = "partial_failure";
    public const string Unknown = "unknown";
}

public sealed record ChannelMutableState(
    string Name,
    string? Topic,
    int SlowModeSeconds,
    string? CategoryId);

public sealed record ChannelUpdateSet(
    string? Name,
    string? Topic,
    int? SlowModeSeconds,
    string? ChannelType = null,
    bool? IsNsfw = null,
    int? Bitrate = null,
    int? UserLimit = null,
    string? RtcRegion = null,
    int? VideoQualityMode = null,
    int? DefaultAutoArchiveMinutes = null,
    int? DefaultThreadSlowModeSeconds = null,
    ImmutableArray<ForumTagInput>? AvailableTags = null,
    DefaultReactionInput? DefaultReaction = null,
    int? DefaultSortOrder = null,
    int? DefaultForumLayout = null,
    bool? RequireTag = null,
    bool? HideMediaDownloadOptions = null,
    bool? IsSpoiler = null);

public sealed record ChannelUpdateInvocation(
    string RequestId,
    string ChannelId,
    string Reason,
    string ExpectedStateDigest,
    ChannelUpdateSet Set,
    ImmutableArray<string> Clear);

public sealed record OperationRecord(
    int SchemaVersion,
    string RequestId,
    string Kind,
    string GuildId,
    string ResourceId,
    string Reason,
    string Risk,
    string PolicyDigest,
    string ManifestDigest,
    string InvocationDigest,
    string InvocationJson,
    DateTimeOffset CreatedAt,
    string Status,
    DateTimeOffset? ExecutionStartedAt,
    DateTimeOffset? CompletedAt,
    string? Outcome,
    string? ErrorCode,
    string? BeforeStateJson,
    string? DesiredStateJson,
    string? ObservedStateJson,
    string? RequestMetadataJson = null);

public enum OperationReservationDisposition
{
    Reserved,
    Existing,
    Conflict
}

public sealed record OperationReservation(
    OperationReservationDisposition Disposition,
    OperationRecord Record);
