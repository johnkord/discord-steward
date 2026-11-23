using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

public sealed record CatalogMutationInvocation(
    string RequestId,
    string ToolName,
    string ResourceId,
    string Reason,
    JsonElement Arguments)
{
    public static CatalogMutationInvocation Create(
        string requestId,
        string toolName,
        string resourceId,
        string reason,
        object arguments) => new(
            requestId,
            toolName,
            resourceId,
            reason,
            JsonSerializer.SerializeToElement(arguments, CatalogJson.Options));
}

public sealed record CatalogMutationPreparation(
    string BeforeStateJson,
    string DesiredStateJson);

public sealed record CatalogMutationWriteReceipt(string? ReceiptJson)
{
    internal static JsonDocument OpenForObservation(string? receiptJson) => receiptJson is null
        ? throw new StewardException(StewardErrorCodes.OutcomeUnknown, "No exact write receipt is available for verification.")
        : JsonDocument.Parse(receiptJson);
}

public enum CatalogMutationObservationKind
{
    DesiredState,
    AcceptedUnverifiable,
    BeforeState,
    UnexpectedState
}

public sealed record CatalogMutationObservation(
    CatalogMutationObservationKind Kind,
    string ObservedStateJson);

public sealed record CatalogMutationResult(
    string RequestId,
    string InvocationDigest,
    string ToolName,
    string ResourceId,
    string Status,
    string Outcome,
    bool Verified,
    JsonElement? Before,
    JsonElement? Desired,
    JsonElement? Observed,
    string? ErrorCode);

public interface ICatalogMutationAdapter
{
    bool Supports(string toolName);

    Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken);

    Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken);

    Task<CatalogMutationObservation> ObserveAsync(
        CatalogMutationInvocation invocation,
        string beforeStateJson,
        string desiredStateJson,
        string? priorReceiptJson,
        CancellationToken cancellationToken);
}

