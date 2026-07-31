using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record RecurrenceNWeekdayInput(int Week, int Day);

public sealed record ScheduledEventRecurrenceInput(
    DateTimeOffset Start,
    string Frequency,
    int Interval,
    ImmutableArray<int> ByWeekday,
    ImmutableArray<RecurrenceNWeekdayInput> ByNWeekday,
    ImmutableArray<int> ByMonth,
    ImmutableArray<int> ByMonthDay);

public sealed record CreateScheduledEventInput(
    string ExpectedEventsStateDigest,
    string? ChannelId,
    string Name,
    string? Description,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    string EntityType,
    string? Location,
    string? CoverAssetId,
    ScheduledEventRecurrenceInput? Recurrence);

public sealed record ScheduledEventUpdateSet(
    string? ChannelId,
    string? Name,
    string? Description,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    string? EntityType,
    string? Location,
    string? CoverAssetId,
    ScheduledEventRecurrenceInput? Recurrence);

public sealed record UpdateScheduledEventInput(
    string ExpectedStateDigest,
    ScheduledEventUpdateSet Set,
    ImmutableArray<string> Clear);

public sealed record TransitionScheduledEventInput(string ExpectedStateDigest);

public sealed record DeleteScheduledEventInput(string ExpectedStateDigest);

public sealed record StartStageInstanceInput(
    string ExpectedChannelStateDigest,
    string Topic,
    string? ScheduledEventId,
    bool SendStartNotification);

public sealed record UpdateStageInstanceInput(
    string ExpectedStateDigest,
    string PrivacyLevel);

public sealed record EndStageInstanceInput(string ExpectedStateDigest);

public sealed record SetStageSpeakerInput(
    string ExpectedStageStateDigest,
    string UserId,
    string ExpectedMemberStateDigest,
    bool IsSpeaker);

public sealed record RequestStageSpeakingInput(
    string ExpectedStageStateDigest,
    bool Requesting);
