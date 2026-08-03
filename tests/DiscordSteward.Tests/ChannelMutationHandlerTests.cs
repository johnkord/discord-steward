using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class ChannelMutationHandlerTests
{
    private const string GuildId = "123";
    private const string ChannelId = "456";

    [Fact]
    public async Task UpdateVoiceChannel_WritesSparseTypedPatchAndVerifies()
    {
        await using var fixture = CreateFixture(ChannelState(
            type: 2,
            additional: """
                "bitrate": 64000,
                "user_limit": 0,
                "rtc_region": null,
                "video_quality_mode": 1,
                "rate_limit_per_user": 0
                """));
        var invocation = UpdateInvocation(
            fixture.CurrentState(),
            EmptySet() with
            {
                Bitrate = 96000,
                UserLimit = 12,
                RtcRegion = "us-west",
                VideoQualityMode = 2
            });

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
            ["bitrate", "rtc_region", "user_limit", "video_quality_mode"],
            body.Select(property => property.Key).Order(StringComparer.Ordinal));
        Assert.Equal(96000, body["bitrate"]!.GetValue<int>());
        Assert.Equal("us-west", body["rtc_region"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateForumChannel_VerifiesGeneratedTagIdsAndPreservesUnrelatedFlags()
    {
        await using var fixture = CreateFixture(ChannelState(
            type: 15,
            additional: """
                "topic": "Support",
                "nsfw": false,
                "rate_limit_per_user": 0,
                "default_auto_archive_duration": 1440,
                "default_thread_rate_limit_per_user": 0,
                "default_sort_order": 0,
                "default_forum_layout": 1,
                "flags": 2,
                "available_tags": []
                """));
        var invocation = UpdateInvocation(
            fixture.CurrentState(),
            EmptySet() with
            {
                AvailableTags = ImmutableArray.Create(new ForumTagInput(
                    Id: null,
                    Name: "Resolved",
                    Moderated: true,
                    EmojiId: null,
                    EmojiName: "\u2705")),
                DefaultReaction = new DefaultReactionInput(EmojiId: null, EmojiName: "\u2705"),
                RequireTag = true
            });

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);
        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            preparation.BeforeStateJson,
            preparation.DesiredStateJson,
            priorReceiptJson: null,
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.DesiredState, observation.Kind);
        var body = Assert.IsType<JsonObject>(fixture.Http.LastMutationBody);
        Assert.Equal(18, body["flags"]!.GetValue<long>());
        var tag = body["available_tags"]!.AsArray().Single()!.AsObject();
        Assert.False(tag.ContainsKey("id"));
        Assert.Equal("Resolved", tag["name"]!.GetValue<string>());
        Assert.Equal("900", fixture.Http.State["available_tags"]!.AsArray().Single()!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateTextChannel_RejectsForumFieldsBeforeMutation()
    {
        await using var fixture = CreateFixture(ChannelState(
            type: 0,
            additional: """
                "topic": null,
                "nsfw": false,
                "rate_limit_per_user": 0
                """));
        var invocation = UpdateInvocation(
            fixture.CurrentState(),
            EmptySet() with { AvailableTags = [] });

        var exception = await Assert.ThrowsAsync<StewardException>(() =>
            fixture.Handler.PrepareAsync(invocation, CancellationToken.None));

        Assert.Equal(StewardErrorCodes.InvalidArgument, exception.Code);
        Assert.Equal(0, fixture.Http.MutationCount);
    }

    [Fact]
    public async Task UpdateMediaChannel_SetsNamedDownloadFlagWithoutRawBitInput()
    {
        await using var fixture = CreateFixture(ChannelState(
            type: 16,
            additional: """
                "topic": "Media",
                "nsfw": false,
                "rate_limit_per_user": 0,
                "default_auto_archive_duration": 1440,
                "default_thread_rate_limit_per_user": 0,
                "default_sort_order": 0,
                "flags": 0,
                "available_tags": []
                """));
        var invocation = UpdateInvocation(
            fixture.CurrentState(),
            EmptySet() with { HideMediaDownloadOptions = true });

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);
        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            preparation.BeforeStateJson,
            preparation.DesiredStateJson,
            priorReceiptJson: null,
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.DesiredState, observation.Kind);
        Assert.Equal(1L << 15, Assert.IsType<JsonObject>(fixture.Http.LastMutationBody)["flags"]!.GetValue<long>());
    }

    [Fact]
    public async Task TriggerTypingIndicator_UsesReceiptOnlyVerification()
    {
        await using var fixture = CreateFixture(ChannelState(
            type: 0,
            additional: """
                "topic": null,
                "nsfw": false,
                "rate_limit_per_user": 0
                """));
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "trigger_typing_indicator",
            ChannelId,
            "show command progress",
            new TriggerTypingIndicatorInput(fixture.CurrentState().ComputeDigest()));

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        var receipt = await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);
        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            preparation.BeforeStateJson,
            preparation.DesiredStateJson,
            receipt.ReceiptJson,
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.DesiredState, observation.Kind);
        Assert.Equal(1, fixture.Http.MutationCount);
        Assert.Equal(HttpMethod.Post, fixture.Http.LastMutationMethod);
        Assert.Equal($"/api/v10/channels/{ChannelId}/typing", fixture.Http.LastMutationPath);
        Assert.Contains("\"accepted\":true", receipt.ReceiptJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateTextChannel_OmittedFalseAndZeroDefaultsVerifyDesiredState()
    {
        await using var fixture = CreateFixture(ChannelState(
            type: 0,
            additional: """
                "topic": null
                """));
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "create_text_channel",
            GuildId,
            "create a text channel with explicit defaults",
            new { });
        const string desired = """
            {
              "type": 0,
              "name": "general",
              "parentId": null,
              "position": null,
              "topic": null,
              "nsfw": false,
              "rateLimitPerUser": 0,
              "bitrate": null,
              "userLimit": null,
              "rtcRegion": null,
              "videoQualityMode": null,
              "defaultAutoArchiveDuration": null,
              "defaultThreadRateLimitPerUser": null,
              "defaultSortOrder": null,
              "defaultForumLayout": null,
              "permissionOverwrites": [],
              "archived": null,
              "locked": null,
              "invitable": null,
              "autoArchiveDuration": null,
              "appliedTags": []
            }
            """;

        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            beforeStateJson: "{}",
            desired,
            priorReceiptJson: $$"""{"id":"{{ChannelId}}"}""",
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.DesiredState, observation.Kind);
    }

    [Fact]
    public async Task CreateTextChannel_OmittedNonDefaultValueDoesNotVerify()
    {
        await using var fixture = CreateFixture(ChannelState(
            type: 0,
            additional: """
                "topic": null
                """));
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "create_text_channel",
            GuildId,
            "create a text channel with non-default slow mode",
            new { });
        const string desired = """
            {
              "type": 0,
              "name": "general",
              "parentId": null,
              "position": null,
              "topic": null,
              "nsfw": false,
              "rateLimitPerUser": 5,
              "bitrate": null,
              "userLimit": null,
              "rtcRegion": null,
              "videoQualityMode": null,
              "defaultAutoArchiveDuration": null,
              "defaultThreadRateLimitPerUser": null,
              "defaultSortOrder": null,
              "defaultForumLayout": null,
              "permissionOverwrites": [],
              "archived": null,
              "locked": null,
              "invitable": null,
              "autoArchiveDuration": null,
              "appliedTags": []
            }
            """;

        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            beforeStateJson: "{}",
            desired,
            priorReceiptJson: $$"""{"id":"{{ChannelId}}"}""",
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.UnexpectedState, observation.Kind);
    }

    [Fact]
    public async Task SetVoiceChannelStatus_AcceptedButOmittedFromReadbackIsUnverifiable()
    {
        await using var fixture = CreateFixture(ChannelState(
            type: 2,
            additional: """
                "bitrate": 64000,
                "user_limit": 0,
                "rtc_region": null,
                "video_quality_mode": 1,
                "status": null
                """));
        var invocation = CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "set_voice_channel_status",
            ChannelId,
            "set a visible voice status",
            new SetVoiceChannelStatusInput(fixture.CurrentState().ComputeDigest(), "Sonic hearing"));

        var preparation = await fixture.Handler.PrepareAsync(invocation, CancellationToken.None);
        var receipt = await fixture.Handler.WriteAsync(invocation, preparation, CancellationToken.None);
        var observation = await fixture.Handler.ObserveAsync(
            invocation,
            preparation.BeforeStateJson,
            preparation.DesiredStateJson,
            receipt.ReceiptJson,
            CancellationToken.None);

        Assert.Equal(CatalogMutationObservationKind.AcceptedUnverifiable, observation.Kind);
        Assert.Equal(1, fixture.Http.MutationCount);
        Assert.Equal(HttpMethod.Put, fixture.Http.LastMutationMethod);
        Assert.Equal($"/api/v10/channels/{ChannelId}/voice-status", fixture.Http.LastMutationPath);
        Assert.Contains("\"accepted\":true", receipt.ReceiptJson, StringComparison.Ordinal);
    }

    private static CatalogMutationInvocation UpdateInvocation(
        ChannelApiState current,
        UpdateChannelSetInput set,
        ImmutableArray<string> clear = default) => CatalogMutationInvocation.Create(
            Guid.CreateVersion7().ToString("D"),
            "update_channel",
            ChannelId,
            "test channel update",
            new UpdateChannelInput(current.ComputeDigest(), set, clear.IsDefault ? [] : clear));

    private static UpdateChannelSetInput EmptySet() => new(
        Name: null,
        ChannelType: null,
        Topic: null,
        IsNsfw: null,
        SlowModeSeconds: null,
        Bitrate: null,
        UserLimit: null,
        RtcRegion: null,
        VideoQualityMode: null,
        DefaultAutoArchiveMinutes: null,
        DefaultThreadSlowModeSeconds: null,
        AvailableTags: null,
        DefaultReaction: null,
        DefaultSortOrder: null,
        DefaultForumLayout: null,
        RequireTag: null,
        HideMediaDownloadOptions: null,
        IsSpoiler: null);

    private static string ChannelState(int type, string additional) => $$"""
        {
          "id": "{{ChannelId}}",
          "guild_id": "{{GuildId}}",
          "type": {{type}},
          "name": "general",
          "position": 0,
          "parent_id": null,
          "permission_overwrites": [],
          {{additional}}
        }
        """;

    private static Fixture CreateFixture(string stateJson)
    {
        var options = DiscordOptions.FromValues(GuildId, "test-token");
        var session = new DiscordRestSession(options);
        var http = new ScriptedDiscordHandler(stateJson);
        var transport = new DiscordApiTransport(options, session, http);
        var handler = new ChannelMutationHandler(
            transport,
            guildReader: null!,
            botInspector: null!,
            analysisService: null!,
            StewardPolicy.Create([], [], []),
            StewardRuntimeSettings.FromValues(GuildId, "test-token"),
            assetVault: null!,
            messageReader: null!);
        return new Fixture(handler, transport, session, http);
    }

    private sealed class Fixture(
        ChannelMutationHandler handler,
        DiscordApiTransport transport,
        DiscordRestSession session,
        ScriptedDiscordHandler http) : IAsyncDisposable
    {
        internal ChannelMutationHandler Handler { get; } = handler;

        internal ScriptedDiscordHandler Http { get; } = http;

        internal ChannelApiState CurrentState()
        {
            using var document = JsonDocument.Parse(Http.State.ToJsonString());
            return ChannelApiState.Parse(document.RootElement);
        }

        public async ValueTask DisposeAsync()
        {
            transport.Dispose();
            await session.DisposeAsync();
        }
    }

    private sealed class ScriptedDiscordHandler(string stateJson) : HttpMessageHandler
    {
        internal JsonObject State { get; } = JsonNode.Parse(stateJson)!.AsObject();

        internal int MutationCount { get; private set; }

        internal JsonObject? LastMutationBody { get; private set; }

        internal HttpMethod? LastMutationMethod { get; private set; }

        internal string? LastMutationPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(State);
            }

            MutationCount++;
            LastMutationMethod = request.Method;
            LastMutationPath = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post &&
                request.RequestUri.AbsolutePath == $"/api/v10/channels/{ChannelId}/typing")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Put &&
                request.RequestUri.AbsolutePath == $"/api/v10/channels/{ChannelId}/voice-status")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal($"/api/v10/channels/{ChannelId}", request.RequestUri!.AbsolutePath);
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastMutationBody = JsonNode.Parse(json)!.AsObject();
            foreach (var property in LastMutationBody)
            {
                State[property.Key] = property.Value?.DeepClone();
            }

            if (State["available_tags"] is JsonArray tags)
            {
                for (var index = 0; index < tags.Count; index++)
                {
                    var tag = tags[index]!.AsObject();
                    tag["id"] ??= (900 + index).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return JsonResponse(State);
        }

        private static HttpResponseMessage JsonResponse(JsonObject body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }
}