public sealed class CatalogMutationOperationExecutor(
    ICatalogMutationAdapter adapter,
    IDiscordBotInspector botInspector,
    IOperationJournal journal,
    StewardPolicy policy,
    StewardToolManifest manifest,
    StewardRuntimeSettings settings,
    TimeProvider timeProvider,
    StewardRequestMetadataContext? requestMetadata = null)
{
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReconciliationDelay = TimeSpan.FromMinutes(2);

    public async Task<CatalogMutationResult> ExecuteAsync(
        CatalogMutationInvocation request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var invocation = NormalizeAndValidate(request);
        if (!manifest.IsRegistered(invocation.ToolName))
        {
            throw new StewardException(
                StewardErrorCodes.ToolNotEnabled,
                $"The {invocation.ToolName} tool is not enabled in the startup manifest.");
        }

        if (!adapter.Supports(invocation.ToolName))
        {
            throw new StewardException(
                StewardErrorCodes.ToolNotEnabled,
                "The requested mutation has no installed typed Discord adapter.");
        }

        var invocationJson = OperationCanonicalizer.Serialize(invocation);
        var invocationDigest = OperationCanonicalizer.ComputeDigest(invocationJson);
        var now = timeProvider.GetUtcNow();
        var operation = new OperationRecord(
            SchemaVersion: 1,
            invocation.RequestId,
            Kind: invocation.ToolName,
            GuildId: settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture),
            ResourceId: invocation.ResourceId,
            invocation.Reason,
            Risk: StewardToolManifest.GetRisk(invocation.ToolName).ToString(),
            PolicyDigest: policy.Digest,
            ManifestDigest: manifest.Digest,
            invocationDigest,
            invocationJson,
            CreatedAt: now,
            Status: OperationStatuses.Reserved,
            ExecutionStartedAt: null,
            CompletedAt: null,
            Outcome: null,
            ErrorCode: null,
            BeforeStateJson: null,
            DesiredStateJson: null,
            ObservedStateJson: null,
            RequestMetadataJson: requestMetadata?.CurrentJson);

        var reservation = await ReserveAsync(operation, cancellationToken).ConfigureAwait(false);
        if (reservation.Disposition == OperationReservationDisposition.Conflict)
        {
            throw new StewardException(
                StewardErrorCodes.IdempotencyConflict,
                "The request ID is already bound to a different canonical invocation.");
        }

        if (reservation.Disposition == OperationReservationDisposition.Existing)
        {
            if (IsTerminal(reservation.Record.Status))
            {
                return ToResult(reservation.Record);
            }

            if (reservation.Record.Status == OperationStatuses.Executing)
            {
                throw InProgress();
            }

            if (!string.Equals(reservation.Record.PolicyDigest, policy.Digest, StringComparison.Ordinal) ||
                !string.Equals(reservation.Record.ManifestDigest, manifest.Digest, StringComparison.Ordinal))
            {
                await FailPreflightAsync(
                    reservation.Record,
                    "execution_context_changed",
                    StewardErrorCodes.PolicyDenied,
                    cancellationToken).ConfigureAwait(false);
                throw PolicyDenied("Policy or startup manifest changed after the operation was reserved.");
            }

            operation = reservation.Record;
        }

        CatalogMutationPreparation preparation;
        try
        {
            await ValidateBotPreflightAsync(invocation.ToolName, cancellationToken).ConfigureAwait(false);
            if (!policy.IsUnrestricted && policy.ProtectedResourceIds.Contains(invocation.ResourceId))
            {
                throw PolicyDenied("Steward policy protects the requested resource from mutation.");
            }

            preparation = await adapter.PrepareAsync(invocation, cancellationToken).ConfigureAwait(false);
            ValidateStateJson(preparation.BeforeStateJson, "before");
            ValidateStateJson(preparation.DesiredStateJson, "desired");
            if (string.Equals(preparation.BeforeStateJson, preparation.DesiredStateJson, StringComparison.Ordinal))
            {
                throw new StewardException(
                    StewardErrorCodes.StateConflict,
                    "The requested mutation already matches current Discord state.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException exception)
        {
            await FailPreflightAsync(
                operation,
                "preflight_failed",
                exception.Code,
                cancellationToken).ConfigureAwait(false);
            throw;
        }

        bool claimed;
        try
        {
            claimed = await journal.BeginExecutionAsync(
                operation.RequestId,
                operation.InvocationDigest,
                preparation.BeforeStateJson,
                preparation.DesiredStateJson,
                timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw AuditUnavailable("The execution claim could not be persisted; no Discord write was attempted.");
        }

        if (!claimed)
        {
            var existing = await GetStoredAsync(operation.RequestId, cancellationToken).ConfigureAwait(false);
            if (existing is not null && IsTerminal(existing.Status))
            {
                return ToResult(existing);
            }

            throw InProgress();
        }

        string? writeErrorCode = null;
        CatalogMutationWriteReceipt receipt = new(ReceiptJson: null);
        try
        {
            receipt = await adapter.WriteAsync(invocation, preparation, cancellationToken).ConfigureAwait(false);
            if (receipt.ReceiptJson is not null)
            {
                ValidateStateJson(receipt.ReceiptJson, "write receipt");
                receipt = new CatalogMutationWriteReceipt(SanitizeReceipt(receipt.ReceiptJson));
            }
        }
        catch (OperationCanceledException)
        {
            writeErrorCode = StewardErrorCodes.DiscordUnavailable;
        }
        catch (StewardException exception)
        {
            writeErrorCode = exception.Code;
        }
        catch (Exception)
        {
            writeErrorCode = StewardErrorCodes.DiscordUnavailable;
        }

        return await VerifyAfterWriteAsync(
            operation,
            invocation,
            preparation,
            receipt.ReceiptJson,
            writeErrorCode).ConfigureAwait(false);
    }

    public async Task<CatalogMutationResult> ReconcileAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        var normalizedRequestId = NormalizeRequestId(requestId);
        var operation = await GetStoredAsync(normalizedRequestId, cancellationToken).ConfigureAwait(false)
            ?? throw new StewardException(
                StewardErrorCodes.ResourceNotFound,
                "The requested operation was not found.");
        if (!adapter.Supports(operation.Kind))
        {
            throw InvalidArgument("The operation kind does not use catalog reconciliation.");
        }

        if (operation.Status is OperationStatuses.Succeeded or
            OperationStatuses.Failed or
            OperationStatuses.PartialFailure)
        {
            return ToResult(operation);
        }

        var now = timeProvider.GetUtcNow();
        if (operation.Status == OperationStatuses.Reserved)
        {
            if (now - operation.CreatedAt < ReconciliationDelay)
            {
                throw InProgress();
            }

            await FailPreflightAsync(
                operation,
                "verified_not_attempted",
                StewardErrorCodes.StateConflict,
                cancellationToken).ConfigureAwait(false);
            return ToResult(await GetStoredAsync(normalizedRequestId, cancellationToken).ConfigureAwait(false)
                ?? throw AuditUnavailable("The reconciled operation could not be read back."));
        }

        if (operation.Status == OperationStatuses.Executing &&
            (!operation.ExecutionStartedAt.HasValue || now - operation.ExecutionStartedAt.Value < ReconciliationDelay))
        {
            throw InProgress();
        }

        if (operation.Status is not OperationStatuses.Executing and not OperationStatuses.Unknown)
        {
            throw new StewardException(
                StewardErrorCodes.StateConflict,
                "The operation is not in a reconcilable state.");
        }

        var invocation = OperationCanonicalizer.DeserializeCatalogMutation(operation.InvocationJson);
        var before = operation.BeforeStateJson
            ?? throw AuditUnavailable("The operation has no durable before-state evidence.");
        var desired = operation.DesiredStateJson
            ?? throw AuditUnavailable("The operation has no durable desired-state evidence.");
        CatalogMutationObservation observation;
        try
        {
            observation = await adapter.ObserveAsync(
                invocation, before, desired, operation.ObservedStateJson, cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.OutcomeUnknown)
        {
            return operation.Status == OperationStatuses.Unknown
                ? ToResult(operation)
                : await RecordUnknownAsync(operation, new CatalogMutationPreparation(before, desired), operation.ObservedStateJson).ConfigureAwait(false);
        }
        return await PersistReconciliationAsync(operation, observation, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateBotPreflightAsync(
        string toolName,
        CancellationToken cancellationToken)
    {
        var observed = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var configuredGuildId = settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(observed.GuildId, configuredGuildId, StringComparison.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.GuildMismatch,
                "Discord returned permissions for a guild other than the configured guild.");
        }

        if (!policy.IsUnrestricted && observed.HasAdministrator)
        {
            throw PolicyDenied("The Steward bot has Administrator; remove it before enabling mutations.");
        }

        var actual = observed.EffectivePermissions.ToImmutableHashSet(StringComparer.Ordinal);
        var deniedHeldPermissions = policy.IsUnrestricted
            ? []
            : actual
                .Where(policy.DeniedPermissions.Contains)
                .Order(StringComparer.Ordinal)
                .ToArray();
        if (deniedHeldPermissions.Length > 0)
        {
            throw PolicyDenied(
                $"The Steward bot holds permissions denied by local policy: {string.Join(", ", deniedHeldPermissions)}.");
        }

        var missing = StewardToolManifest.GetRequiredDiscordPermissions(toolName)
            .Where(permission => !permission.EndsWith("Intent", StringComparison.Ordinal))
            .Where(permission => !observed.HasPermission(permission))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new StewardException(
                StewardErrorCodes.DiscordPermissionDenied,
                $"The Steward bot lacks required permissions: {string.Join(", ", missing)}.");
        }
    }

    private async Task<CatalogMutationResult> VerifyAfterWriteAsync(
        OperationRecord operation,
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        string? receiptJson,
        string? writeErrorCode)
    {
        if (IsDefinitiveWriteRejection(writeErrorCode))
        {
            await CompleteAsync(
                operation.RequestId,
                OperationStatuses.Failed,
                "write_rejected",
                writeErrorCode,
                preparation.BeforeStateJson,
                CancellationToken.None).ConfigureAwait(false);
            return Result(
                operation,
                OperationStatuses.Failed,
                "write_rejected",
                preparation.BeforeStateJson,
                preparation.DesiredStateJson,
                preparation.BeforeStateJson,
                writeErrorCode);
        }

        using var timeout = new CancellationTokenSource(VerificationTimeout);
        try
        {
            var observation = await adapter.ObserveAsync(
                invocation,
                preparation.BeforeStateJson,
                preparation.DesiredStateJson,
                receiptJson,
                timeout.Token).ConfigureAwait(false);
            var mapped = MapObservation(observation, writeErrorCode);
            await CompleteAsync(
                operation.RequestId,
                mapped.Status,
                mapped.Outcome,
                mapped.ErrorCode,
                observation.ObservedStateJson,
                timeout.Token).ConfigureAwait(false);
            return Result(
                operation,
                mapped.Status,
                mapped.Outcome,
                preparation.BeforeStateJson,
                preparation.DesiredStateJson,
                observation.ObservedStateJson,
                mapped.ErrorCode);
        }
        catch (StewardException exception) when (exception.Code != StewardErrorCodes.AuditUnavailable)
        {
            return await RecordUnknownAsync(operation, preparation, receiptJson).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await RecordUnknownAsync(operation, preparation, receiptJson).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await RecordUnknownAsync(operation, preparation, receiptJson).ConfigureAwait(false);
        }
    }

    private static bool IsDefinitiveWriteRejection(string? errorCode) => errorCode is
        StewardErrorCodes.InvalidArgument or
        StewardErrorCodes.NotConfigured or
        StewardErrorCodes.DiscordAuthenticationFailed or
        StewardErrorCodes.DiscordPermissionDenied or
        StewardErrorCodes.ResourceNotFound or
        StewardErrorCodes.RateLimited or
        StewardErrorCodes.PolicyDenied or
        StewardErrorCodes.StateConflict;

    private async Task<CatalogMutationResult> RecordUnknownAsync(
        OperationRecord operation,
        CatalogMutationPreparation preparation,
        string? receiptJson)
    {
        await CompleteAsync(
            operation.RequestId,
            OperationStatuses.Unknown,
            "write_outcome_unresolved",
            StewardErrorCodes.OutcomeUnknown,
            receiptJson,
            CancellationToken.None).ConfigureAwait(false);
        return Result(
            operation,
            OperationStatuses.Unknown,
            "write_outcome_unresolved",
            preparation.BeforeStateJson,
            preparation.DesiredStateJson,
            receiptJson,
            StewardErrorCodes.OutcomeUnknown);
    }

    private async Task<CatalogMutationResult> PersistReconciliationAsync(
        OperationRecord operation,
        CatalogMutationObservation observation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var mapped = MapObservation(observation, writeErrorCode: null, reconciliation: true);
        bool resolved;
        try
        {
            resolved = operation.Status == OperationStatuses.Unknown
                ? await journal.ResolveUnknownAsync(
                    operation.RequestId,
                    mapped.Status,
                    mapped.Outcome,
                    mapped.ErrorCode,
                    observation.ObservedStateJson,
                    now,
                    cancellationToken).ConfigureAwait(false)
                : await journal.CompleteExecutionAsync(
                    operation.RequestId,
                    mapped.Status,
                    mapped.Outcome,
                    mapped.ErrorCode,
                    observation.ObservedStateJson,
                    now,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw AuditUnavailable("The reconciled outcome could not be persisted.");
        }

        if (!resolved)
        {
            throw AuditUnavailable("The operation changed state before reconciliation completed.");
        }

        return Result(
            operation,
            mapped.Status,
            mapped.Outcome,
            operation.BeforeStateJson,
            operation.DesiredStateJson,
            observation.ObservedStateJson,
            mapped.ErrorCode);
    }

    private static ObservationMapping MapObservation(
        CatalogMutationObservation observation,
        string? writeErrorCode,
        bool reconciliation = false) => observation.Kind switch
        {
            CatalogMutationObservationKind.DesiredState => new ObservationMapping(
                OperationStatuses.Succeeded,
                reconciliation
                    ? "reconciled_verified_desired_state"
                    : writeErrorCode is null ? "verified_desired_state" : "recovered_verified_desired_state",
                ErrorCode: null),
            CatalogMutationObservationKind.AcceptedUnverifiable => new ObservationMapping(
                OperationStatuses.Succeeded,
                reconciliation ? "reconciled_accepted_unverifiable" : "accepted_unverifiable",
                ErrorCode: null),
            CatalogMutationObservationKind.BeforeState => new ObservationMapping(
                OperationStatuses.Failed,
                reconciliation ? "reconciled_verified_no_change" : "verified_no_change",
                writeErrorCode ?? StewardErrorCodes.StateConflict),
            _ => new ObservationMapping(
                OperationStatuses.PartialFailure,
                reconciliation ? "reconciled_unexpected_state" : "observed_unexpected_state",
                StewardErrorCodes.PartialFailure)
        };

    private async Task<OperationReservation> ReserveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await journal.ReserveAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw AuditUnavailable("The operation could not be reserved durably; no Discord write was attempted.");
        }
    }

    private async Task FailPreflightAsync(
        OperationRecord operation,
        string outcome,
        string errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await journal.FailPreflightAsync(
                    operation.RequestId,
                    operation.InvocationDigest,
                    outcome,
                    errorCode,
                    timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false))
            {
                throw AuditUnavailable("The failed preflight could not be recorded durably.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException)
        {
            throw;
        }
        catch (Exception)
        {
            throw AuditUnavailable("The failed preflight could not be persisted.");
        }
    }

    private async Task CompleteAsync(
        string requestId,
        string status,
        string outcome,
        string? errorCode,
        string? observedStateJson,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await journal.CompleteExecutionAsync(
                    requestId,
                    status,
                    outcome,
                    errorCode,
                    observedStateJson,
                    timeProvider.GetUtcNow(),
                    cancellationToken).ConfigureAwait(false))
            {
                throw AuditUnavailable("The operation outcome could not be recorded durably.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException)
        {
            throw;
        }
        catch (Exception)
        {
            throw AuditUnavailable("The operation outcome could not be persisted.");
        }
    }

    private async Task<OperationRecord?> GetStoredAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await journal.GetAsync(requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw AuditUnavailable("The operation journal is unavailable.");
        }
    }

    private static CatalogMutationInvocation NormalizeAndValidate(CatalogMutationInvocation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestId = NormalizeRequestId(request.RequestId);
        var toolName = request.ToolName?.Trim() ?? string.Empty;
        if (toolName.Length is < 1 or > 100 ||
            toolName.Any(character => !(character is >= 'a' and <= 'z' || char.IsAsciiDigit(character) || character == '_')))
        {
            throw InvalidArgument("Mutation tool name is invalid.");
        }

        var resourceId = request.ResourceId?.Trim() ?? string.Empty;
        if (resourceId.Length is < 1 or > 200 || resourceId.Any(char.IsControl))
        {
            throw InvalidArgument("Mutation resource ID is invalid.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > 500 || reason.Any(character => char.IsControl(character) && character is not '\t'))
        {
            throw InvalidArgument("Operation reason must contain 1 to 500 printable characters.");
        }

        if (request.Arguments.ValueKind != JsonValueKind.Object)
        {
            throw InvalidArgument("Mutation arguments must be a typed JSON object.");
        }

        return request with
        {
            RequestId = requestId,
            ToolName = toolName,
            ResourceId = resourceId,
            Reason = reason,
            Arguments = request.Arguments.Clone()
        };
    }

    private void EnsureConfigured()
    {
        if (!settings.IsConfigured)
        {
            throw new StewardException(
                StewardErrorCodes.NotConfigured,
                "Discord writes require a valid configured guild ID and bot token.");
        }
    }

    private static void ValidateStateJson(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1_048_576)
        {
            throw AuditUnavailable($"The {label} state is empty or exceeds the journal limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind is JsonValueKind.Undefined)
            {
                throw new JsonException();
            }
        }
        catch (JsonException)
        {
            throw AuditUnavailable($"The {label} state is not valid JSON.");
        }
    }

    private static string SanitizeReceipt(string receiptJson)
    {
        using var document = JsonDocument.Parse(receiptJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return "{}";
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var name in new[] { "id", "sound_id", "webhook_id", "channel_id", "code", "vault_id" })
            {
                if (!document.RootElement.TryGetProperty(name, out var value) ||
                    value.ValueKind is not JsonValueKind.String and not JsonValueKind.Number)
                {
                    continue;
                }

                writer.WritePropertyName(name);
                value.WriteTo(writer);
            }

            if (document.RootElement.TryGetProperty("accepted", out var accepted) &&
                accepted.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                writer.WriteBoolean("accepted", accepted.GetBoolean());
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static CatalogMutationResult ToResult(OperationRecord operation) => Result(
        operation,
        operation.Status,
        operation.Outcome ?? operation.Status,
        operation.BeforeStateJson,
        operation.DesiredStateJson,
        operation.ObservedStateJson,
        operation.ErrorCode);

    private static CatalogMutationResult Result(
        OperationRecord operation,
        string status,
        string outcome,
        string? beforeJson,
        string? desiredJson,
        string? observedJson,
        string? errorCode) => new(
            operation.RequestId,
            operation.InvocationDigest,
            operation.Kind,
            operation.ResourceId,
            status,
            outcome,
            status switch
            {
                OperationStatuses.Succeeded => outcome is "verified_desired_state" or "recovered_verified_desired_state" or "reconciled_verified_desired_state",
                OperationStatuses.Failed => outcome is "write_rejected" or "verified_no_change" or "reconciled_verified_no_change",
                _ => false
            },
            ParseState(beforeJson),
            ParseState(desiredJson),
            ParseState(observedJson),
            errorCode);

    private static JsonElement? ParseState(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string NormalizeRequestId(string value)
    {
        try
        {
            return OperationCanonicalizer.NormalizeRequestId(value);
        }
        catch (ArgumentException)
        {
            throw InvalidArgument("Request ID must be a non-empty UUID.");
        }
    }

    private static bool IsTerminal(string status) => status is
        OperationStatuses.Succeeded or
        OperationStatuses.Failed or
        OperationStatuses.PartialFailure or
        OperationStatuses.Unknown;

    private static StewardException InvalidArgument(string message) =>
        new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException InProgress() =>
        new(StewardErrorCodes.OperationInProgress, "The operation is already being executed.");

    private static StewardException PolicyDenied(string message) =>
        new(StewardErrorCodes.PolicyDenied, message);

    private static StewardException AuditUnavailable(string message) =>
        new(StewardErrorCodes.AuditUnavailable, message);

    private sealed record ObservationMapping(
        string Status,
        string Outcome,
        string? ErrorCode);
}

internal static class CatalogJson
{
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
