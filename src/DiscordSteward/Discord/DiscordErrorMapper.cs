using System.Net;
using DiscordSteward.Errors;
using global::Discord.Net;

namespace DiscordSteward.Discord;

internal static class DiscordErrorMapper
{
    internal static StewardException Map(
        Exception exception,
        string notFoundCode,
        bool authenticationPhase = false)
    {
        if (exception is StewardException stewardException)
        {
            return stewardException;
        }

        if (exception is RateLimitedException)
        {
            return Create(StewardErrorCodes.RateLimited);
        }

        if (exception is HttpException httpException)
        {
            return MapStatusCode(httpException.HttpCode, notFoundCode);
        }

        if (authenticationPhase && exception is ArgumentException or InvalidOperationException)
        {
            return Create(StewardErrorCodes.DiscordAuthenticationFailed);
        }

        return Create(StewardErrorCodes.DiscordUnavailable);
    }

    internal static StewardException MapStatusCode(HttpStatusCode statusCode, string notFoundCode) =>
        statusCode switch
        {
            HttpStatusCode.BadRequest => Create(StewardErrorCodes.InvalidArgument),
            HttpStatusCode.Unauthorized => Create(StewardErrorCodes.DiscordAuthenticationFailed),
            HttpStatusCode.Forbidden => Create(StewardErrorCodes.DiscordPermissionDenied),
            HttpStatusCode.NotFound => Create(notFoundCode),
            HttpStatusCode.TooManyRequests => Create(StewardErrorCodes.RateLimited),
            _ => Create(StewardErrorCodes.DiscordUnavailable)
        };

    internal static StewardException NotConfigured() => Create(StewardErrorCodes.NotConfigured);

    private static StewardException Create(string code) => new(code, code switch
    {
        StewardErrorCodes.InvalidArgument =>
            "Discord rejected the request as invalid.",
        StewardErrorCodes.NotConfigured =>
            "Discord reads require a valid configured guild ID and bot token.",
        StewardErrorCodes.DiscordAuthenticationFailed =>
            "Discord rejected the configured bot credential.",
        StewardErrorCodes.DiscordPermissionDenied =>
            "The configured bot lacks permission for this Discord read.",
        StewardErrorCodes.GuildMismatch =>
            "The configured bot cannot access the configured guild.",
        StewardErrorCodes.ResourceNotFound =>
            "The requested resource was not found in the configured guild.",
        StewardErrorCodes.AmbiguousName =>
            "The requested display name is ambiguous; use an exact resource ID.",
        StewardErrorCodes.RateLimited =>
            "Discord rate limited the read before it could complete.",
        _ => "Discord could not complete the read."
    });
}
