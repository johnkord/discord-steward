using System.Collections.Immutable;
using System.Text.Json;
using DiscordSteward.Errors;
using DiscordSteward.Operations;

namespace DiscordSteward.Tests;

public sealed class RichMessagePayloadTests
{
    [Fact]
    public void Embeds_NormalizeAndRoundTripSettableDiscordFields()
    {
        var input = ImmutableArray.Create(new RichEmbedInput(
            Title: "Release",
            Description: "Details",
            Url: "https://example.com/release",
            Timestamp: DateTimeOffset.Parse("2026-07-26T12:34:56.1234567Z", System.Globalization.CultureInfo.InvariantCulture),
            ColorHex: "#12AB34",
            Footer: new RichEmbedFooterInput("Footer", "https://example.com/footer.png"),
            Image: new RichEmbedMediaInput("https://example.com/image.png", "Image description"),
            Thumbnail: null,
            Author: new RichEmbedAuthorInput("Steward", "https://example.com", null),
            Fields: ImmutableArray.Create(new RichEmbedFieldInput("Status", "Ready", Inline: true))));

        var normalized = RichMessagePayloadNormalizer.NormalizeEmbeds(input)!.Value;
        var payload = RichMessagePayloadNormalizer.EmbedsPayload(normalized);
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(new { embeds = payload }, CatalogJson.Options));
        var observed = RichMessagePayloadNormalizer.ParseObservedEmbeds(response.RootElement);

        AssertJsonEqual(RichMessagePayloadNormalizer.CanonicalEmbedsJson(normalized), observed);
    }

    [Fact]
    public void Components_NormalizeAndRoundTripStaticLayoutsInOrder()
    {
        var components = ImmutableArray.Create(
            new StaticComponentInput("text_display", Content: "# Status"),
            new StaticComponentInput("separator", Divider: true, Spacing: 2),
            new StaticComponentInput(
                "section",
                Section: new StaticSectionInput(
                    ["Read the runbook"],
                    new StaticSectionAccessoryInput(
                        "link_button",
                        Url: "https://example.com/runbook",
                        Label: "Runbook"))),
            new StaticComponentInput(
                "container",
                Container: new StaticContainerInput(
                    [new StaticContainerChildInput("text_display", Content: "Contained")],
                    AccentColorHex: "#336699")));

        var normalized = RichMessagePayloadNormalizer.NormalizeComponents(
            components,
            new Dictionary<string, string>(StringComparer.Ordinal))!.Value;
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(new { components = normalized.Payload }, CatalogJson.Options));
        var observed = RichMessagePayloadNormalizer.ParseObservedComponents(response.RootElement);

        Assert.Equal(7, normalized.Count);
        AssertJsonEqual(normalized.CanonicalJson, observed);
    }

    [Fact]
    public void Components_RejectInteractiveOrUnknownTypes()
    {
        var exception = Assert.Throws<StewardException>(() =>
            RichMessagePayloadNormalizer.NormalizeComponents(
                [new StaticComponentInput("button")],
                new Dictionary<string, string>(StringComparer.Ordinal)));

        Assert.Equal(StewardErrorCodes.InvalidArgument, exception.Code);
    }

    [Fact]
    public void Embeds_RejectCombinedTextAboveDiscordLimit()
    {
        var exception = Assert.Throws<StewardException>(() =>
            RichMessagePayloadNormalizer.NormalizeEmbeds(
                [
                    new RichEmbedInput(Description: new string('a', 4_000)),
                    new RichEmbedInput(Description: new string('b', 2_001))
                ]));

        Assert.Equal(StewardErrorCodes.InvalidArgument, exception.Code);
    }

    [Fact]
    public void VoiceMessage_NormalizesBoundedWaveform()
    {
        var normalized = RichMessagePayloadNormalizer.NormalizeVoiceMessage(new VoiceMessageInput(
            "asset_abc",
            DurationSeconds: 1.25,
            WaveformBase64: Convert.ToBase64String([0, 64, 255])));

        Assert.Equal("asset_abc", normalized.AssetId);
        Assert.Equal([0, 64, 255], Convert.FromBase64String(normalized.WaveformBase64));
    }

    private static void AssertJsonEqual(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement));
    }
}
