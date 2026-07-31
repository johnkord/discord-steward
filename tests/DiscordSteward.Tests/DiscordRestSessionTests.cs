using System.Net;
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
}
