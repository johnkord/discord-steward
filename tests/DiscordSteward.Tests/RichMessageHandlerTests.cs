using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;
using DiscordSteward.Operations;
using DiscordSteward.Secrets;

namespace DiscordSteward.Tests;

public sealed class RichMessageHandlerTests
{
    [Theory]
    [InlineData("invented", "valid-nonce")]
    [InlineData("new", "")]
    [InlineData("new", "12345678901234567890123456")]
    public async Task SendMessage_InvalidModeOrNonceFailsBeforeDiscordRequest(string mode, string nonce)
    {
        var options = DiscordOptions.FromValues("123", "test-token");
        await using var session = new DiscordRestSession(options);
        var http = new RecordingHandler(new Dictionary<string, string>());
        using var transport = new DiscordApiTransport(options, session, http);
        var handler = new MessageMutationHandler(
            transport,
            messageReader: null!,
            new FakeInspector(),
            assetVault: null!,
            StewardRuntimeSettings.FromValues("123", "test-token"),
            TimeProvider.System);
        var input = new SendMessageInput(
            ExpectedChannelStateDigest: "unused",
            Mode: mode,
            Content: "Hello",
            SourceChannelId: null,
            SourceMessageId: null,
            ExpectedSourceMessageStateDigest: null,
            new AllowedMentionsInput([], [], Everyone: false, RepliedUser: false),
            AssetIds: [],
            Nonce: nonce,
            SuppressNotifications: false);
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "send_message",
            "456",
            "invalid preflight",
            input);

        var exception = await Assert.ThrowsAsync<StewardException>(() =>
            handler.PrepareAsync(invocation, CancellationToken.None));

        Assert.Equal(StewardErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal(0, http.RequestCount);
    }

