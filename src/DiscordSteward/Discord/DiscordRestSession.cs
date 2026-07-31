using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Errors;
using global::Discord;
using global::Discord.Rest;

namespace DiscordSteward.Discord;

public sealed class DiscordRestSession : IDiscordReadiness, IAsyncDisposable
{
    private readonly DiscordOptions _options;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private DiscordRestClient? _client;
    private DiscordReadinessSnapshot _readiness;

    public DiscordRestSession(DiscordOptions options)
    {
        _options = options;
        _readiness = new DiscordReadinessSnapshot(
            options.IsConfigured ? "not_checked" : "unconfigured",
            options.GuildId?.ToString(CultureInfo.InvariantCulture),
            BotUserId: null,
            LastErrorCode: options.IsConfigured ? null : StewardErrorCodes.NotConfigured,
            LastSuccessfulReadAt: null);
    }

    public DiscordReadinessSnapshot GetReadiness() => Volatile.Read(ref _readiness);

    internal async Task<RestGuild> GetGuildAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw DiscordErrorMapper.NotConfigured();
        }

        DiscordRestClient client;
        RestGuild? preflightGuild = null;

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is null)
            {
                (client, preflightGuild) = await InitializeAsync(cancellationToken).ConfigureAwait(false);
                _client = client;
            }
            else
            {
                client = _client;
            }
        }
        finally
        {
            _initializationGate.Release();
        }

        if (preflightGuild is not null)
        {
            return preflightGuild;
        }

        try
        {
            var guild = await client.GetGuildAsync(
                _options.GetRequiredGuildId(),
                ReadOptions(cancellationToken)).ConfigureAwait(false);

            return ValidateGuild(guild);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var mapped = DiscordErrorMapper.Map(exception, StewardErrorCodes.GuildMismatch);
            ReportFailure(mapped.Code);
            throw mapped;
        }
    }

    internal void ReportSuccessfulRead()
    {
        var current = GetReadiness();
        Volatile.Write(ref _readiness, current with
        {
            State = "ready",
            LastErrorCode = null,
            LastSuccessfulReadAt = DateTimeOffset.UtcNow
        });
    }

    internal void ReportFailure(string errorCode)
    {
        var current = GetReadiness();
        Volatile.Write(ref _readiness, current with
        {
            State = "degraded",
            LastErrorCode = errorCode
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _initializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is not null)
            {
                await DisposeClientAsync(_client).ConfigureAwait(false);
                _client = null;
            }
        }
        finally
        {
            _initializationGate.Release();
            _initializationGate.Dispose();
        }
    }

    private async Task<(DiscordRestClient Client, RestGuild Guild)> InitializeAsync(
        CancellationToken cancellationToken)
    {
        var client = new DiscordRestClient();
        try
        {
            await client.LoginAsync(
                    TokenType.Bot,
                    _options.GetRequiredBotToken().Value,
                    validateToken: true)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var guild = await client.GetGuildAsync(
                _options.GetRequiredGuildId(),
                ReadOptions(cancellationToken)).ConfigureAwait(false);
            var validatedGuild = ValidateGuild(guild);

            Volatile.Write(ref _readiness, new DiscordReadinessSnapshot(
                "ready",
                validatedGuild.Id.ToString(CultureInfo.InvariantCulture),
                client.CurrentUser.Id.ToString(CultureInfo.InvariantCulture),
                LastErrorCode: null,
                LastSuccessfulReadAt: DateTimeOffset.UtcNow));

            return (client, validatedGuild);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeClientAsync(client).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await DisposeClientAsync(client).ConfigureAwait(false);
            var mapped = DiscordErrorMapper.Map(
                exception,
                StewardErrorCodes.GuildMismatch,
                authenticationPhase: true);
            ReportFailure(mapped.Code);
            throw mapped;
        }
    }

    private RestGuild ValidateGuild(RestGuild? guild)
    {
        if (guild is null || guild.Id != _options.GetRequiredGuildId())
        {
            throw new StewardException(
                StewardErrorCodes.GuildMismatch,
                "The configured bot cannot access the configured guild.");
        }

        return guild;
    }

    private static RequestOptions ReadOptions(CancellationToken cancellationToken) => new()
    {
        CancelToken = cancellationToken,
        RetryMode = RetryMode.RetryRatelimit
    };

    private static async ValueTask DisposeClientAsync(DiscordRestClient client)
    {
        try
        {
            if (client.LoginState is not LoginState.LoggedOut)
            {
                await client.LogoutAsync().ConfigureAwait(false);
            }
        }
        catch (global::Discord.Net.HttpException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        await client.DisposeAsync().ConfigureAwait(false);
    }
}
