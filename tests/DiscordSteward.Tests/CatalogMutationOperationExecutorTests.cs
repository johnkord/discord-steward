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
