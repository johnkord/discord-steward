using System.Collections.Immutable;
using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

public sealed record ChannelUpdateResult(
    string RequestId,
    string InvocationDigest,
    string Status,
    string Outcome,
    string ChannelId,
    bool Verified,
    ChannelMutableState? Before,
    ChannelMutableState? Desired,
    ChannelMutableState? Observed,
    string? ErrorCode);

public sealed class ChannelUpdateOperationExecutor(
    IDiscordGuildReader guildReader,
    IDiscordGuildWriter guildWriter,
    IOperationJournal journal,
    StewardPolicy policy,
    StewardToolManifest manifest,
    StewardRuntimeSettings settings,
    TimeProvider timeProvider,
    StewardRequestMetadataContext? requestMetadata = null)
{
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReconciliationDelay = TimeSpan.FromMinutes(2);

    public async Task<ChannelUpdateResult> ExecuteAsync(
        ChannelUpdateInvocation request,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            throw new StewardException(
                StewardErrorCodes.NotConfigured,
                "Discord writes require a valid configured guild ID and bot token.");
        }

        if (!manifest.IsRegistered("update_channel"))
        {
            throw new StewardException(
                StewardErrorCodes.ToolNotEnabled,
                "The update_channel tool is not enabled in the startup manifest.");
        }

        var invocation = NormalizeAndValidate(request);
        var invocationJson = OperationCanonicalizer.Serialize(invocation);
        var invocationDigest = OperationCanonicalizer.ComputeDigest(invocationJson);
        var now = timeProvider.GetUtcNow();
        var operation = new OperationRecord(
            SchemaVersion: 1,
            invocation.RequestId,
            Kind: OperationKinds.ChannelUpdate,
            GuildId: settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture),
            ResourceId: invocation.ChannelId,
            invocation.Reason,
            Risk: "R3",
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

        ChannelMutableState before;
        ChannelMutableState desired;
        try
        {
            var channel = await GetChannelAsync(invocation.ChannelId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(channel.Kind, "text", StringComparison.Ordinal))
            {
                throw PolicyDenied("The initial direct mutation slice supports standard text channels only.");
            }

            if (!policy.IsUnrestricted && policy.GetProtection(channel.Id, channel.Name).IsProtected)
            {
                throw PolicyDenied("Steward policy protects the requested channel from mutation.");
            }

            if (!string.Equals(
                    ChannelStateCanonicalizer.ComputeDigest(channel),
                    invocation.ExpectedStateDigest,
                    StringComparison.Ordinal))
            {
                throw new StewardException(
                    StewardErrorCodes.StateConflict,
                    "The channel state does not match the approved precondition digest.");
            }

            before = ChannelStateCanonicalizer.ToMutableState(channel);
            desired = ApplyDesiredState(before, invocation);
            if (desired == before)
            {
                throw new StewardException(
                    StewardErrorCodes.StateConflict,
                    "The requested update already matches current channel state.");
            }

            var preflight = await guildWriter.PreflightChannelUpdateAsync(
                channel.Id,
                cancellationToken).ConfigureAwait(false);
            if (!policy.IsUnrestricted && preflight.HasAdministrator)
            {
                throw PolicyDenied("The Steward bot has Administrator; remove it before enabling mutations.");
            }

            var deniedHeldPermissions = policy.IsUnrestricted || preflight.EffectivePermissions.IsDefault
                ? []
                : preflight.EffectivePermissions
                    .Where(policy.DeniedPermissions.Contains)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            if (deniedHeldPermissions.Length > 0)
            {
                throw PolicyDenied(
                    $"The Steward bot holds permissions denied by local policy: {string.Join(", ", deniedHeldPermissions)}.");
            }

            if (!preflight.HasManageChannels && !preflight.HasAdministrator)
            {
                throw new StewardException(
                    StewardErrorCodes.DiscordPermissionDenied,
                    "The Steward bot lacks Manage Channels; no Discord write was attempted.");
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
                ChannelStateCanonicalizer.SerializeState(before),
                ChannelStateCanonicalizer.SerializeState(desired),
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
        try
        {
            await guildWriter.UpdateTextChannelAsync(
                operation.RequestId,
                operation.ResourceId,
                desired,
                cancellationToken).ConfigureAwait(false);
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
            before,
            desired,
            writeErrorCode).ConfigureAwait(false);
    }

    public async Task<ChannelUpdateResult> ReconcileAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        var normalizedRequestId = NormalizeRequestId(requestId);
        var operation = await GetStoredAsync(normalizedRequestId, cancellationToken).ConfigureAwait(false)
            ?? throw new StewardException(
                StewardErrorCodes.ResourceNotFound,
                "The requested operation was not found.");
        if (!string.Equals(operation.Kind, OperationKinds.ChannelUpdate, StringComparison.Ordinal))
        {
            throw InvalidArgument("The operation kind does not use channel reconciliation.");
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
            (!operation.ExecutionStartedAt.HasValue ||
             now - operation.ExecutionStartedAt.Value < ReconciliationDelay))
        {
            throw InProgress();
        }

        if (operation.Status is not OperationStatuses.Executing and not OperationStatuses.Unknown)
        {
            throw new StewardException(
                StewardErrorCodes.StateConflict,
                "The operation is not in a reconcilable state.");
        }

        var before = DeserializeState(operation.BeforeStateJson)
            ?? throw AuditUnavailable("The operation has no durable before-state evidence.");
        var desired = DeserializeState(operation.DesiredStateJson)
            ?? throw AuditUnavailable("The operation has no durable desired-state evidence.");
        var observedChannel = await GetChannelAsync(operation.ResourceId, cancellationToken).ConfigureAwait(false);
        var observed = ChannelStateCanonicalizer.ToMutableState(observedChannel);

        string status;
        string outcome;
        string? errorCode;
        bool verified;
        if (observed == desired)
        {
            status = OperationStatuses.Succeeded;
            outcome = "reconciled_verified_desired_state";
            errorCode = null;
            verified = true;
        }
        else if (observed == before)
        {
            status = OperationStatuses.Failed;
            outcome = "reconciled_verified_no_change";
            errorCode = StewardErrorCodes.StateConflict;
            verified = true;
        }
        else
        {
            status = OperationStatuses.PartialFailure;
            outcome = "reconciled_unexpected_state";
            errorCode = StewardErrorCodes.PartialFailure;
            verified = false;
        }

        var observedJson = ChannelStateCanonicalizer.SerializeState(observed);
        bool resolved;
        try
        {
            resolved = operation.Status == OperationStatuses.Unknown
                ? await journal.ResolveUnknownAsync(
                    operation.RequestId,
                    status,
                    outcome,
                    errorCode,
                    observedJson,
                    now,
                    cancellationToken).ConfigureAwait(false)
                : await journal.CompleteExecutionAsync(
                    operation.RequestId,
                    status,
                    outcome,
                    errorCode,
                    observedJson,
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

        return new ChannelUpdateResult(
            operation.RequestId,
            operation.InvocationDigest,
            status,
            outcome,
            operation.ResourceId,
            verified,
            before,
            desired,
            observed,
            errorCode);
    }

    private async Task<ChannelUpdateResult> VerifyAfterWriteAsync(
        OperationRecord operation,
        ChannelMutableState before,
        ChannelMutableState desired,
        string? writeErrorCode)
    {
        using var timeout = new CancellationTokenSource(VerificationTimeout);
        try
        {
            var observedChannel = await GetChannelAsync(
                operation.ResourceId,
                timeout.Token).ConfigureAwait(false);
            var observed = ChannelStateCanonicalizer.ToMutableState(observedChannel);

            string status;
            string outcome;
            string? errorCode;
            bool verified;
            if (observed == desired)
            {
                status = OperationStatuses.Succeeded;
                outcome = writeErrorCode is null
                    ? "verified_desired_state"
                    : "recovered_verified_desired_state";
                errorCode = null;
                verified = true;
            }
            else if (observed == before)
            {
                status = OperationStatuses.Failed;
                outcome = "verified_no_change";
                errorCode = writeErrorCode ?? StewardErrorCodes.StateConflict;
                verified = true;
            }
            else
            {
                status = OperationStatuses.PartialFailure;
                outcome = "observed_unexpected_state";
                errorCode = StewardErrorCodes.PartialFailure;
                verified = false;
            }

            await CompleteAsync(
                operation.RequestId,
                status,
                outcome,
                errorCode,
                ChannelStateCanonicalizer.SerializeState(observed),
                timeout.Token).ConfigureAwait(false);
            return new ChannelUpdateResult(
                operation.RequestId,
                operation.InvocationDigest,
                status,
                outcome,
                operation.ResourceId,
                verified,
                before,
                desired,
                observed,
                errorCode);
        }
        catch (StewardException exception) when (exception.Code != StewardErrorCodes.AuditUnavailable)
        {
            return await RecordUnknownAsync(operation, before, desired).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await RecordUnknownAsync(
                operation,
                before,
                desired).ConfigureAwait(false);
        }
    }

    private async Task<ChannelUpdateResult> RecordUnknownAsync(
        OperationRecord operation,
        ChannelMutableState before,
        ChannelMutableState desired)
    {
        await CompleteAsync(
            operation.RequestId,
            OperationStatuses.Unknown,
            "write_outcome_unresolved",
            StewardErrorCodes.OutcomeUnknown,
            observedStateJson: null,
            CancellationToken.None).ConfigureAwait(false);
        return new ChannelUpdateResult(
            operation.RequestId,
            operation.InvocationDigest,
            OperationStatuses.Unknown,
            "write_outcome_unresolved",
            operation.ResourceId,
            Verified: false,
            before,
            desired,
            Observed: null,
            ErrorCode: StewardErrorCodes.OutcomeUnknown);
    }

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
            throw AuditUnavailable("The failed preflight could not be recorded durably.");
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
                throw AuditUnavailable("Discord state was read, but the completion transition failed.");
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
            throw AuditUnavailable("Discord state was read, but the completion record could not be written.");
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

    private async Task<ChannelSnapshot> GetChannelAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var configuredGuildId = settings.GuildId?.ToString(CultureInfo.InvariantCulture);
        if (configuredGuildId is null || !string.Equals(snapshot.Id, configuredGuildId, StringComparison.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.GuildMismatch,
                "Discord returned a resource outside the configured guild.");
        }

        return snapshot.Channels.SingleOrDefault(channel =>
                   string.Equals(channel.Id, channelId, StringComparison.Ordinal))
            ?? throw new StewardException(
                StewardErrorCodes.ResourceNotFound,
                "The requested channel was not found in the configured guild.");
    }

    private static ChannelUpdateInvocation NormalizeAndValidate(ChannelUpdateInvocation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestId = NormalizeRequestId(request.RequestId);
        var channelId = NormalizeSnowflake(request.ChannelId);
        var reason = ValidateReason(request.Reason);
        var expectedStateDigest = ValidateDigest(request.ExpectedStateDigest);
        if (request.Set is null)
        {
            throw InvalidArgument("The typed set object is required.");
        }

        ValidateSet(request.Set);
        var clearSource = request.Clear.IsDefault ? [] : request.Clear;
        var clear = clearSource
            .Select(field => field?.Trim() ?? string.Empty)
            .ToImmutableHashSet(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (clear.Any(field => field is not "topic"))
        {
            throw InvalidArgument("Only the topic field can be cleared by this operation.");
        }

        if (request.Set.Topic is not null && clear.Contains("topic", StringComparer.Ordinal))
        {
            throw InvalidArgument("The topic cannot be set and cleared in the same operation.");
        }

        if (request.Set.Name is null &&
            request.Set.Topic is null &&
            !request.Set.SlowModeSeconds.HasValue &&
            clear.IsDefaultOrEmpty)
        {
            throw InvalidArgument("At least one channel field must be set or cleared.");
        }

        return new ChannelUpdateInvocation(
            requestId,
            channelId,
            reason,
            expectedStateDigest,
            request.Set,
            clear);
    }

    private static ChannelMutableState ApplyDesiredState(
        ChannelMutableState before,
        ChannelUpdateInvocation invocation) => before with
        {
            Name = invocation.Set.Name ?? before.Name,
            Topic = invocation.Clear.Contains("topic", StringComparer.Ordinal)
                ? null
                : invocation.Set.Topic ?? before.Topic,
            SlowModeSeconds = invocation.Set.SlowModeSeconds ?? before.SlowModeSeconds
        };

    private static ChannelUpdateResult ToResult(OperationRecord operation) => new(
        operation.RequestId,
        operation.InvocationDigest,
        operation.Status,
        operation.Outcome ?? operation.Status,
        operation.ResourceId,
        Verified: operation.Status == OperationStatuses.Succeeded ||
            string.Equals(operation.Outcome, "verified_no_change", StringComparison.Ordinal),
        DeserializeState(operation.BeforeStateJson),
        DeserializeState(operation.DesiredStateJson),
        DeserializeState(operation.ObservedStateJson),
        operation.ErrorCode);

    private static ChannelMutableState? DeserializeState(string? json) =>
        json is null ? null : ChannelStateCanonicalizer.DeserializeState(json);

    private static bool IsTerminal(string status) => status is
        OperationStatuses.Succeeded or
        OperationStatuses.Failed or
        OperationStatuses.PartialFailure or
        OperationStatuses.Unknown;

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

    private static string NormalizeSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : throw InvalidArgument("Channel ID must be a positive Discord snowflake string.");

    private static string ValidateReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500 || value.Any(char.IsControl))
        {
            throw InvalidArgument("Reason must contain 1 to 500 printable characters.");
        }

        return value.Trim();
    }

    private static string ValidateDigest(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw InvalidArgument("Expected state digest must be a lowercase SHA-256 hex string.");
        }

        return value;
    }

    private static void ValidateSet(ChannelUpdateSet set)
    {
        if (set.Name is not null &&
            (string.IsNullOrWhiteSpace(set.Name) ||
             set.Name.Length > 100 ||
             set.Name.Any(character =>
                 character is not (>= 'a' and <= 'z') and
                 not (>= '0' and <= '9') and
                 not '-')))
        {
            throw InvalidArgument("Channel names allow 1 to 100 lowercase ASCII letters, digits, or hyphens.");
        }

        if (set.Topic is not null &&
            (string.IsNullOrWhiteSpace(set.Topic) ||
             set.Topic.Length > 1_024 ||
             set.Topic.Any(char.IsControl)))
        {
            throw InvalidArgument("Channel topics must contain 1 to 1024 printable characters; use clear for removal.");
        }

        if (set.SlowModeSeconds is < 0 or > 21_600)
        {
            throw InvalidArgument("Slow mode must be between 0 and 21600 seconds.");
        }
    }

    private static StewardException InvalidArgument(string message) => new(
        StewardErrorCodes.InvalidArgument,
        message);

    private static StewardException PolicyDenied(string message) => new(
        StewardErrorCodes.PolicyDenied,
        message);

    private static StewardException AuditUnavailable(string message) => new(
        StewardErrorCodes.AuditUnavailable,
        message);

    private static StewardException InProgress() => new(
        StewardErrorCodes.OperationInProgress,
        "The exact operation is already executing; no additional Discord write was attempted.");
}
