using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Errors;

namespace DiscordSteward.Discord;

internal sealed record DiscordBinaryPayload(
    string MediaType,
    byte[] Content);

internal sealed class DiscordApiTransport : IDisposable
{
    private const long MaximumJsonBytes = 4 * 1024 * 1024;
    private const int MaximumErrorJsonBytes = 64 * 1024;
    private const int MaximumBinaryBytes = 512 * 1024;
    private readonly DiscordOptions _options;
    private readonly DiscordRestSession _session;
    private readonly HttpClient _httpClient;

    public DiscordApiTransport(
        DiscordOptions options,
        DiscordRestSession session)
        : this(options, session, new HttpClientHandler())
    {
    }

    internal DiscordApiTransport(
        DiscordOptions options,
        DiscordRestSession session,
        HttpMessageHandler handler)
    {
        _options = options;
        _session = session;
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://discord.com/api/v10/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    internal async Task<JsonElement> GetJsonAsync(
        string fixedRoute,
        CancellationToken cancellationToken)
    {
        using var response = await SendGetWithRateLimitRetryAsync(fixedRoute, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadOptionalJsonAsync(response, cancellationToken).ConfigureAwait(false)
            ?? throw InvalidResponse();
    }

    internal async Task<JsonElement?> TryGetJsonAsync(
        string fixedRoute,
        CancellationToken cancellationToken)
    {
        using var response = await SendGetWithRateLimitRetryAsync(fixedRoute, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _session.ReportSuccessfulRead();
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadOptionalJsonAsync(response, cancellationToken).ConfigureAwait(false)
            ?? throw InvalidResponse();
    }

    internal async Task<DiscordBinaryPayload> GetBytesAsync(
        string fixedRoute,
        CancellationToken cancellationToken)
    {
        using var response = await SendGetWithRateLimitRetryAsync(fixedRoute, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > MaximumBinaryBytes)
        {
            throw new StewardException(
                StewardErrorCodes.ResultTooLarge,
                "Discord binary response exceeds the local safety limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaximumBinaryBytes)
            {
                throw new StewardException(
                    StewardErrorCodes.ResultTooLarge,
                    "Discord binary response exceeds the local safety limit.");
            }

            output.Write(buffer, 0, read);
        }

        _session.ReportSuccessfulRead();
        return new DiscordBinaryPayload(
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            output.ToArray());
    }

    private async Task<HttpResponseMessage> SendGetWithRateLimitRetryAsync(
        string fixedRoute,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = CreateRequest(HttpMethod.Get, fixedRoute, auditReason: null);
            var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= 2)
            {
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            response.Dispose();
            await Task.Delay(
                delay <= TimeSpan.FromSeconds(30) ? delay : TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<JsonElement?> SendJsonAsync(
        HttpMethod method,
        string fixedRoute,
        JsonElement? body,
        string auditReason,
        CancellationToken cancellationToken)
    {
        if (method == HttpMethod.Get)
        {
            throw new ArgumentException("Mutation transport cannot send GET through SendJsonAsync.", nameof(method));
        }

        using var request = CreateRequest(method, fixedRoute, auditReason);
        if (body.HasValue)
        {
            request.Content = JsonContent.Create(body.Value);
        }

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadOptionalJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<JsonElement?> SendContentAsync(
        HttpMethod method,
        string fixedRoute,
        HttpContent content,
        string auditReason,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, fixedRoute, auditReason);
        request.Content = content;
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadOptionalJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<JsonElement> SendRequiredJsonAsync(
        HttpMethod method,
        string fixedRoute,
        JsonElement? body,
        string? auditReason,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, fixedRoute, auditReason);
        if (body.HasValue)
        {
            request.Content = JsonContent.Create(body.Value);
        }

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadOptionalJsonAsync(response, cancellationToken).ConfigureAwait(false)
            ?? throw InvalidResponse();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var mapped = DiscordErrorMapper.Map(exception, StewardErrorCodes.DiscordUnavailable);
            _session.ReportFailure(mapped.Code);
            throw mapped;
        }
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string fixedRoute,
        string? auditReason)
    {
        if (!_options.IsConfigured)
        {
            throw DiscordErrorMapper.NotConfigured();
        }

        ValidateFixedRoute(fixedRoute);
        var request = new HttpRequestMessage(method, fixedRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _options.GetRequiredBotToken().Value);
        request.Headers.UserAgent.ParseAdd("DiscordSteward/1.0");
        if (auditReason is not null)
        {
            if (auditReason.Length is < 1 or > 512 || auditReason.Any(char.IsControl))
            {
                throw new StewardException(
                    StewardErrorCodes.InvalidArgument,
                    "Discord audit reason must contain 1 to 512 printable characters.");
            }

            request.Headers.TryAddWithoutValidation("X-Audit-Log-Reason", Uri.EscapeDataString(auditReason));
        }

        return request;
    }

    private async Task<JsonElement?> ReadOptionalJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            _session.ReportSuccessfulRead();
            return null;
        }

        if (response.Content.Headers.ContentLength > MaximumJsonBytes)
        {
            throw new StewardException(
                StewardErrorCodes.ResultTooLarge,
                "Discord JSON response exceeds the local safety limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = 64 },
            cancellationToken).ConfigureAwait(false);
        _session.ReportSuccessfulRead();
        return document.RootElement.Clone();
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var details = response.StatusCode == HttpStatusCode.BadRequest
                ? await TryReadErrorJsonAsync(response, cancellationToken).ConfigureAwait(false)
                : null;
            var mapped = DiscordErrorMapper.MapStatusCode(
                response.StatusCode,
                StewardErrorCodes.ResourceNotFound,
                details);
            _session.ReportFailure(mapped.Code);
            throw mapped;
        }
    }

    private static async Task<JsonElement?> TryReadErrorJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumErrorJsonBytes)
        {
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[4 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > MaximumErrorJsonBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            using var document = JsonDocument.Parse(
                buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)),
                new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is JsonException or IOException or HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void ValidateFixedRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route) || route.Length > 2_048 ||
            route[0] == '/' ||
            route.Contains("://", StringComparison.Ordinal) ||
            route.Contains("..", StringComparison.Ordinal) ||
            route.Any(char.IsControl))
        {
            throw new InvalidOperationException("A typed Discord adapter produced an invalid fixed route.");
        }
    }

    private static StewardException InvalidResponse() => new(
        StewardErrorCodes.DiscordUnavailable,
        "Discord returned an empty or invalid JSON response.");
}
