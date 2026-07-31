using System.Collections.Immutable;
using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tools;

public sealed class DirectOperationTools(
    ChannelUpdateOperationExecutor executor,
    CatalogMutationOperationExecutor catalogExecutor,
    IOperationJournal journal,
    StewardRuntimeSettings settings)
{
    public async Task<OperationToolResultEnvelope<ChannelUpdateResult>> UpdateChannelAsync(
        ChannelUpdateInvocation invocation,
        CancellationToken cancellationToken)
    {
        var correlationId = NormalizeCorrelationId(invocation.RequestId);
        try
        {
            var result = await executor.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
            return OperationEnvelope(result, result.Status == OperationStatuses.Succeeded ? null : result.ErrorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException exception)
        {
            return OperationError<ChannelUpdateResult>(correlationId, exception);
        }
    }

    public Task<ToolResultEnvelope<OperationRecord>> GetOperationAsync(
        string requestId,
        CancellationToken cancellationToken) =>
        ExecuteReadAsync(async () =>
        {
            var normalized = NormalizeRequestId(requestId);
            return await journal.GetAsync(normalized, cancellationToken).ConfigureAwait(false)
                ?? throw new StewardException(
                    StewardErrorCodes.ResourceNotFound,
                    "The requested operation was not found.");
        }, cancellationToken);

    public Task<ToolResultEnvelope<OperationSummary[]>> ListOperationsAsync(
        int limit,
        CancellationToken cancellationToken) =>
        ExecuteReadAsync(async () =>
        {
            if (limit is < 1 or > 100)
            {
                throw new StewardException(
                    StewardErrorCodes.InvalidArgument,
                    "Operation list limit must be between 1 and 100.");
            }

            var records = await journal.ListAsync(limit, cancellationToken).ConfigureAwait(false);
            return records.Select(OperationSummary.FromRecord).ToArray();
        }, cancellationToken);

    public async Task<OperationToolResultEnvelope<OperationReconciliationResult>> ReconcileOperationAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        var correlationId = NormalizeCorrelationId(requestId);
        try
        {
            var normalized = NormalizeRequestId(requestId);
            var operation = await journal.GetAsync(normalized, cancellationToken).ConfigureAwait(false)
                ?? throw new StewardException(StewardErrorCodes.ResourceNotFound, "The requested operation was not found.");
            OperationReconciliationResult result;
            if (operation.Kind == OperationKinds.ChannelUpdate)
            {
                var channel = await executor.ReconcileAsync(normalized, cancellationToken).ConfigureAwait(false);
                result = new OperationReconciliationResult(
                    channel.RequestId,
                    OperationKinds.ChannelUpdate,
                    channel.ChannelId,
                    channel.Status,
                    channel.Outcome,
                    channel.Verified,
                    channel.ErrorCode);
            }
            else
            {
                var catalog = await catalogExecutor.ReconcileAsync(normalized, cancellationToken).ConfigureAwait(false);
                result = new OperationReconciliationResult(
                    catalog.RequestId,
                    catalog.ToolName,
                    catalog.ResourceId,
                    catalog.Status,
                    catalog.Outcome,
                    catalog.Verified,
                    catalog.ErrorCode);
            }

            return new OperationToolResultEnvelope<OperationReconciliationResult>(
                1,
                result.RequestId,
                result.Status,
                GuildReference(),
                result,
                Warnings: [],
                result.ErrorCode is null
                    ? null
                    : new ToolError(result.ErrorCode, SafeMessage(result.ErrorCode), result.RequestId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException exception)
        {
            return OperationError<OperationReconciliationResult>(correlationId, exception);
        }
    }

    private async Task<ToolResultEnvelope<T>> ExecuteReadAsync<T>(
        Func<Task<T>> read,
        CancellationToken cancellationToken)
        where T : class
    {
        var operationId = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        try
        {
            return new ToolResultEnvelope<T>(
                SchemaVersion: 1,
                operationId,
                Outcome: "ok",
                GuildReference(),
                await read().ConfigureAwait(false),
                Warnings: [],
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException exception)
        {
            return new ToolResultEnvelope<T>(
                SchemaVersion: 1,
                operationId,
                Outcome: "error",
                GuildReference(),
                Data: null,
                Warnings: [],
                new ToolError(exception.Code, exception.Message, operationId));
        }
        catch (Exception)
        {
            return new ToolResultEnvelope<T>(
                SchemaVersion: 1,
                operationId,
                Outcome: "error",
                GuildReference(),
                Data: null,
                Warnings: [],
                new ToolError(
                    StewardErrorCodes.AuditUnavailable,
                    "The operation journal is unavailable.",
                    operationId));
        }
    }

    private OperationToolResultEnvelope<ChannelUpdateResult> OperationEnvelope(
        ChannelUpdateResult result,
        string? errorCode) => new(
        SchemaVersion: 1,
        result.RequestId,
        Outcome: result.Status,
        GuildReference(),
        result,
        Warnings: [],
        errorCode is null
            ? null
            : new ToolError(errorCode, SafeMessage(errorCode), result.RequestId));

    private OperationToolResultEnvelope<T> OperationError<T>(
        string requestId,
        StewardException exception)
        where T : class => new(
            SchemaVersion: 1,
            requestId,
            Outcome: "failed",
            GuildReference(),
            Data: null,
            Warnings: [],
            new ToolError(exception.Code, exception.Message, requestId));

    private GuildReference? GuildReference() => settings.GuildId.HasValue
        ? new GuildReference(settings.GuildId.Value.ToString(CultureInfo.InvariantCulture), Name: null)
        : null;

    private static string NormalizeRequestId(string value)
    {
        try
        {
            return OperationCanonicalizer.NormalizeRequestId(value);
        }
        catch (ArgumentException)
        {
            throw new StewardException(
                StewardErrorCodes.InvalidArgument,
                "Request ID must be a non-empty UUID.");
        }
    }

    private static string NormalizeCorrelationId(string value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D", CultureInfo.InvariantCulture)
            : Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture);

    private static string SafeMessage(string errorCode) => errorCode switch
    {
        StewardErrorCodes.StateConflict => "Discord state did not match the approved call.",
        StewardErrorCodes.DiscordPermissionDenied => "The Steward bot lacks a required Discord permission.",
        StewardErrorCodes.OutcomeUnknown => "The Discord write outcome remains unknown; use reconcile_operation.",
        StewardErrorCodes.PartialFailure => "Discord state did not match either the before or desired state.",
        _ => "The Discord operation did not reach a verified successful outcome."
    };
}

public sealed record OperationSummary(
    string RequestId,
    string Kind,
    string ResourceId,
    string Risk,
    DateTimeOffset CreatedAt,
    string Status,
    DateTimeOffset? CompletedAt,
    string? Outcome,
    string? ErrorCode)
{
    public static OperationSummary FromRecord(OperationRecord record) => new(
        record.RequestId,
        record.Kind,
        record.ResourceId,
        record.Risk,
        record.CreatedAt,
        record.Status,
        record.CompletedAt,
        record.Outcome,
        record.ErrorCode);
}

public sealed record OperationReconciliationResult(
    string RequestId,
    string Kind,
    string ResourceId,
    string Status,
    string Outcome,
    bool Verified,
    string? ErrorCode);
