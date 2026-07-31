using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class EventStageTextValidationTests
{
    [Fact]
    public void Description_AllowsDiscordSafeFormattingControls()
    {
        const string description = "First night\nSecond night\r\n\tDoors at 7 PM";

        var normalized = EventStageMutationHandler.NormalizeText(
            description,
            maximum: 1_000,
            allowEmpty: true,
            allowFormatting: true);

        Assert.Equal(description, normalized);
    }

    [Fact]
    public void SingleLineField_RejectsLineBreaks()
    {
        Assert.Throws<StewardException>(() => EventStageMutationHandler.NormalizeText(
            "Invalid\nlocation",
            maximum: 100,
            allowEmpty: false));
    }

    [Theory]
    [InlineData("Invalid\0description")]
    [InlineData("Invalid\u0001description")]
    [InlineData("Invalid\u000Bdescription")]
    public void Description_RejectsOtherControlCharacters(string description)
    {
        Assert.Throws<StewardException>(() => EventStageMutationHandler.NormalizeText(
            description,
            maximum: 1_000,
            allowEmpty: true,
            allowFormatting: true));
    }
}
