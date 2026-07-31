using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;

namespace DiscordSteward.Tests;

public sealed class DiscordReferenceReadTests
{
    [Fact]
    public async Task VoiceRegionsAndExactThreadMember_UseTypedExactRoutes()
    {
        await using var fixture = CreateFixture(new Dictionary<string, Response>(StringComparer.Ordinal)
        {
            ["/api/v10/voice/regions"] = Json("""[{"id":"us-west","name":"US West","optimal":true,"deprecated":false,"custom":false}]"""),
            ["/api/v10/guilds/123/regions"] = Json("""[{"id":"vip-us","name":"VIP US","optimal":false,"deprecated":false,"custom":true}]"""),
            ["/api/v10/channels/456/thread-members/789?with_member=false"] = Json("""{"id":"456","user_id":"789","join_timestamp":"2026-07-26T12:00:00Z","flags":0}""")
        });

        var global = await fixture.Reader.ListVoiceRegionsAsync("global", CancellationToken.None);
        var guild = await fixture.Reader.ListVoiceRegionsAsync("guild", CancellationToken.None);
        var member = await fixture.Reader.GetThreadMemberAsync("456", "789", CancellationToken.None);

        Assert.Equal("global", Assert.Single(global).Scope);
        Assert.Equal("vip-us", Assert.Single(guild).Id);
        Assert.Equal("789", member.UserId);
        Assert.Contains("/api/v10/channels/456/thread-members/789?with_member=false", fixture.Http.Requests);
    }

    [Fact]
    public async Task InvitesStickerPacksAndDefaultSounds_AreBoundedAndSanitized()
    {
        var longTag = new string('x', 250);
        var packJson = $$"""
            {
              "id":"1001",
              "name":"Standard Pack",
              "description":"Public stickers",
              "sku_id":"1002",
              "cover_sticker_id":"1003",
              "banner_asset_id":null,
              "stickers":[{
                "id":"1003",
                "pack_id":"1001",
                "name":"Wave",
                "description":"A wave",
                "tags":"{{longTag}}, wave",
                "format_type":1,
                "sort_value":0
              }]
            }
            """;
        await using var fixture = CreateFixture(new Dictionary<string, Response>(StringComparer.Ordinal)
        {
            ["/api/v10/channels/456/invites"] = Json("""
                [{
                  "code":"abc_123",
                  "guild":{"id":"123"},
                  "channel":{"id":"456","name":"general","type":0},
                  "expires_at":null,
                  "temporary":false,
                  "max_age":3600,
                  "max_uses":10,
                  "uses":2,
                  "created_at":"2026-07-26T12:00:00Z",
                  "role_ids":["777"]
                }]
                """),
            ["/api/v10/sticker-packs"] = Json($$"""{"sticker_packs":[{{packJson}}]}"""),
            ["/api/v10/sticker-packs/1001"] = Json(packJson),
            ["/api/v10/soundboard-default-sounds"] = Json("""[{"sound_id":"2001","name":"Quack","volume":0.75,"emoji_id":null,"emoji_name":"Q"}]""")
        });

        var invites = await fixture.Reader.ListChannelInvitesAsync("456", CancellationToken.None);
        var packs = await fixture.Reader.ListStickerPacksAsync(CancellationToken.None);
        var pack = await fixture.Reader.GetStickerPackAsync("1001", CancellationToken.None);
        var sounds = await fixture.Reader.ListDefaultSoundboardSoundsAsync(CancellationToken.None);

        var invite = Assert.Single(invites);
        Assert.Equal("abc_123", invite.Code);
        Assert.Equal("777", Assert.Single(invite.GrantedRoleIds));
        Assert.Equal(64, invite.StateDigest.Length);
        var sticker = Assert.Single(Assert.Single(packs).Stickers);
        Assert.Equal("Wave", sticker.Name);
        Assert.Contains(longTag, sticker.Tags);
        Assert.Equal("1001", pack.Id);
        Assert.Equal(0.75, Assert.Single(sounds).Volume);
    }

    [Fact]
    public async Task GuildPreviewAndWidgetImage_ReturnAggregateAndDigestBoundBinary()
    {
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        await using var fixture = CreateFixture(new Dictionary<string, Response>(StringComparer.Ordinal)
        {
            ["/api/v10/guilds/123/preview"] = Json("""
                {
                  "id":"123",
                  "name":"Preview Guild",
                  "description":"Description",
                  "icon":"icon-hash",
                  "splash":null,
                  "discovery_splash":null,
                  "features":["COMMUNITY"],
                  "approximate_member_count":42,
                  "approximate_presence_count":7,
                  "emojis":[{}],
                  "stickers":[{},{}]
                }
                """),
            ["/api/v10/guilds/123/widget.png?style=banner2"] = Binary(png, "image/png")
        });

        var preview = await fixture.Reader.GetGuildPreviewAsync(CancellationToken.None);
        var widget = await fixture.Reader.GetGuildWidgetImageAsync("banner2", CancellationToken.None);

        Assert.Equal(42, preview.ApproximateMemberCount);
        Assert.Equal(1, preview.EmojiCount);
        Assert.Equal(2, preview.StickerCount);
        Assert.Equal(png.Length, widget.ByteLength);
        Assert.Equal(Convert.ToBase64String(png), widget.Base64Png);
        Assert.Equal(64, widget.Sha256.Length);
    }

    private static Fixture CreateFixture(IReadOnlyDictionary<string, Response> responses)
    {
        var options = DiscordOptions.FromValues("123", "test-token");
        var session = new DiscordRestSession(options);
        var http = new ScriptedHandler(responses);
        var transport = new DiscordApiTransport(options, session, http);
        var reader = new DiscordExtendedReader(
            session,
            httpApi: null!,
            channelStateReader: null!,
            botInspector: null!,
            transport,
            StewardRuntimeSettings.FromValues("123", "test-token"));
        return new Fixture(reader, transport, session, http);
    }

    private static Response Json(string body) => new(body, "application/json", IsBinary: false);

    private static Response Binary(byte[] body, string mediaType) =>
        new(Convert.ToBase64String(body), mediaType, IsBinary: true);

    private sealed record Response(string Body, string MediaType, bool IsBinary);

    private sealed class Fixture(
        DiscordExtendedReader reader,
        DiscordApiTransport transport,
        DiscordRestSession session,
        ScriptedHandler http) : IAsyncDisposable
    {
        internal DiscordExtendedReader Reader { get; } = reader;

        internal ScriptedHandler Http { get; } = http;

        public async ValueTask DisposeAsync()
        {
            transport.Dispose();
            await session.DisposeAsync();
        }
    }

    private sealed class ScriptedHandler(IReadOnlyDictionary<string, Response> responses) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            var route = request.RequestUri!.PathAndQuery;
            Requests.Add(route);
            if (!responses.TryGetValue(route, out var scripted))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            HttpContent content;
            if (scripted.IsBinary)
            {
                content = new ByteArrayContent(Convert.FromBase64String(scripted.Body));
                content.Headers.ContentType = new MediaTypeHeaderValue(scripted.MediaType);
            }
            else
            {
                content = new StringContent(scripted.Body, Encoding.UTF8, scripted.MediaType);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
