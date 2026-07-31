using DiscordSteward.Discord;

namespace DiscordSteward.Tests;

public sealed class DiscordGuildWriterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("Welcome", "Welcome")]
    public void TopicForDiscord_UsesEmptyStringForClear(string? topic, string expected)
    {
        Assert.Equal(expected, DiscordGuildWriter.TopicForDiscord(topic));
    }
}
