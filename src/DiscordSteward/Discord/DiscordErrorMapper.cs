using System.Net;
using System.Text;
using System.Text.Json;
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

    internal static StewardException MapStatusCode(
        HttpStatusCode statusCode,
        string notFoundCode,
        JsonElement? discordError = null) =>
        statusCode switch
        {
            HttpStatusCode.BadRequest => InvalidArgument(statusCode, discordError),
            HttpStatusCode.Unauthorized => Create(StewardErrorCodes.DiscordAuthenticationFailed),
            HttpStatusCode.Forbidden => Create(StewardErrorCodes.DiscordPermissionDenied),
            HttpStatusCode.NotFound => Create(notFoundCode),
            HttpStatusCode.TooManyRequests => Create(StewardErrorCodes.RateLimited),
            _ => Create(StewardErrorCodes.DiscordUnavailable)
        };

    internal static StewardException NotConfigured() => Create(StewardErrorCodes.NotConfigured);

    private static StewardException InvalidArgument(HttpStatusCode statusCode, JsonElement? discordError)
    {
        var message = new StringBuilder("Discord rejected the request as invalid (HTTP ")
            .Append((int)statusCode);
        if (discordError is { ValueKind: JsonValueKind.Object } root)
        {
            if (root.TryGetProperty("code", out var code) && code.TryGetInt64(out var numericCode))
            {
                message.Append(", Discord code ").Append(numericCode);
            }

            var paths = ReadFieldPaths(root);
            if (paths.Count > 0)
            {
                message.Append("; fields: ").AppendJoin(", ", paths);
            }
        }

        return new StewardException(StewardErrorCodes.InvalidArgument, message.Append(").").ToString());
    }

    private static List<string> ReadFieldPaths(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var paths = new List<string>(capacity: 8);
        CollectFieldPaths(errors, prefix: string.Empty, paths, depth: 0);
        return paths;
    }

    private static void CollectFieldPaths(
        JsonElement node,
        string prefix,
        List<string> paths,
        int depth)
    {
        if (paths.Count >= 8 || depth >= 16 || node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in node.EnumerateObject())
        {
            if (paths.Count >= 8)
            {
                return;
            }

            if (property.NameEquals("_errors"))
            {
                if (prefix.Length > 0 && !paths.Contains(prefix, StringComparer.Ordinal))
                {
                    paths.Add(prefix);
                }

                continue;
            }

            if (!IsSafePathSegment(property.Name))
            {
                continue;
            }

            var next = ulong.TryParse(property.Name, out _)
                ? string.Concat(prefix, "[", property.Name, "]")
                : prefix.Length == 0 ? property.Name : string.Concat(prefix, ".", property.Name);
            if (next.Length <= 160)
            {
                CollectFieldPaths(property.Value, next, paths, depth + 1);
            }
        }
    }

    private static bool IsSafePathSegment(string value) =>
        value.Length is >= 1 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

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
