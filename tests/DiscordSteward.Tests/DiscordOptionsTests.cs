using System.Text.Json;
using DiscordSteward.Configuration;

namespace DiscordSteward.Tests;

public sealed class DiscordOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-snowflake")]
    [InlineData("0")]
    public void FromValues_InvalidGuildId_FailsClosed(string? guildId)
    {
        var options = DiscordOptions.FromValues(guildId, "sensitive-token-value");

        Assert.Null(options.GuildId);
        Assert.True(options.BotTokenConfigured);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void FromValues_MissingToken_FailsClosed()
    {
        var options = DiscordOptions.FromValues("667956000757776386", "   ");

        Assert.Equal(667956000757776386UL, options.GuildId);
        Assert.False(options.BotTokenConfigured);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void SecretOwningOptions_DoNotExposeTokenThroughTextOrJson()
    {
        const string token = "sensitive-token-value";
        var options = DiscordOptions.FromValues("667956000757776386", token);

        var text = options.ToString();
        var json = JsonSerializer.Serialize(options);

        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        Assert.Contains("BotTokenConfigured = True", text, StringComparison.Ordinal);
        Assert.Contains("\"BotTokenConfigured\":true", json, StringComparison.Ordinal);
    }
}
