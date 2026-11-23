using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class CatalogMutationOperationExecutorTests
{
    [Fact]
    public async Task Execute_ClaimsWritesOnceVerifiesAndReplays()
    {
        var adapter = new FakeAdapter();
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(adapter, journal);
        var invocation = Invocation(Guid.NewGuid().ToString("D"), "{\"b\":2,\"a\":1}");

        var first = await executor.ExecuteAsync(invocation, CancellationToken.None);
        var replay = await executor.ExecuteAsync(invocation, CancellationToken.None);

        Assert.Equal(OperationStatuses.Succeeded, first.Status);
        Assert.True(first.Verified);
        Assert.Equal(first.InvocationDigest, replay.InvocationDigest);
        Assert.Equal(first.Status, replay.Status);
        Assert.Equal(first.Observed?.GetRawText(), replay.Observed?.GetRawText());
        Assert.Equal(1, adapter.WriteCount);
        var stored = await journal.GetAsync(first.RequestId, CancellationToken.None);
        Assert.Equal(OperationStatuses.Succeeded, stored?.Status);
    }

    [Fact]
    public async Task Execute_CanonicalizesArgumentObjectOrderForIdempotency()
    {
        var adapter = new FakeAdapter();
        var executor = CreateExecutor(adapter, new InMemoryOperationJournal());
        var requestId = Guid.NewGuid().ToString("D");

        var first = await executor.ExecuteAsync(
            Invocation(requestId, "{\"b\":2,\"a\":1}"),
            CancellationToken.None);
        var replay = await executor.ExecuteAsync(
            Invocation(requestId, "{\"a\":1,\"b\":2}"),
            CancellationToken.None);

        Assert.Equal(first.InvocationDigest, replay.InvocationDigest);
        Assert.Equal(1, adapter.WriteCount);
    }

    [Fact]
    public async Task Execute_RejectsRequestIdCollisionBeforeSecondWrite()
    {
        var adapter = new FakeAdapter();
        var executor = CreateExecutor(adapter, new InMemoryOperationJournal());
        var requestId = Guid.NewGuid().ToString("D");
        await executor.ExecuteAsync(Invocation(requestId, "{\"value\":1}"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<StewardException>(() => executor.ExecuteAsync(
            Invocation(requestId, "{\"value\":2}"),
            CancellationToken.None));

        Assert.Equal(StewardErrorCodes.IdempotencyConflict, exception.Code);
        Assert.Equal(1, adapter.WriteCount);
    }

    [Fact]
    public async Task Execute_AdministratorFailsPreflightWithoutWrite()
    {
        var adapter = new FakeAdapter();
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(adapter, journal, hasAdministrator: true);
        var requestId = Guid.NewGuid().ToString("D");

        var exception = await Assert.ThrowsAsync<StewardException>(() => executor.ExecuteAsync(
            Invocation(requestId, "{\"value\":1}"),
            CancellationToken.None));

        Assert.Equal(StewardErrorCodes.PolicyDenied, exception.Code);
        Assert.Equal(0, adapter.WriteCount);
        var stored = await journal.GetAsync(requestId, CancellationToken.None);
        Assert.Equal(OperationStatuses.Failed, stored?.Status);
        Assert.Equal(StewardErrorCodes.PolicyDenied, stored?.ErrorCode);
    }

    [Fact]
    public async Task Execute_UnrestrictedAutonomyAllowsAdministratorAndProtectedResource()
    {
        var adapter = new FakeAdapter();
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(
            adapter,
            journal,
            hasAdministrator: true,
            effectivePermissions: ["Administrator"],
            authorizationMode: StewardAuthorizationMode.UnrestrictedAutonomy,
            protectedResourceIds: ["999"]);

        var result = await executor.ExecuteAsync(
            Invocation(Guid.NewGuid().ToString("D"), "{\"value\":1}"),
            CancellationToken.None);

        Assert.Equal(OperationStatuses.Succeeded, result.Status);
        Assert.Equal(1, adapter.WriteCount);
    }

    [Fact]
    public async Task Execute_PersistsCanonicalMcpRequestMetadata()
    {
        var adapter = new FakeAdapter();
        var journal = new InMemoryOperationJournal();
        var requestMetadata = new StewardRequestMetadataContext();
        var executor = CreateExecutor(adapter, journal, requestMetadata: requestMetadata);
        CatalogMutationResult result;
        using (requestMetadata.Push(JsonNode.Parse("{\"traceId\":\"trace-1\",\"actionId\":\"action-1\"}")!.AsObject()))
        {
            result = await executor.ExecuteAsync(
                Invocation(Guid.NewGuid().ToString("D"), "{\"value\":1}"),
                CancellationToken.None);
        }

        var stored = await journal.GetAsync(result.RequestId, CancellationToken.None);

        Assert.Equal("{\"actionId\":\"action-1\",\"traceId\":\"trace-1\"}", stored?.RequestMetadataJson);
    }

    [Fact]
    public async Task Execute_UnknownOutcomeReconcilesWithoutRepeatingWrite()
    {
        var adapter = new FakeAdapter { ObservationException = new IOException("read failed") };
        var journal = new InMemoryOperationJournal();
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var executor = CreateExecutor(adapter, journal, timeProvider: time);
        var invocation = Invocation(Guid.NewGuid().ToString("D"), "{\"value\":1}");

        var unknown = await executor.ExecuteAsync(invocation, CancellationToken.None);
        Assert.Equal(OperationStatuses.Unknown, unknown.Status);
        Assert.Equal(1, adapter.WriteCount);

        adapter.ObservationException = null;
        time.Advance(TimeSpan.FromMinutes(3));
        var reconciled = await executor.ReconcileAsync(invocation.RequestId, CancellationToken.None);

        Assert.Equal(OperationStatuses.Succeeded, reconciled.Status);
        Assert.Equal("reconciled_verified_desired_state", reconciled.Outcome);
        Assert.Equal(1, adapter.WriteCount);
    }

    [Fact]
    public async Task AbandonedExecutionCanRemainUnknownOrResolveWithoutCallingWrite()
    {
        var adapter = new FakeAdapter { ObservationException = new StewardException(StewardErrorCodes.OutcomeUnknown, "No receipt") };
        var journal = new InMemoryOperationJournal();
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var executor = CreateExecutor(adapter, journal, timeProvider: time);
        var invocation = Invocation(Guid.NewGuid().ToString("D"), "{}");
        var json = OperationCanonicalizer.Serialize(invocation);
        var digest = OperationCanonicalizer.ComputeDigest(json);
        await journal.ReserveAsync(new OperationRecord(1, invocation.RequestId, invocation.ToolName, "123", invocation.ResourceId,
            invocation.Reason, "R3", "profile", "manifest", digest, json, time.GetUtcNow(), OperationStatuses.Reserved,
            null, null, null, null, null, null, null), CancellationToken.None);
        Assert.True(await journal.BeginExecutionAsync(invocation.RequestId, digest, "{}", "{\"state\":1}", time.GetUtcNow(), CancellationToken.None));
        var recent = await Assert.ThrowsAsync<StewardException>(() => executor.ReconcileAsync(invocation.RequestId, CancellationToken.None));
        Assert.Equal(StewardErrorCodes.OperationInProgress, recent.Code);
        Assert.Equal(0, adapter.ObservationCount);
        time.Advance(TimeSpan.FromMinutes(3));
        var unknown = await executor.ReconcileAsync(invocation.RequestId, CancellationToken.None);
        Assert.Equal(OperationStatuses.Unknown, unknown.Status);
        Assert.False(unknown.Verified);
        Assert.Equal(StewardErrorCodes.OutcomeUnknown, (await journal.GetAsync(invocation.RequestId, CancellationToken.None))?.ErrorCode);
        Assert.Equal(unknown.Outcome, (await executor.ReconcileAsync(invocation.RequestId, CancellationToken.None)).Outcome);
        adapter.ObservationException = null;
        var resolved = await executor.ReconcileAsync(invocation.RequestId, CancellationToken.None);
        Assert.Equal(OperationStatuses.Succeeded, resolved.Status);
        Assert.Null(resolved.ErrorCode);
        Assert.Equal(0, adapter.WriteCount);
    }

    [Fact]
    public async Task Execute_UnknownOutcomePersistsOnlySanitizedReceiptIdentifiers()
    {
        var adapter = new FakeAdapter
        {
            ReceiptJson = "{\"id\":\"123\",\"token\":\"secret-token\",\"content\":\"private body\",\"url\":\"https://example.invalid\"}",
            ObservationException = new IOException("verification failed")
        };
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(adapter, journal);
        var invocation = Invocation(Guid.NewGuid().ToString("D"), "{\"value\":1}");

        var result = await executor.ExecuteAsync(invocation, CancellationToken.None);
        var stored = await journal.GetAsync(result.RequestId, CancellationToken.None);

        Assert.Equal(OperationStatuses.Unknown, result.Status);
        Assert.Equal("{\"id\":\"123\"}", stored?.ObservedStateJson);
        Assert.DoesNotContain("secret-token", stored?.ObservedStateJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private body", stored?.ObservedStateJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_DefinitiveWriteRejectionRecordsVerifiedFailureWithoutObservation()
    {
        var adapter = new FakeAdapter
        {
            WriteException = new StewardException(
                StewardErrorCodes.DiscordPermissionDenied,
                "Discord rejected the write.")
        };
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(adapter, journal);
        var invocation = Invocation(Guid.NewGuid().ToString("D"), "{\"value\":1}");

        var result = await executor.ExecuteAsync(invocation, CancellationToken.None);
        var stored = await journal.GetAsync(result.RequestId, CancellationToken.None);

        Assert.Equal(OperationStatuses.Failed, result.Status);
        Assert.Equal("write_rejected", result.Outcome);
        Assert.Equal(StewardErrorCodes.DiscordPermissionDenied, result.ErrorCode);
        Assert.True(result.Verified);
        Assert.Equal(1, adapter.WriteCount);
        Assert.Equal(0, adapter.ObservationCount);
        Assert.Equal("{\"state\":\"before\"}", stored?.ObservedStateJson);
    }

    [Fact]
    public async Task Execute_UnexpectedObservedStatePersistsPartialFailure()
    {
        var adapter = new FakeAdapter
        {
            ObservationKind = CatalogMutationObservationKind.UnexpectedState
        };
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(adapter, journal);

        var result = await executor.ExecuteAsync(
            Invocation(Guid.NewGuid().ToString("D"), "{\"value\":1}"),
            CancellationToken.None);
        var stored = await journal.GetAsync(result.RequestId, CancellationToken.None);

        Assert.Equal(OperationStatuses.PartialFailure, result.Status);
        Assert.Equal("observed_unexpected_state", result.Outcome);
        Assert.Equal(StewardErrorCodes.PartialFailure, result.ErrorCode);
        Assert.False(result.Verified);
        Assert.Equal(OperationStatuses.PartialFailure, stored?.Status);
    }

    [Theory]
    [InlineData("write_rejected", true)]
    [InlineData("verified_desired_state", true)]
    [InlineData("verified_no_change", true)]
    [InlineData("accepted_unverifiable", false)]
    [InlineData("observed_unexpected_state", false)]
    [InlineData("write_outcome_unresolved", false)]
    public async Task TerminalReceiptSurvivesDuplicateAndJournalReopen(string outcome, bool verified)
    {
        var directory = Directory.CreateTempSubdirectory("steward-receipt-");
        try
        {
            var path = Path.Combine(directory.FullName, "operations.json");
            var adapter = new FakeAdapter
            {
                WriteException = outcome == "write_rejected" ? new StewardException(StewardErrorCodes.DiscordPermissionDenied, "Rejected") : null,
                ObservationException = outcome == "write_outcome_unresolved" ? new IOException("No observation") : null,
                ObservationKind = outcome switch
                {
                    "verified_no_change" => CatalogMutationObservationKind.BeforeState,
                    "accepted_unverifiable" => CatalogMutationObservationKind.AcceptedUnverifiable,
                    "observed_unexpected_state" => CatalogMutationObservationKind.UnexpectedState,
                    _ => CatalogMutationObservationKind.DesiredState
                }
            };
            var invocation = Invocation(Guid.NewGuid().ToString("D"), "{}");
            string expected;
            using (var journal = new FileBackedOperationJournal(path))
            {
                var executor = CreateExecutor(adapter, journal);
                var first = await executor.ExecuteAsync(invocation, CancellationToken.None);
                Assert.Equal(outcome, first.Outcome);
                Assert.Equal(verified, first.Verified);
                expected = JsonSerializer.Serialize(first);
                Assert.Equal(expected, JsonSerializer.Serialize(await executor.ExecuteAsync(invocation, CancellationToken.None)));
            }

            using var reopened = new FileBackedOperationJournal(path);
            var restarted = CreateExecutor(adapter, reopened);
            Assert.Equal(expected, JsonSerializer.Serialize(await restarted.ExecuteAsync(invocation, CancellationToken.None)));
            if (outcome != "write_outcome_unresolved")
            {
                Assert.Equal(expected, JsonSerializer.Serialize(await restarted.ReconcileAsync(invocation.RequestId, CancellationToken.None)));
            }

            Assert.Equal(1, adapter.WriteCount);
            Assert.Equal(outcome == "write_rejected" ? 0 : 1, adapter.ObservationCount);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_AcceptedUnverifiableIsTerminalWithoutClaimingVerification()
    {
        var adapter = new FakeAdapter
        {
            ObservationKind = CatalogMutationObservationKind.AcceptedUnverifiable
        };
        var journal = new InMemoryOperationJournal();
        var executor = CreateExecutor(adapter, journal);
        var invocation = Invocation(Guid.NewGuid().ToString("D"), "{\"value\":1}");

        var result = await executor.ExecuteAsync(invocation, CancellationToken.None);
        var replay = await executor.ExecuteAsync(invocation, CancellationToken.None);

        Assert.Equal(OperationStatuses.Succeeded, result.Status);
        Assert.Equal("accepted_unverifiable", result.Outcome);
        Assert.False(result.Verified);
        Assert.Null(result.ErrorCode);
        Assert.Equal(result.InvocationDigest, replay.InvocationDigest);
        Assert.Equal(result.Status, replay.Status);
        Assert.Equal(result.Outcome, replay.Outcome);
        Assert.Equal(result.Verified, replay.Verified);
        Assert.Equal(result.Before?.GetRawText(), replay.Before?.GetRawText());
        Assert.Equal(result.Desired?.GetRawText(), replay.Desired?.GetRawText());
        Assert.Equal(result.Observed?.GetRawText(), replay.Observed?.GetRawText());
        Assert.Equal(result.ErrorCode, replay.ErrorCode);
        Assert.Equal(1, adapter.WriteCount);
    }

    [Fact]
    public async Task Execute_ExcessNonAdministratorPermissionsDoNotBlockWrite()
    {
        var adapter = new FakeAdapter();
        var executor = CreateExecutor(
            adapter,
            new InMemoryOperationJournal(),
            effectivePermissions:
            [
                "BanMembers",
                "KickMembers",
                "ManageChannels",
                "ManageWebhooks",
                "MentionEveryone",
                "ViewChannel"
            ]);

        var result = await executor.ExecuteAsync(
            Invocation(Guid.NewGuid().ToString("D"), "{\"value\":1}"),
            CancellationToken.None);

        Assert.Equal(OperationStatuses.Succeeded, result.Status);
        Assert.Equal(1, adapter.WriteCount);
    }

    private static CatalogMutationOperationExecutor CreateExecutor(
        FakeAdapter adapter,
        IOperationJournal journal,
        bool hasAdministrator = false,
        TimeProvider? timeProvider = null,
        ImmutableArray<string>? effectivePermissions = null,
        StewardAuthorizationMode authorizationMode = StewardAuthorizationMode.InteractiveApproval,
        IEnumerable<string>? protectedResourceIds = null,
        StewardRequestMetadataContext? requestMetadata = null)
    {
        var manifest = StewardToolManifest.Create(
            authorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy ? ["*"] : ["update_channel"],
            new StewardRiskGates(
                SensitiveReads: false,
                ReversibleWrites: true,
                PersonImpactingWrites: false,
                IrreversibleWrites: false),
            authorizationMode);
        return new CatalogMutationOperationExecutor(
            adapter,
            new FakeInspector(new BotEffectivePermissions(
                GuildId: "123",
                BotUserId: "456",
                EffectivePermissions: effectivePermissions ?? ["ManageChannels", "ViewChannel"],
                HighestRoleId: "789",
                HighestRoleName: "Steward",
                HighestRolePosition: 10,
                HasAdministrator: hasAdministrator)),
            journal,
            StewardPolicy.Create(
                protectedResourceIds ?? [],
                ["discsteward"],
                ["Administrator"],
                authorizationMode),
            manifest,
            StewardRuntimeSettings.FromValues("123", "token"),
            timeProvider ?? TimeProvider.System,
            requestMetadata);
    }

    private static CatalogMutationInvocation Invocation(string requestId, string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return new CatalogMutationInvocation(
            requestId,
            "update_channel",
            "999",
            "test operation",
            document.RootElement.Clone());
    }

    private sealed class FakeAdapter : ICatalogMutationAdapter
    {
        public int WriteCount { get; private set; }

        public int ObservationCount { get; private set; }

        public Exception? ObservationException { get; set; }

        public Exception? WriteException { get; set; }

        public string ReceiptJson { get; set; } = "{\"receipt\":\"ok\"}";

        public CatalogMutationObservationKind ObservationKind { get; set; } =
            CatalogMutationObservationKind.DesiredState;

        public bool Supports(string toolName) => toolName == "update_channel";

        public Task<CatalogMutationPreparation> PrepareAsync(
            CatalogMutationInvocation invocation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CatalogMutationPreparation(
                "{\"state\":\"before\"}",
                "{\"state\":\"desired\"}"));
        }

        public Task<CatalogMutationWriteReceipt> WriteAsync(
            CatalogMutationInvocation invocation,
            CatalogMutationPreparation preparation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            if (WriteException is not null)
            {
                throw WriteException;
            }

            return Task.FromResult(new CatalogMutationWriteReceipt(ReceiptJson));
        }

        public Task<CatalogMutationObservation> ObserveAsync(
            CatalogMutationInvocation invocation,
            string beforeStateJson,
            string desiredStateJson,
            string? priorReceiptJson,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObservationCount++;
            if (ObservationException is not null)
            {
                throw ObservationException;
            }

            return Task.FromResult(new CatalogMutationObservation(
                ObservationKind,
                ObservationKind == CatalogMutationObservationKind.DesiredState
                    ? desiredStateJson
                    : "{\"state\":\"mixed\"}"));
        }
    }

    private sealed class FakeInspector(BotEffectivePermissions result) : IDiscordBotInspector
    {
        public Task<BotEffectivePermissions> GetEffectivePermissionsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now += duration;
    }
}
