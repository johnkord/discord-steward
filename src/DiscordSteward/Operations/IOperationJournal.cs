namespace DiscordSteward.Operations;

public interface IOperationJournal
{
    Task<OperationReservation> ReserveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken);

    Task<OperationRecord?> GetAsync(
        string requestId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OperationRecord>> ListAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<bool> FailPreflightAsync(
        string requestId,
        string invocationDigest,
        string outcome,
        string errorCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<bool> BeginExecutionAsync(
        string requestId,
        string invocationDigest,
        string beforeStateJson,
        string desiredStateJson,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<bool> CompleteExecutionAsync(
        string requestId,
        string status,
        string outcome,
        string? errorCode,
        string? observedStateJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<bool> ResolveUnknownAsync(
        string requestId,
        string status,
        string outcome,
        string? errorCode,
        string observedStateJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}
