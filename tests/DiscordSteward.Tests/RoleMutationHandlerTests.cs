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

public sealed class RoleMutationHandlerTests
{
    [Fact]
    public async Task CreateRole_WritesFullGradientColorsObject()
    {
        await using var fixture = CreateFixture(enhancedColors: true);
        var input = new CreateRoleInput(
            ResourceStateCanonicalizer.ComputeRolesDigest(fixture.Snapshot.Roles),
            "Gradient",
            Permissions: [],
            ColorHex: "#112233",
            IsHoisted: false,
            IsMentionable: false,
            IconAssetId: null,
            UnicodeEmoji: null,
            SecondaryColorHex: "#445566");
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "create_role",
            fixture.Snapshot.Id,
            "create gradient role",
            input);

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);

        var colors = Assert.IsType<JsonObject>(fixture.Http.LastBody!["colors"]);
        Assert.Equal(0x112233, colors["primary_color"]!.GetValue<int>());
        Assert.Equal(0x445566, colors["secondary_color"]!.GetValue<int>());
        Assert.Null(colors["tertiary_color"]);
        Assert.False(fixture.Http.LastBody.ContainsKey("color"));
    }

    [Fact]
    public async Task UpdateAppearance_PreservesPrimaryWhenSettingSecondary()
    {
        await using var fixture = CreateFixture(enhancedColors: true);
        var role = fixture.Snapshot.Roles.Single(item => !item.IsEveryone);
        var input = new UpdateRoleAppearanceInput(
            role.StateDigest,
            new RoleAppearanceSet(
                Name: null,
                ColorHex: null,
                IsHoisted: null,
                IsMentionable: null,
                IconAssetId: null,
                UnicodeEmoji: null,
                SecondaryColorHex: "#445566"),
            Clear: []);
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "update_role_appearance",
            role.Id,
            "set role gradient",
            input);

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);

        var colors = Assert.IsType<JsonObject>(fixture.Http.LastBody!["colors"]);
        Assert.Equal(0x112233, colors["primary_color"]!.GetValue<int>());
        Assert.Equal(0x445566, colors["secondary_color"]!.GetValue<int>());
    }

    [Fact]
    public async Task CreateRole_RejectsArbitraryTertiaryBeforeWrite()
    {
        await using var fixture = CreateFixture(enhancedColors: true);
        var input = new CreateRoleInput(
            ResourceStateCanonicalizer.ComputeRolesDigest(fixture.Snapshot.Roles),
            "Invalid Holographic",
            Permissions: [],
            ColorHex: "#112233",
            IsHoisted: false,
            IsMentionable: false,
            IconAssetId: null,
            UnicodeEmoji: null,
            SecondaryColorHex: "#445566",
            TertiaryColorHex: "#778899");
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "create_role",
            fixture.Snapshot.Id,
            "invalid tertiary",
            input);

        var exception = await Assert.ThrowsAsync<StewardException>(() =>
            fixture.Handler.PrepareAsync(invocation, CancellationToken.None));

        Assert.Equal(StewardErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal(0, fixture.Http.MutationCount);
    }

    [Fact]
    public async Task CreateRole_UnrestrictedAutonomyAllowsAdministrator()
    {
        await using var fixture = CreateFixture(
            enhancedColors: false,
            authorizationMode: StewardAuthorizationMode.UnrestrictedAutonomy);
        var input = new CreateRoleInput(
            ResourceStateCanonicalizer.ComputeRolesDigest(fixture.Snapshot.Roles),
            "Administrator",
            Permissions: ["Administrator"],
            ColorHex: null,
            IsHoisted: false,
            IsMentionable: false,
            IconAssetId: null,
            UnicodeEmoji: null);
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "create_role",
            fixture.Snapshot.Id,
            "create administrator role",
            input);

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);

        Assert.Equal(1, fixture.Http.MutationCount);
    }

    private static Fixture CreateFixture(
        bool enhancedColors,
        StewardAuthorizationMode authorizationMode = StewardAuthorizationMode.InteractiveApproval)
    {
        var everyone = RoleSnapshot.Create(
            123,
            123,
            "@everyone",
            0,
            isManaged: false,
            isEveryone: true,
            new ResourceProtection(false, []),
            permissions: [],
            colorHex: null,
            isHoisted: false,
            isMentionable: false);
        var role = RoleSnapshot.Create(
            123,
            456,
            "Existing",
            1,
            isManaged: false,
            isEveryone: false,
            new ResourceProtection(false, []),
            permissions: [],
            colorHex: "#112233",
            isHoisted: false,
            isMentionable: false);
        var snapshot = GuildSnapshot.Create(
            123,
            "Guild",
            ownerId: 1,
            description: null,
            DateTimeOffset.UnixEpoch,
            enhancedColors ? ["EnhancedRoleColors"] : [],
            [everyone, role],
            categories: [],
            channels: []);
        var options = DiscordOptions.FromValues("123", "test-token");
        var session = new DiscordRestSession(options);
        var http = new RecordingHandler();
        var transport = new DiscordApiTransport(options, session, http);
        var handler = new RoleMutationHandler(
            transport,
            new InMemoryDiscordGuildReader(snapshot),
            memberReader: null!,
            new FakeInspector(),
            analysisService: null!,
            assetVault: null!,
            StewardPolicy.Create([], [], [], authorizationMode),
            StewardRuntimeSettings.FromValues("123", "test-token"));
        return new Fixture(handler, snapshot, transport, session, http);
    }

    private sealed class FakeInspector : IDiscordBotInspector
    {
        public Task<BotEffectivePermissions> GetEffectivePermissionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BotEffectivePermissions(
                "123",
                "999",
                ["ManageRoles"],
                "888",
                "Steward",
                10,
                HasAdministrator: false));
    }

    private sealed class Fixture(
        RoleMutationHandler handler,
        GuildSnapshot snapshot,
        DiscordApiTransport transport,
        DiscordRestSession session,
        RecordingHandler http) : IAsyncDisposable
    {
        internal RoleMutationHandler Handler { get; } = handler;

        internal GuildSnapshot Snapshot { get; } = snapshot;

        internal RecordingHandler Http { get; } = http;

        public async ValueTask DisposeAsync()
        {
            transport.Dispose();
            await session.DisposeAsync();
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal int MutationCount { get; private set; }

        internal JsonObject? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            MutationCount++;
            LastBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"777"}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
