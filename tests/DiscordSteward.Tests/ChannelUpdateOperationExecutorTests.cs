using System.Collections.Immutable;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class ChannelUpdateOperationExecutorTests
{
    private const ulong GuildId = 100000000000000001UL;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-07-25T12:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task ExecuteAsync_ValidCallWritesOnceAndPersistsVerifiedResult()
    {
        var fixture = CreateFixture();

        var result = await fixture.Executor.ExecuteAsync(
            CreateRequest(fixture.Reader.CurrentChannel),
            CancellationToken.None);

        Assert.Equal(OperationStatuses.Succeeded, result.Status);
        Assert.Equal("verified_desired_state", result.Outcome);
        Assert.True(result.Verified);
        Assert.Equal(1, fixture.Writer.UpdateCount);
        var stored = await fixture.Journal.GetAsync(result.RequestId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(OperationStatuses.Succeeded, stored.Status);
        Assert.Equal(result.InvocationDigest, stored.InvocationDigest);
        Assert.Equal(fixture.Manifest.Digest, stored.ManifestDigest);
    }

    [Fact]
    public async Task ExecuteAsync_ExactReplayReturnsPriorResultWithoutAnotherWrite()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.Reader.CurrentChannel);

        var first = await fixture.Executor.ExecuteAsync(request, CancellationToken.None);
        var replay = await fixture.Executor.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(1, fixture.Writer.UpdateCount);
    }

    [Fact]
    public async Task ExecuteAsync_RequestIdCollisionFailsBeforeDiscordAccess()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.Reader.CurrentChannel);
        await fixture.Executor.ExecuteAsync(request, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<StewardException>(() => fixture.Executor.ExecuteAsync(
            request with { Reason = "A different approved call" },
            CancellationToken.None));

        Assert.Equal(StewardErrorCodes.IdempotencyConflict, exception.Code);
        Assert.Equal(1, fixture.Writer.UpdateCount);
    }

    [Fact]
    public async Task ExecuteAsync_StateConflictIsJournaledBeforePermissionCheck()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.Reader.CurrentChannel) with
        {
            ExpectedStateDigest = new string('0', 64)
        };

        var exception = await Assert.ThrowsAsync<StewardException>(() => fixture.Executor.ExecuteAsync(
            request,
            CancellationToken.None));

        Assert.Equal(StewardErrorCodes.StateConflict, exception.Code);
        Assert.Equal(0, fixture.Writer.PreflightCount);
        Assert.Equal(0, fixture.Writer.UpdateCount);
        var stored = await fixture.Journal.GetAsync(request.RequestId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(OperationStatuses.Failed, stored.Status);
        Assert.Equal(StewardErrorCodes.StateConflict, stored.ErrorCode);
    }

    [Theory]
    [InlineData(false, false, "discord_permission_denied")]
    [InlineData(true, true, "policy_denied")]
    public async Task ExecuteAsync_UnsafePermissionsStopBeforeClaimAndWrite(
        bool hasManageChannels,
        bool hasAdministrator,
        string expectedCode)
    {
        var fixture = CreateFixture();
        fixture.Writer.Preflight = new DiscordChannelWritePreflight(hasManageChannels, hasAdministrator);
        var request = CreateRequest(fixture.Reader.CurrentChannel);

        var exception = await Assert.ThrowsAsync<StewardException>(() => fixture.Executor.ExecuteAsync(
            request,
            CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(0, fixture.Writer.UpdateCount);
        Assert.Equal(
            OperationStatuses.Failed,
            (await fixture.Journal.GetAsync(request.RequestId, CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task ExecuteAsync_OtherPolicyDeniedBotPermissionStopsBeforeWrite()
    {
        var fixture = CreateFixture(["Administrator", "ManageWebhooks"]);
        fixture.Writer.Preflight = new DiscordChannelWritePreflight(
            HasManageChannels: true,
            HasAdministrator: false,
            EffectivePermissions: ["ManageChannels", "ManageWebhooks", "ViewChannel"]);

        var exception = await Assert.ThrowsAsync<StewardException>(() => fixture.Executor.ExecuteAsync(
            CreateRequest(fixture.Reader.CurrentChannel),
            CancellationToken.None));

        Assert.Equal(StewardErrorCodes.PolicyDenied, exception.Code);
        Assert.Contains("ManageWebhooks", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Writer.UpdateCount);
    }

    [Fact]
    public async Task ExecuteAsync_LostResponseRecoversDesiredStateWithoutRetry()
    {
        var fixture = CreateFixture();
        fixture.Writer.UpdateException = new StewardException(
            StewardErrorCodes.DiscordUnavailable,
            "Discord response was lost.");
        fixture.Writer.ApplyDesiredBeforeException = true;

        var result = await fixture.Executor.ExecuteAsync(
            CreateRequest(fixture.Reader.CurrentChannel),
            CancellationToken.None);

        Assert.Equal(OperationStatuses.Succeeded, result.Status);
        Assert.Equal("recovered_verified_desired_state", result.Outcome);
        Assert.Equal(1, fixture.Writer.UpdateCount);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentExactCallsIssueOneWrite()
    {
        var fixture = CreateFixture();
        fixture.Writer.PauseBeforeUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest(fixture.Reader.CurrentChannel);

        var first = fixture.Executor.ExecuteAsync(request, CancellationToken.None);
        await fixture.Writer.UpdateStarted.Task;
        var second = fixture.Executor.ExecuteAsync(request, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<StewardException>(() => second);
        fixture.Writer.PauseBeforeUpdate.SetResult();
        var result = await first;

        Assert.Equal(StewardErrorCodes.OperationInProgress, exception.Code);
        Assert.Equal(OperationStatuses.Succeeded, result.Status);
        Assert.Equal(1, fixture.Writer.UpdateCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsSetAndClearOfSameFieldBeforeReservation()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.Reader.CurrentChannel) with
        {
            Clear = ["topic"]
        };

        var exception = await Assert.ThrowsAsync<StewardException>(() => fixture.Executor.ExecuteAsync(
            request,
            CancellationToken.None));

        Assert.Equal(StewardErrorCodes.InvalidArgument, exception.Code);
        Assert.Null(await fixture.Journal.GetAsync(request.RequestId, CancellationToken.None));
        Assert.Equal(0, fixture.Writer.UpdateCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNullSetAndDefaultClearBeforeReservation()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.Reader.CurrentChannel) with
        {
            Set = null!,
            Clear = default
        };

        var exception = await Assert.ThrowsAsync<StewardException>(() => fixture.Executor.ExecuteAsync(
            request,
            CancellationToken.None));

        Assert.Equal(StewardErrorCodes.InvalidArgument, exception.Code);
        Assert.Null(await fixture.Journal.GetAsync(request.RequestId, CancellationToken.None));
    }

    [Fact]
    public async Task ReconcileAsync_UnknownWriteResolvesByReadWithoutRetry()
    {
        var fixture = CreateFixture();
        fixture.Writer.BreakReadsAfterUpdate = true;
        var request = CreateRequest(fixture.Reader.CurrentChannel);

        var unknown = await fixture.Executor.ExecuteAsync(request, CancellationToken.None);
        fixture.Reader.ReadException = null;
        var reconciled = await fixture.Executor.ReconcileAsync(request.RequestId, CancellationToken.None);

        Assert.Equal(OperationStatuses.Unknown, unknown.Status);
        Assert.Equal(StewardErrorCodes.OutcomeUnknown, unknown.ErrorCode);
        Assert.Equal(OperationStatuses.Succeeded, reconciled.Status);
        Assert.Equal("reconciled_verified_desired_state", reconciled.Outcome);
        Assert.Equal(1, fixture.Writer.UpdateCount);
    }

    [Fact]
    public async Task ReconcileAsync_StaleReservationRecordsNoWriteAttempt()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.Reader.CurrentChannel);
        var json = OperationCanonicalizer.Serialize(request);
        var digest = OperationCanonicalizer.ComputeDigest(json);
        await fixture.Journal.ReserveAsync(
            new OperationRecord(
                1,
                request.RequestId,
                OperationKinds.ChannelUpdate,
                GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                request.ChannelId,
                request.Reason,
                "R3",
                "policy",
                fixture.Manifest.Digest,
                digest,
                json,
                Now,
                OperationStatuses.Reserved,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            CancellationToken.None);
        fixture.Time.SetUtcNow(Now.AddMinutes(3));

        var result = await fixture.Executor.ReconcileAsync(request.RequestId, CancellationToken.None);

        Assert.Equal(OperationStatuses.Failed, result.Status);
        Assert.Equal("verified_not_attempted", result.Outcome);
        Assert.Equal(0, fixture.Writer.UpdateCount);
    }

    private static ExecutorFixture CreateFixture(IEnumerable<string>? deniedPermissions = null)
    {
        var reader = new MutableGuildReader(CreateSnapshot());
        var writer = new FakeGuildWriter(reader);
        var journal = new InMemoryOperationJournal();
        var policy = deniedPermissions is null
            ? StewardPolicy.CreateDefault()
            : StewardPolicy.Create([], [], deniedPermissions);
        var manifest = StewardToolManifest.Create(
            [
                "get_steward_capabilities",
                "get_channel",
                "get_operation",
                "reconcile_operation",
                "update_channel"
            ],
            new StewardRiskGates(
                SensitiveReads: true,
                ReversibleWrites: true,
                PersonImpactingWrites: false,
                IrreversibleWrites: false));
        var time = new MutableTimeProvider(Now);
        var executor = new ChannelUpdateOperationExecutor(
            reader,
            writer,
            journal,
            policy,
            manifest,
            new StewardRuntimeSettings(GuildId, DiscordTokenConfigured: true),
            time);
        return new ExecutorFixture(executor, reader, writer, journal, manifest, time);
    }

    private static ChannelUpdateInvocation CreateRequest(ChannelSnapshot channel) => new(
        RequestId: "01900000-0000-7000-8000-000000000111",
        ChannelId: channel.Id,
        Reason: "Set a welcome topic",
        ExpectedStateDigest: ChannelStateCanonicalizer.ComputeDigest(channel),
        Set: new ChannelUpdateSet(Name: null, Topic: "Welcome", SlowModeSeconds: null),
        Clear: []);

    private static GuildSnapshot CreateSnapshot() => GuildSnapshot.Create(
        GuildId,
        "Guild",
        ownerId: 1,
        description: null,
        DateTimeOffset.UnixEpoch,
        [],
        [],
        [CategorySnapshot.Create(GuildId, 20, "Text", 0, [])],
        [ChannelSnapshot.Create(GuildId, 21, "general", "text", 0, 20, null, false, 0, [])]);

    private sealed record ExecutorFixture(
        ChannelUpdateOperationExecutor Executor,
        MutableGuildReader Reader,
        FakeGuildWriter Writer,
        InMemoryOperationJournal Journal,
        StewardToolManifest Manifest,
        MutableTimeProvider Time);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class MutableGuildReader(GuildSnapshot snapshot) : IDiscordGuildReader
    {
        private readonly object _gate = new();
        private GuildSnapshot _snapshot = snapshot;

        public Exception? ReadException { get; set; }

        public ChannelSnapshot CurrentChannel
        {
            get
            {
                lock (_gate)
                {
                    return _snapshot.Channels.Single();
                }
            }
        }

        public Task<GuildSnapshot> GetGuildSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadException is not null)
            {
                throw ReadException;
            }

            lock (_gate)
            {
                return Task.FromResult(_snapshot);
            }
        }

        public void SetChannelState(string channelId, ChannelMutableState state)
        {
            lock (_gate)
            {
                var channel = _snapshot.Channels.Single(candidate => candidate.Id == channelId);
                var replacement = ChannelSnapshot.Create(
                    GuildId,
                    ulong.Parse(channel.Id, System.Globalization.CultureInfo.InvariantCulture),
                    state.Name,
                    channel.Kind,
                    channel.Position,
                    state.CategoryId is null
                        ? null
                        : ulong.Parse(state.CategoryId, System.Globalization.CultureInfo.InvariantCulture),
                    state.Topic,
                    channel.IsNsfw,
                    state.SlowModeSeconds,
                    channel.PermissionOverwrites);
                _snapshot = GuildSnapshot.Create(
                    GuildId,
                    _snapshot.Name,
                    ulong.Parse(_snapshot.OwnerId, System.Globalization.CultureInfo.InvariantCulture),
                    _snapshot.Description,
                    _snapshot.CreatedAt,
                    _snapshot.Features,
                    _snapshot.Roles,
                    _snapshot.Categories,
                    _snapshot.Channels.Select(candidate => candidate.Id == channelId ? replacement : candidate));
            }
        }
    }

    private sealed class FakeGuildWriter(MutableGuildReader reader) : IDiscordGuildWriter
    {
        public DiscordChannelWritePreflight Preflight { get; set; } = new(
            HasManageChannels: true,
            HasAdministrator: false);

        public Exception? UpdateException { get; set; }

        public bool ApplyDesiredBeforeException { get; set; }

        public bool BreakReadsAfterUpdate { get; set; }

        public TaskCompletionSource UpdateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? PauseBeforeUpdate { get; set; }

        public int PreflightCount { get; private set; }

        public int UpdateCount { get; private set; }

        public Task<DiscordChannelWritePreflight> PreflightChannelUpdateAsync(
            string channelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreflightCount++;
            return Task.FromResult(Preflight);
        }

        public async Task UpdateTextChannelAsync(
            string planId,
            string channelId,
            ChannelMutableState desired,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateCount++;
            UpdateStarted.TrySetResult();
            if (PauseBeforeUpdate is not null)
            {
                await PauseBeforeUpdate.Task.WaitAsync(cancellationToken);
            }

            if (UpdateException is null || ApplyDesiredBeforeException)
            {
                reader.SetChannelState(channelId, desired);
            }

            if (BreakReadsAfterUpdate)
            {
                reader.ReadException = new StewardException(
                    StewardErrorCodes.DiscordUnavailable,
                    "Discord verification read is unavailable.");
            }

            if (UpdateException is not null)
            {
                throw UpdateException;
            }
        }
    }
}
