using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class GuildAccessControlsTests
{
    [Fact]
    public async Task Update_PreservesImmutableFeaturesAndVerifiesMutableFlags()
    {
        await using var fixture = CreateFixture();
        var current = ImmutableArray.Create("COMMUNITY", "ROLE_ICONS");
        var input = new UpdateGuildAccessControlsInput(
            ResourceStateCanonicalizer.ComputeObjectDigest(current.Order(StringComparer.Ordinal).ToImmutableArray()),
            InvitesDisabled: true,
            RaidAlertsDisabled: true);
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "update_guild_access_controls",
            "123",
            "pause access and raid alerts",
            input);

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);
        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            preparation.BeforeStateJson,
            preparation.DesiredStateJson,
            priorReceiptJson: null,
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.DesiredState, observation.Kind);
        var features = fixture.Http.LastBody!["features"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        Assert.Equal(
            ["COMMUNITY", "INVITES_DISABLED", "RAID_ALERTS_DISABLED", "ROLE_ICONS"],
            features);
        Assert.Equal(1, fixture.Http.MutationCount);
    }

    [Fact]
    public async Task Update_StaleFeatureDigestRefusesBeforeWrite()
    {
        await using var fixture = CreateFixture();
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "update_guild_access_controls",
            "123",
            "stale update",
            new UpdateGuildAccessControlsInput(new string('0', 64), true, false));

        var exception = await Assert.ThrowsAsync<StewardException>(() =>
            fixture.Handler.PrepareAsync(invocation, CancellationToken.None));

        Assert.Equal(StewardErrorCodes.StateConflict, exception.Code);
        Assert.Equal(0, fixture.Http.MutationCount);
    }

    private static Fixture CreateFixture()
    {
        var options = DiscordOptions.FromValues("123", "test-token");
        var session = new DiscordRestSession(options);
        var http = new GuildHandler();
        var transport = new DiscordApiTransport(options, session, http);
        var handler = new CommunityMutationHandler(
            transport,
            guildReader: null!,
            extendedReader: null!,
            catalogReader: null!,
            assetVault: null!,
            StewardRuntimeSettings.FromValues("123", "test-token"),
            TimeProvider.System);
        return new Fixture(handler, transport, session, http);
    }

    private sealed class Fixture(
        CommunityMutationHandler handler,
        DiscordApiTransport transport,
        DiscordRestSession session,
        GuildHandler http) : IAsyncDisposable
    {
        internal CommunityMutationHandler Handler { get; } = handler;

        internal GuildHandler Http { get; } = http;

        public async ValueTask DisposeAsync()
        {
            transport.Dispose();
            await session.DisposeAsync();
        }
    }

    private sealed class GuildHandler : HttpMessageHandler
    {
        private readonly JsonObject _state = JsonNode.Parse("""
            {
              "id":"123",
              "name":"Guild",
              "features":["COMMUNITY","ROLE_ICONS"]
            }
            """)!.AsObject();

        internal int MutationCount { get; private set; }

        internal JsonObject? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(_state.ToJsonString());
            }

            MutationCount++;
            LastBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            _state["features"] = LastBody["features"]!.DeepClone();
            return JsonResponse(_state.ToJsonString());
        }

        private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
