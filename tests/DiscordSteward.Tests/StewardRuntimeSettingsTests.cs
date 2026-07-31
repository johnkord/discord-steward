using DiscordSteward.Configuration;

namespace DiscordSteward.Tests;

public sealed class StewardRuntimeSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-snowflake")]
    [InlineData("0")]
    public void FromValues_InvalidGuildId_RemainsUnconfigured(string? guildId)
    {
        var settings = StewardRuntimeSettings.FromValues(guildId, "a-token");

        Assert.Null(settings.GuildId);
        Assert.True(settings.DiscordTokenConfigured);
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void FromValues_ValidGuildAndToken_IsConfigured()
    {
        var settings = StewardRuntimeSettings.FromValues("100000000000000001", "a-token");

        Assert.Equal(100000000000000001UL, settings.GuildId);
        Assert.True(settings.DiscordTokenConfigured);
        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void FromValues_MissingToken_RemainsUnconfigured()
    {
        var settings = StewardRuntimeSettings.FromValues("100000000000000001", "   ");

        Assert.Equal(100000000000000001UL, settings.GuildId);
        Assert.False(settings.DiscordTokenConfigured);
        Assert.False(settings.IsConfigured);
    }
}
