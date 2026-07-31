using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class BotGuildProfileTests
{
    [Fact]
    public async Task Reader_ParsesProfileAndComputesStateDigest()
    {
        await using var fixture = CreateReaderFixture();

        var profile = await fixture.Reader.GetBotGuildProfileAsync(CancellationToken.None);

        Assert.Equal("999", profile.BotUserId);
        Assert.Equal("Steward", profile.Nickname);
        Assert.Equal("avatar-hash", profile.AvatarHash);
        Assert.Equal("banner-hash", profile.BannerHash);
        Assert.Equal("Guild bio", profile.Bio);
        Assert.Equal(64, profile.StateDigest.Length);
        Assert.Equal("/api/v10/guilds/123/members/999", fixture.Http.Route);
    }

    [Fact]
    public async Task Update_WritesSetAndClearWithApprovedImageAsset()
    {
        var profile = new BotGuildProfileView(
            "999",
            "Steward",
            "avatar-hash",
            "banner-hash",
            "Guild bio");
        profile = profile with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(profile) };
        var options = DiscordOptions.FromValues("123", "test-token");
        await using var session = new DiscordRestSession(options);
        var http = new ProfileHandler();
        using var transport = new DiscordApiTransport(options, session, http);
        var reader = DispatchProxy.Create<IDiscordExtendedReader, ProfileReaderProxy>();
        ((ProfileReaderProxy)(object)reader).Profile = profile;
        var assetVault = new FakeAssetVault();
        var handler = new BotGuildProfileMutationHandler(
            transport,
            reader,
            assetVault,
            StewardRuntimeSettings.FromValues("123", "test-token"));
        var input = new UpdateBotGuildProfileInput(
            profile.StateDigest,
            new BotGuildProfileSet(
                Nickname: "Steward Two",
                AvatarAssetId: "asset_image",
                BannerAssetId: null),
            Clear: ["banner"]);
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "update_bot_guild_profile",
            "123",
            "update profile",
            input);

        var preparation = await handler.PrepareAsync(invocation, CancellationToken.None);
        await handler.WriteAsync(invocation, preparation, CancellationToken.None);

        var body = Assert.IsType<JsonObject>(http.Body);
        Assert.Equal("Steward Two", body["nick"]!.GetValue<string>());
        Assert.False(body.ContainsKey("bio"));
        Assert.StartsWith("data:image/png;base64,", body["avatar"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Null(body["banner"]);
        Assert.Equal("/api/v10/guilds/123/members/@me", http.Route);
    }

    private static ReaderFixture CreateReaderFixture()
    {
        var options = DiscordOptions.FromValues("123", "test-token");
        var session = new DiscordRestSession(options);
        var http = new ProfileHandler(readOnly: true);
        var transport = new DiscordApiTransport(options, session, http);
        var reader = new DiscordExtendedReader(
            session,
            httpApi: null!,
            channelStateReader: null!,
            new FakeBotInspector(),
            transport,
            StewardRuntimeSettings.FromValues("123", "test-token"));
        return new ReaderFixture(reader, transport, session, http);
    }

    private sealed class ReaderFixture(
        DiscordExtendedReader reader,
        DiscordApiTransport transport,
        DiscordRestSession session,
        ProfileHandler http) : IAsyncDisposable
    {
        internal DiscordExtendedReader Reader { get; } = reader;

        internal ProfileHandler Http { get; } = http;

        public async ValueTask DisposeAsync()
        {
            transport.Dispose();
            await session.DisposeAsync();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1852:Seal internal types",
        Justification = "DispatchProxy generates a runtime subtype.")]
    private class ProfileReaderProxy : DispatchProxy
    {
        internal BotGuildProfileView Profile { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IDiscordExtendedReader.GetBotGuildProfileAsync) => Task.FromResult(Profile),
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }

    private sealed class FakeAssetVault : IAssetVault
    {
        private readonly string _path;

        public FakeAssetVault()
        {
            _path = Path.GetTempFileName();
            File.WriteAllBytes(_path, [137, 80, 78, 71]);
        }

        public Task<AssetView> GetAsync(string assetId, CancellationToken cancellationToken) => Task.FromResult(new AssetView(
            "asset_image",
            "image.png",
            "image/png",
            4,
            new string('a', 64),
            DateTimeOffset.UnixEpoch));

        public Task<string> GetContentPathAsync(string assetId, CancellationToken cancellationToken) => Task.FromResult(_path);

        public Task<AssetCatalogView> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AssetView> RegisterAsync(string candidateId, string expectedDigest, string mediaType, long byteLength, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AssetView> DeleteAsync(string assetId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeBotInspector : IDiscordBotInspector
    {
        public Task<BotEffectivePermissions> GetEffectivePermissionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BotEffectivePermissions(
                GuildId: "123",
                BotUserId: "999",
                EffectivePermissions: [],
                HighestRoleId: null,
                HighestRoleName: null,
                HighestRolePosition: null,
                HasAdministrator: false));
    }

    private sealed class ProfileHandler(bool readOnly = false) : HttpMessageHandler
    {
        internal string? Route { get; private set; }

        internal JsonObject? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Route = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get || readOnly)
            {
                return JsonResponse("""
                    {
                      "nick":"Steward",
                      "avatar":"avatar-hash",
                      "banner":"banner-hash",
                      "bio":"Guild bio"
                    }
                    """);
            }

            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            return JsonResponse("""{"user":{"id":"999"},"nick":"Steward Two","avatar":"new-avatar","banner":null,"bio":"Guild bio"}""");
        }

        private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