    [Fact]
    public async Task SendMessage_WritesRichCreateOnlyFieldsAndNamedFlags()
    {
        var options = DiscordOptions.FromValues("123", "test-token");
        await using var session = new DiscordRestSession(options);
        var http = new RecordingHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v10/channels/456"] = ChannelJson(type: 0),
            ["/api/v10/guilds/123/stickers/777"] = """{"id":"777","guild_id":"123"}"""
        });
        using var transport = new DiscordApiTransport(options, session, http);
        var handler = new MessageMutationHandler(
            transport,
            messageReader: null!,
            new FakeInspector(),
            assetVault: null!,
            StewardRuntimeSettings.FromValues("123", "test-token"),
            TimeProvider.System);
        using var channelDocument = JsonDocument.Parse(ChannelJson(type: 0));
        var channel = ChannelApiState.Parse(channelDocument.RootElement);
        var input = new SendMessageInput(
            channel.ComputeDigest(),
            Mode: "new",
            Content: "Hello",
            SourceChannelId: null,
            SourceMessageId: null,
            ExpectedSourceMessageStateDigest: null,
            new AllowedMentionsInput([], [], Everyone: false, RepliedUser: false),
            AssetIds: [],
            Nonce: "rich-message",
            SuppressNotifications: true,
            Embeds: [new RichEmbedInput(Title: "Status", Description: "Ready", ColorHex: "#00AA55")],
            StickerIds: ["777"],
            Tts: true,
            SuppressEmbeds: false,
            SharedClientTheme: new SharedClientThemeInput(["5865F2"], 45, 60, "dark"));
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "send_message",
            "456",
            "send rich message",
            input);

        var preparation = await handler.PrepareAsync(invocation, CancellationToken.None);
        await handler.WriteAsync(invocation, preparation, CancellationToken.None);

        var body = Assert.IsType<JsonObject>(http.LastMutationBody);
        Assert.Equal(HttpMethod.Post, http.LastMutationMethod);
        Assert.Equal("/api/v10/channels/456/messages", http.LastMutationRoute);
        Assert.Equal(4_096, body["flags"]!.GetValue<int>());
        Assert.True(body["tts"]!.GetValue<bool>());
        Assert.Equal("777", body["sticker_ids"]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal("Status", body["embeds"]!.AsArray().Single()!["title"]!.GetValue<string>());
        Assert.Equal(45, body["shared_client_theme"]!["gradient_angle"]!.GetValue<int>());
    }

    [Fact]
    public async Task SendWebhook_WritesIdentityEmbedPollAndSuppressionFields()
    {
        var options = DiscordOptions.FromValues("123", "test-token");
        await using var session = new DiscordRestSession(options);
        var http = new RecordingHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v10/channels/456"] = ChannelJson(type: 0)
        });
        using var transport = new DiscordApiTransport(options, session, http);
        var webhook = new WebhookView(
            "321",
            "123",
            "456",
            "Steward Hook",
            "Incoming",
            ApplicationId: null,
            FollowedChannelId: null,
            FollowedChannelName: null,
            DateTimeOffset.UnixEpoch,
            AvatarHash: null,
            StateDigest: new string('a', 64));
        var extendedReader = DispatchProxy.Create<IDiscordExtendedReader, ExtendedReaderProxy>();
        ((ExtendedReaderProxy)(object)extendedReader).Webhook = webhook;
        var secret = new StoredWebhookSecret(
            "whsec_test",
            "321",
            "456",
            "secret-token",
            DateTimeOffset.UnixEpoch);
        var handler = new IntegrationMutationHandler(
            transport,
            extendedReader,
            catalogReader: null!,
            messageReader: null!,
            guildReader: null!,
            assetVault: null!,
            new FakeWebhookVault(secret),
            StewardRuntimeSettings.FromValues("123", "test-token"));
        var input = new SendWebhookMessageInput(
            secret.VaultId,
            webhook.StateDigest,
            ThreadId: null,
            Content: "Deployment",
            new AllowedMentionsInput([], [], Everyone: false, RepliedUser: false),
            AssetIds: [],
            UsernameOverride: "Release Bot",
            AvatarUrlOverride: "https://example.com/avatar.png",
            Tts: false,
            Embeds: [new RichEmbedInput(Title: "Version 2")],
            Poll: new WebhookPollInput(
                "Ship it?",
                [new PollAnswerInput("Yes", null, null), new PollAnswerInput("No", null, null)],
                DurationHours: 24,
                AllowMultiselect: false),
            SuppressNotifications: true,
                SuppressEmbeds: false);
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "send_webhook_message",
            "123",
            "send rich webhook",
            input);

        var preparation = await handler.PrepareAsync(invocation, CancellationToken.None);
        await handler.WriteAsync(invocation, preparation, CancellationToken.None);

        var body = Assert.IsType<JsonObject>(http.LastMutationBody);
        Assert.Equal("/api/v10/webhooks/321/secret-token?wait=true", http.LastMutationRoute);
        Assert.Equal("Release Bot", body["username"]!.GetValue<string>());
        Assert.Equal("https://example.com/avatar.png", body["avatar_url"]!.GetValue<string>());
        Assert.Equal(4_096, body["flags"]!.GetValue<int>());
        Assert.Equal("Version 2", body["embeds"]!.AsArray().Single()!["title"]!.GetValue<string>());
        Assert.Equal("Ship it?", body["poll"]!["question"]!["text"]!.GetValue<string>());
        Assert.Equal(2, body["poll"]!["answers"]!.AsArray().Count);
    }

    [Fact]
    public async Task SendWebhook_CreatesTaggedForumThreadWithStaticComponents()
    {
        var options = DiscordOptions.FromValues("123", "test-token");
        await using var session = new DiscordRestSession(options);
        var forumJson = """
            {
              "id":"456",
              "guild_id":"123",
              "type":15,
              "name":"forum",
              "position":0,
              "parent_id":null,
              "permission_overwrites":[],
              "flags":0,
              "available_tags":[{"id":"700","name":"Validated","moderated":false,"emoji_id":null,"emoji_name":null}]
            }
            """;
        var http = new RecordingHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v10/channels/456"] = forumJson
        });
        using var transport = new DiscordApiTransport(options, session, http);
        var webhook = new WebhookView(
            "321",
            "123",
            "456",
            "Steward Hook",
            "Incoming",
            ApplicationId: null,
            FollowedChannelId: null,
            FollowedChannelName: null,
            DateTimeOffset.UnixEpoch,
            AvatarHash: null,
            StateDigest: new string('a', 64));
        var extendedReader = DispatchProxy.Create<IDiscordExtendedReader, ExtendedReaderProxy>();
        ((ExtendedReaderProxy)(object)extendedReader).Webhook = webhook;
        var secret = new StoredWebhookSecret("whsec_test", "321", "456", "secret-token", DateTimeOffset.UnixEpoch);
        using var forumDocument = JsonDocument.Parse(forumJson);
        var forum = ChannelApiState.Parse(forumDocument.RootElement);
        var handler = new IntegrationMutationHandler(
            transport,
            extendedReader,
            catalogReader: null!,
            messageReader: null!,
            guildReader: null!,
            assetVault: null!,
            new FakeWebhookVault(secret),
            StewardRuntimeSettings.FromValues("123", "test-token"));
        var input = new SendWebhookMessageInput(
            secret.VaultId,
            webhook.StateDigest,
            ThreadId: null,
            Content: null,
            new AllowedMentionsInput([], [], Everyone: false, RepliedUser: false),
            AssetIds: [],
            Components: [new StaticComponentInput("text_display", Content: "Static forum post")],
            ThreadName: "Validated thread",
            ExpectedAvailableTagsDigest: forum.ComputeAvailableTagsDigest(),
            AppliedTagIds: ["700"]);
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "send_webhook_message",
            "123",
            "create static forum thread",
            input);

        var preparation = await handler.PrepareAsync(invocation, CancellationToken.None);
        await handler.WriteAsync(invocation, preparation, CancellationToken.None);

        var body = Assert.IsType<JsonObject>(http.LastMutationBody);
        Assert.Equal("/api/v10/webhooks/321/secret-token?wait=true&with_components=true", http.LastMutationRoute);
        Assert.Equal("Validated thread", body["thread_name"]!.GetValue<string>());
        Assert.Equal("700", body["applied_tags"]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal(10, body["components"]!.AsArray().Single()!["type"]!.GetValue<int>());
    }

    private static string ChannelJson(int type) => $$"""
        {
          "id":"456",
          "guild_id":"123",
          "type":{{type}},
          "name":"general",
          "position":0,
          "parent_id":null,
          "permission_overwrites":[]
        }
        """;

    private sealed class FakeInspector : IDiscordBotInspector
    {
        public Task<BotEffectivePermissions> GetEffectivePermissionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BotEffectivePermissions(
                "123",
                "999",
                ["SendMessages", "SendTTSMessages", "ViewChannel"],
                "888",
                "Steward",
                10,
                HasAdministrator: false));
    }

    private sealed class FakeWebhookVault(StoredWebhookSecret secret) : IWebhookSecretVault
    {
        public Task<StoredWebhookSecret> GetAsync(string vaultId, CancellationToken cancellationToken) =>
            Task.FromResult(vaultId == secret.VaultId ? secret : throw new InvalidOperationException());

        public Task<StoredWebhookSecret> StoreAsync(
            string webhookId,
            string channelId,
            string token,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateChannelAsync(string vaultId, string channelId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string vaultId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1852:Seal internal types",
        Justification = "DispatchProxy generates a runtime subtype.")]
    private class ExtendedReaderProxy : DispatchProxy
    {
        internal WebhookView Webhook { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IDiscordExtendedReader.GetWebhookAsync) => Task.FromResult(Webhook),
            _ => throw new NotSupportedException(targetMethod?.Name)
        };
    }

    private sealed class RecordingHandler(IReadOnlyDictionary<string, string> reads) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        internal HttpMethod? LastMutationMethod { get; private set; }

        internal string? LastMutationRoute { get; private set; }

        internal JsonObject? LastMutationBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var route = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get)
            {
                return reads.TryGetValue(route, out var body)
                    ? JsonResponse(body)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            LastMutationMethod = request.Method;
            LastMutationRoute = route;
            LastMutationBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            return JsonResponse("""{"id":"1000","channel_id":"456"}""");
        }

        private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
