using System.Globalization;

namespace DiscordSteward.Configuration;

public sealed class DiscordOptions
{
    private readonly DiscordBotToken? _botToken;

    private DiscordOptions(ulong? guildId, DiscordBotToken? botToken)
    {
        GuildId = guildId;
        _botToken = botToken;
    }

    public ulong? GuildId { get; }

    public bool BotTokenConfigured => _botToken is not null;

    public bool IsConfigured => GuildId.HasValue && BotTokenConfigured;

    public static DiscordOptions FromValues(string? guildId, string? botToken)
    {
        ulong? parsedGuildId = ulong.TryParse(guildId, out var value) && value > 0
            ? value
            : null;
        var parsedBotToken = string.IsNullOrWhiteSpace(botToken)
            ? null
            : new DiscordBotToken(botToken);

        return new DiscordOptions(parsedGuildId, parsedBotToken);
    }

    internal ulong GetRequiredGuildId() => GuildId
        ?? throw new InvalidOperationException("Discord guild configuration is unavailable.");

    internal DiscordBotToken GetRequiredBotToken() => _botToken
        ?? throw new InvalidOperationException("Discord bot credential is unavailable.");

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"DiscordOptions {{ GuildId = {GuildId?.ToString(CultureInfo.InvariantCulture) ?? "unconfigured"}, BotTokenConfigured = {BotTokenConfigured} }}");
}

internal sealed class DiscordBotToken(string value)
{
    internal string Value { get; } = value;

    public override string ToString() => "[REDACTED]";
}
