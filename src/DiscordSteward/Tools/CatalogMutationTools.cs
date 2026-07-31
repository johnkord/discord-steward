using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tools;

public sealed class CatalogMutationTools(
    CatalogMutationOperationExecutor executor,
    StewardRuntimeSettings settings)
{
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> ExecuteForGuildAsync(
        string toolName,
        string requestId,
        string reason,
        object arguments,
        CancellationToken cancellationToken)
    {
        var guildId = settings.GuildId?.ToString(CultureInfo.InvariantCulture) ?? "unconfigured";
        return ExecuteAsync(toolName, requestId, guildId, reason, arguments, cancellationToken);
    }

    public async Task<OperationToolResultEnvelope<CatalogMutationResult>> ExecuteAsync(
        string toolName,
        string requestId,
        string resourceId,
        string reason,
        object arguments,
        CancellationToken cancellationToken)
    {
        var correlationId = NormalizeCorrelationId(requestId);
        try
        {
            var result = await executor.ExecuteAsync(
                CatalogMutationInvocation.Create(
                    requestId,
                    toolName,
                    resourceId,
                    reason,
                    arguments),
                cancellationToken).ConfigureAwait(false);
            return new OperationToolResultEnvelope<CatalogMutationResult>(
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
            return new OperationToolResultEnvelope<CatalogMutationResult>(
                1,
                correlationId,
                "failed",
                GuildReference(),
                Data: null,
                Warnings: [],
                new ToolError(exception.Code, exception.Message, correlationId));
        }
    }

    private GuildReference? GuildReference() => settings.GuildId.HasValue
        ? new GuildReference(settings.GuildId.Value.ToString(CultureInfo.InvariantCulture), Name: null)
        : null;

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
