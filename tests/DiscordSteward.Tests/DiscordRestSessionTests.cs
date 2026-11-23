using System.Net;
using System.Text;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Tests;

public sealed class DiscordRestSessionTests
{
    [Fact]
    public async Task GetGuildAsync_Unconfigured_FailsBeforeNetworkAccess()
    {
        await using var session = new DiscordRestSession(
            DiscordOptions.FromValues("100000000000000001", botToken: null));

        var exception = await Assert.ThrowsAsync<StewardException>(
            () => session.GetGuildAsync(CancellationToken.None));

        Assert.Equal(StewardErrorCodes.NotConfigured, exception.Code);
        Assert.Equal("unconfigured", session.GetReadiness().State);
        Assert.Null(session.GetReadiness().BotUserId);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, StewardErrorCodes.InvalidArgument)]
    [InlineData(HttpStatusCode.Unauthorized, StewardErrorCodes.DiscordAuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, StewardErrorCodes.DiscordPermissionDenied)]
    [InlineData(HttpStatusCode.NotFound, StewardErrorCodes.ResourceNotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, StewardErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, StewardErrorCodes.DiscordUnavailable)]
    public void MapStatusCode_UsesStableSafeCodes(HttpStatusCode statusCode, string expectedCode)
    {
        var mapped = DiscordErrorMapper.MapStatusCode(statusCode, StewardErrorCodes.ResourceNotFound);

        Assert.Equal(expectedCode, mapped.Code);
        Assert.DoesNotContain(statusCode.ToString(), mapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_AuthenticationFailure_DoesNotCopyRawExceptionText()
    {
        const string sensitiveText = "credential-shaped-sensitive-value";

        var mapped = DiscordErrorMapper.Map(
            new ArgumentException(sensitiveText),
            StewardErrorCodes.GuildMismatch,
            authenticationPhase: true);

        Assert.Equal(StewardErrorCodes.DiscordAuthenticationFailed, mapped.Code);
        Assert.DoesNotContain(sensitiveText, mapped.Message, StringComparison.Ordinal);
        Assert.Null(mapped.InnerException);
    }

        [Fact]
        public async Task MutationTransport_InvalidFormBodyPreservesOnlySafeStructuralEvidence()
        {
                const string sensitiveText = "rejected-private-value";
                var body = $$"""
                        {
                            "message": "{{sensitiveText}}",
                            "code": 50035,
                            "errors": {
                                "embeds": {
                                    "0": {
                                        "title": {
                                            "_errors": [{ "code": "BASE_TYPE_MAX_LENGTH", "message": "{{sensitiveText}}" }]
                                        }
                                    }
                                }
                            }
                        }
                        """;
                var options = DiscordOptions.FromValues("100000000000000001", "test-token");
                await using var session = new DiscordRestSession(options);
                using var transport = new DiscordApiTransport(
                        options,
                        session,
                        new StaticResponseHandler(HttpStatusCode.BadRequest, body));

                var exception = await Assert.ThrowsAsync<StewardException>(() => transport.SendJsonAsync(
                        HttpMethod.Post,
                    "channels/100000000000000002/messages",
                        JsonSerializer.SerializeToElement(new { content = sensitiveText }),
                        "test invalid form",
                        CancellationToken.None));

                Assert.Equal(StewardErrorCodes.InvalidArgument, exception.Code);
                Assert.Contains("HTTP 400", exception.Message, StringComparison.Ordinal);
                Assert.Contains("Discord code 50035", exception.Message, StringComparison.Ordinal);
                Assert.Contains("embeds[0].title", exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(sensitiveText, exception.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("BASE_TYPE_MAX_LENGTH", exception.Message, StringComparison.Ordinal);
        }

        private sealed class StaticResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
        {
                protected override Task<HttpResponseMessage> SendAsync(
                        HttpRequestMessage request,
                        CancellationToken cancellationToken)
                {
                        cancellationToken.ThrowIfCancellationRequested();
                        return Task.FromResult(new HttpResponseMessage(statusCode)
                        {
                                Content = new StringContent(body, Encoding.UTF8, "application/json")
                        });
                }
        }
}
