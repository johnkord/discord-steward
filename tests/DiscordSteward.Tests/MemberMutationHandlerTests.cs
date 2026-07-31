using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class MemberMutationHandlerTests
{
    private const string GuildId = "123";

    [Fact]
    public async Task BulkBan_WritesSortedExactTargetsWithoutHistoryDeletionAndVerifies()
    {
        await using var fixture = CreateFixture(["10", "11"]);
        var invocation = BulkBanInvocation(fixture, "11", "10");

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);
        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            preparation.BeforeStateJson,
            preparation.DesiredStateJson,
            priorReceiptJson: null,
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.DesiredState, observation.Kind);
        Assert.Equal(1, fixture.Http.MutationCount);
        var body = Assert.IsType<JsonObject>(fixture.Http.LastMutationBody);
        Assert.Equal(
            ["10", "11"],
            body["user_ids"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.Equal(0, body["delete_message_seconds"]!.GetValue<int>());
    }

    [Fact]
    public async Task BulkBan_MixedDiscordResultIsObservedAsPartialState()
    {
        await using var fixture = CreateFixture(["10"]);
        var invocation = BulkBanInvocation(fixture, "10", "11");

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);
        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            preparation.BeforeStateJson,
            preparation.DesiredStateJson,
            priorReceiptJson: null,
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.UnexpectedState, observation.Kind);
        Assert.Contains("\"userId\":\"10\",\"isBanned\":true", observation.ObservedStateJson, StringComparison.Ordinal);
        Assert.Contains("\"userId\":\"11\",\"isBanned\":false", observation.ObservedStateJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkBan_ProtectedTargetRefusesBeforeWrite()
    {
        await using var fixture = CreateFixture(["10", "11"], protectedUserIds: ["10"]);
        var invocation = BulkBanInvocation(fixture, "10", "11");

        var exception = await Assert.ThrowsAsync<StewardException>(() =>
            fixture.Handler.PrepareAsync(invocation, CancellationToken.None));

        Assert.Equal(StewardErrorCodes.PolicyDenied, exception.Code);
        Assert.Equal(0, fixture.Http.MutationCount);
    }

    [Fact]
    public async Task BulkBan_UnrestrictedAutonomyAllowsProtectedTarget()
    {
        await using var fixture = CreateFixture(
            ["10", "11"],
            protectedUserIds: ["10"],
            authorizationMode: StewardAuthorizationMode.UnrestrictedAutonomy);
        var invocation = BulkBanInvocation(fixture, "10", "11");

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);

        Assert.Equal(1, fixture.Http.MutationCount);
    }

    private static CatalogMutationInvocation BulkBanInvocation(Fixture fixture, params string[] userIds) =>
        CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "bulk_ban_members",
            GuildId,
            "remove coordinated abusive accounts",
            new BulkBanMembersInput(userIds.Select(userId => new BulkBanTargetInput(
                userId,
                fixture.MemberReader.Members[userId].StateDigest)).ToImmutableArray()));

    private static Fixture CreateFixture(
        ImmutableArray<string> successfulUserIds,
        ImmutableArray<string> protectedUserIds = default,
        StewardAuthorizationMode authorizationMode = StewardAuthorizationMode.InteractiveApproval)
    {
        var options = DiscordOptions.FromValues(GuildId, "test-token");
        var session = new DiscordRestSession(options);
        var http = new BulkBanHttpHandler(successfulUserIds);
        var transport = new DiscordApiTransport(options, session, http);
        var memberReader = new FakeMemberReader([CreateMember("10", 'a'), CreateMember("11", 'b')]);
        var guildReader = new InMemoryDiscordGuildReader(GuildSnapshot.Create(
            123,
            "Guild",
            ownerId: 1,
            description: null,
            DateTimeOffset.UnixEpoch,
            [],
            [],
            [],
            []));
        var botInspector = new FakeBotInspector();
        var policy = StewardPolicy.Create(
            protectedUserIds.IsDefault ? [] : protectedUserIds,
            [],
            [],
            authorizationMode);
        var handler = new MemberMutationHandler(
            transport,
            guildReader,
            memberReader,
            botInspector,
            policy,
            StewardRuntimeSettings.FromValues(GuildId, "test-token"),
            TimeProvider.System);
        return new Fixture(handler, transport, session, http, memberReader);
    }

    private static MemberView CreateMember(string userId, char digestCharacter) => new(
        userId,
        $"member-{userId}",
        $"Member {userId}",
        Nickname: null,
        RoleIds: [],
        JoinedAt: DateTimeOffset.UnixEpoch,
        TimedOutUntil: null,
        Flags: "None",
        FlagsValue: 0,
        Hierarchy: 1,
        IsPending: false,
        new MemberVoiceStateView(userId, null, false, false, false, false, false, false, false, null),
        new string(digestCharacter, 64));

    private sealed class Fixture(
        MemberMutationHandler handler,
        DiscordApiTransport transport,
        DiscordRestSession session,
        BulkBanHttpHandler http,
        FakeMemberReader memberReader) : IAsyncDisposable
    {
        internal MemberMutationHandler Handler { get; } = handler;

        internal BulkBanHttpHandler Http { get; } = http;

        internal FakeMemberReader MemberReader { get; } = memberReader;

        public async ValueTask DisposeAsync()
        {
            transport.Dispose();
            await session.DisposeAsync();
        }
    }

    private sealed class FakeMemberReader(IEnumerable<MemberView> members) : IDiscordMemberReader
    {
        internal ImmutableDictionary<string, MemberView> Members { get; } =
            members.ToImmutableDictionary(member => member.UserId, StringComparer.Ordinal);

        public Task<MemberView> GetMemberAsync(string userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Members.TryGetValue(userId, out var member)
                ? Task.FromResult(member)
                : throw new StewardException(StewardErrorCodes.ResourceNotFound, "Member not found.");
        }

        public Task<MemberPage> SearchMembersAsync(string query, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MemberPage> ListMembersAsync(int limit, string? afterUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MemberPage> ListMembersByRoleAsync(
            string roleId,
            int limit,
            string? afterUserId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<MemberVoiceStateView> GetMemberVoiceStateAsync(string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImmutableArray<RoleMemberCountView>> GetRoleMemberCountsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MemberPrunePreview> PreviewMemberPruneAsync(
            int inactivityDays,
            IReadOnlyCollection<string> roleIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeBotInspector : IDiscordBotInspector
    {
        public Task<BotEffectivePermissions> GetEffectivePermissionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BotEffectivePermissions(
                GuildId,
                BotUserId: "2",
                EffectivePermissions: ["BanMembers", "ManageGuild"],
                HighestRoleId: "20",
                HighestRoleName: "Steward",
                HighestRolePosition: 10,
                HasAdministrator: false));
    }

    private sealed class BulkBanHttpHandler(ImmutableArray<string> successfulUserIds) : HttpMessageHandler
    {
        private readonly ImmutableHashSet<string> _successfulUserIds =
            successfulUserIds.ToImmutableHashSet(StringComparer.Ordinal);
        private readonly HashSet<string> _bannedUserIds = new(StringComparer.Ordinal);

        internal int MutationCount { get; private set; }

        internal JsonObject? LastMutationBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                var userId = path[(path.LastIndexOf('/') + 1)..];
                return _bannedUserIds.Contains(userId)
                    ? JsonResponse(
                        HttpStatusCode.OK,
                        new JsonObject
                        {
                            ["reason"] = null,
                            ["user"] = new JsonObject { ["id"] = userId }
                        }.ToJsonString())
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v10/guilds/{GuildId}/bulk-ban", path);
            MutationCount++;
            LastMutationBody = JsonNode.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var requested = LastMutationBody["user_ids"]!.AsArray()
                .Select(value => value!.GetValue<string>())
                .ToArray();
            var banned = requested.Where(_successfulUserIds.Contains).ToArray();
            var failed = requested.Where(userId => !_successfulUserIds.Contains(userId)).ToArray();
            _bannedUserIds.UnionWith(banned);
            return JsonResponse(
                HttpStatusCode.OK,
                new JsonObject
                {
                    ["banned_users"] = new JsonArray(
                        banned.Select(userId => (JsonNode?)JsonValue.Create(userId)).ToArray()),
                    ["failed_users"] = new JsonArray(
                        failed.Select(userId => (JsonNode?)JsonValue.Create(userId)).ToArray())
                }.ToJsonString());
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
