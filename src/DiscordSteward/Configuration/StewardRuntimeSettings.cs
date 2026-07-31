namespace DiscordSteward.Configuration;

public sealed record StewardRuntimeSettings(ulong? GuildId, bool DiscordTokenConfigured)
{
    public bool IsConfigured => GuildId.HasValue && DiscordTokenConfigured;

    public static StewardRuntimeSettings FromValues(string? guildId, string? discordToken) =>
        FromOptions(DiscordOptions.FromValues(guildId, discordToken));

    public static StewardRuntimeSettings FromOptions(DiscordOptions options) => new(
        options.GuildId,
        options.BotTokenConfigured);
}
