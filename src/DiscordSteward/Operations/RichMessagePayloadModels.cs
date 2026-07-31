using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

public sealed record RichEmbedFieldInput(
    string Name,
    string Value,
    bool Inline = false);

public sealed record RichEmbedFooterInput(
    string Text,
    string? IconUrl = null);

public sealed record RichEmbedMediaInput(
    string Url,
    string? Description = null);

public sealed record RichEmbedAuthorInput(
    string Name,
    string? Url = null,
    string? IconUrl = null);

public sealed record RichEmbedInput(
    string? Title = null,
    string? Description = null,
    string? Url = null,
    DateTimeOffset? Timestamp = null,
    string? ColorHex = null,
    RichEmbedFooterInput? Footer = null,
    RichEmbedMediaInput? Image = null,
    RichEmbedMediaInput? Thumbnail = null,
    RichEmbedAuthorInput? Author = null,
    ImmutableArray<RichEmbedFieldInput>? Fields = null);

public sealed record SharedClientThemeInput(
    ImmutableArray<string> Colors,
    int GradientAngle,
    int BaseMix,
    string BaseTheme);

public sealed record VoiceMessageInput(
    string AssetId,
    double DurationSeconds,
    string WaveformBase64);

public sealed record StaticMediaItemInput(
    string? Url = null,
    string? AssetId = null,
    string? Description = null,
    bool Spoiler = false);

public sealed record StaticSectionAccessoryInput(
    string Type,
    string? Url = null,
    string? AssetId = null,
    string? Label = null,
    string? Description = null,
    bool Spoiler = false);

public sealed record StaticSectionInput(
    ImmutableArray<string> TextDisplays,
    StaticSectionAccessoryInput Accessory);

public sealed record StaticContainerChildInput(
    string Type,
    string? Content = null,
    bool? Divider = null,
    int? Spacing = null,
    ImmutableArray<StaticMediaItemInput>? MediaItems = null,
    string? AssetId = null,
    bool Spoiler = false);

public sealed record StaticContainerInput(
    ImmutableArray<StaticContainerChildInput> Children,
    string? AccentColorHex = null,
    bool Spoiler = false);

public sealed record StaticComponentInput(
    string Type,
    string? Content = null,
    bool? Divider = null,
    int? Spacing = null,
    ImmutableArray<StaticMediaItemInput>? MediaItems = null,
    string? AssetId = null,
    bool Spoiler = false,
    StaticSectionInput? Section = null,
    StaticContainerInput? Container = null);

internal sealed record RichPayloadState(
    bool IsTts,
    ImmutableArray<string> StickerIds,
    string EmbedsJson,
    string ComponentsJson,
    SharedClientThemeInput? SharedClientTheme);

internal static class RichMessagePayloadNormalizer
{
    private const int ComponentsV2Flag = 1 << 15;

    internal static ImmutableArray<RichEmbedInput>? NormalizeEmbeds(ImmutableArray<RichEmbedInput>? embeds)
    {
        if (!embeds.HasValue)
        {
            return null;
        }

        var source = embeds.Value.IsDefault ? [] : embeds.Value;
        if (source.Length > 10)
        {
            throw InvalidArgument("A message supports at most 10 rich embeds.");
        }

        var totalCharacters = 0;
        var normalized = source.Select(embed =>
        {
            var title = NormalizeOptionalText(embed.Title, 256, ref totalCharacters);
            var description = NormalizeOptionalText(embed.Description, 4_096, ref totalCharacters);
            var url = embed.Url is null ? null : NormalizeWebUrl(embed.Url);
            DateTimeOffset? timestamp = embed.Timestamp.HasValue ? NormalizeTimestamp(embed.Timestamp.Value) : null;
            var color = embed.ColorHex is null ? null : NormalizeColor(embed.ColorHex);
            var footer = embed.Footer is null ? null : new RichEmbedFooterInput(
                NormalizeRequiredText(embed.Footer.Text, 2_048, ref totalCharacters),
                embed.Footer.IconUrl is null ? null : NormalizeMediaUrl(embed.Footer.IconUrl));
            var image = NormalizeMedia(embed.Image);
            var thumbnail = NormalizeMedia(embed.Thumbnail);
            var author = embed.Author is null ? null : new RichEmbedAuthorInput(
                NormalizeRequiredText(embed.Author.Name, 256, ref totalCharacters),
                embed.Author.Url is null ? null : NormalizeWebUrl(embed.Author.Url),
                embed.Author.IconUrl is null ? null : NormalizeMediaUrl(embed.Author.IconUrl));
            ImmutableArray<RichEmbedFieldInput>? fields = embed.Fields.HasValue
                ? NormalizeFields(embed.Fields.Value, ref totalCharacters)
                : null;
            if (title is null && description is null && url is null && !timestamp.HasValue && color is null &&
                footer is null && image is null && thumbnail is null && author is null &&
                (!fields.HasValue || fields.Value.IsEmpty))
            {
                throw InvalidArgument("A rich embed must contain at least one settable field.");
            }

            return new RichEmbedInput(
                title,
                description,
                url,
                timestamp,
                color,
                footer,
                image,
                thumbnail,
                author,
                fields);
        }).ToImmutableArray();
        if (totalCharacters > 6_000)
        {
            throw InvalidArgument("Combined rich embed text exceeds 6000 characters.");
        }

        return normalized;
    }

    internal static ImmutableArray<string> NormalizeStickerIds(ImmutableArray<string>? stickerIds)
    {
        if (!stickerIds.HasValue)
        {
            return [];
        }

        var source = stickerIds.Value.IsDefault ? [] : stickerIds.Value;
        var normalized = source.Select(ChannelApiState.NormalizeSnowflake).ToImmutableArray();
        if (normalized.Length > 3 || normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw InvalidArgument("Sticker IDs must contain at most three unique guild sticker IDs.");
        }

        return normalized;
    }

    internal static SharedClientThemeInput? NormalizeTheme(SharedClientThemeInput? theme)
    {
        if (theme is null)
        {
            return null;
        }

        var colors = theme.Colors.IsDefault ? [] : theme.Colors.Select(NormalizeColorWithoutPrefix).ToImmutableArray();
        if (colors.Length is < 1 or > 5 || theme.GradientAngle is < 0 or > 360 || theme.BaseMix is < 0 or > 100)
        {
            throw InvalidArgument("Shared client theme values are outside Discord limits.");
        }

        var baseTheme = theme.BaseTheme.Trim().ToLowerInvariant() switch
        {
            "unset" or "dark" or "light" or "darker" or "midnight" => theme.BaseTheme.Trim().ToLowerInvariant(),
            _ => throw InvalidArgument("Shared client theme base must be unset, dark, light, darker, or midnight.")
        };
        return new SharedClientThemeInput(colors, theme.GradientAngle, theme.BaseMix, baseTheme);
    }

    internal static VoiceMessageInput NormalizeVoiceMessage(VoiceMessageInput input)
    {
        var waveform = DecodeBase64(input.WaveformBase64, "Voice waveform");
        if (waveform.Length is < 1 or > 256 || input.DurationSeconds is <= 0 or > 1_200 || !double.IsFinite(input.DurationSeconds))
        {
            throw InvalidArgument("Voice message duration or waveform is outside local limits.");
        }

        return new VoiceMessageInput(
            NormalizeAssetId(input.AssetId),
            input.DurationSeconds,
            Convert.ToBase64String(waveform));
    }

    internal static (object[] Payload, string CanonicalJson, int Count)? NormalizeComponents(
        ImmutableArray<StaticComponentInput>? components,
        IReadOnlyDictionary<string, string> assetFilenames,
        bool allowEmpty = false)
    {
        if (!components.HasValue)
        {
            return null;
        }

        var source = components.Value.IsDefault ? [] : components.Value;
        if (source.IsEmpty)
        {
            return allowEmpty
                ? ([], "[]", 0)
                : throw InvalidArgument("Components v2 requires at least one static component.");
        }

        var count = 0;
        var payload = source.Select(component => NormalizeTopLevelComponent(component, assetFilenames, ref count)).ToArray();
        if (count > 40)
        {
            throw InvalidArgument("Components v2 supports at most 40 total components.");
        }

        return (payload, JsonSerializer.Serialize(payload, CatalogJson.Options), count);
    }

    internal static void ValidateEmbedAttachmentReferences(
        ImmutableArray<RichEmbedInput> embeds,
        IEnumerable<string> filenames)
    {
        var allowed = filenames.ToHashSet(StringComparer.Ordinal);
        foreach (var embed in embeds)
        {
            foreach (var url in new[]
            {
                embed.Footer?.IconUrl,
                embed.Image?.Url,
                embed.Thumbnail?.Url,
                embed.Author?.IconUrl
            })
            {
                if (url is not null && url.StartsWith("attachment://", StringComparison.Ordinal) &&
                    !allowed.Contains(url["attachment://".Length..]))
                {
                    throw InvalidArgument("A rich embed references an attachment not included in this message.");
                }
            }
        }
    }

    internal static string CanonicalEmbedsJson(ImmutableArray<RichEmbedInput> embeds) =>
        JsonSerializer.Serialize(EmbedsPayload(embeds), CatalogJson.Options);

    internal static object[] EmbedsPayload(ImmutableArray<RichEmbedInput> embeds) =>
        embeds.Select(EmbedPayload).ToArray();

    internal static Dictionary<string, object?> ThemePayload(SharedClientThemeInput theme) => new(StringComparer.Ordinal)
    {
        ["colors"] = theme.Colors.ToArray(),
        ["gradient_angle"] = theme.GradientAngle,
        ["base_mix"] = theme.BaseMix,
        ["base_theme"] = BaseThemeValue(theme.BaseTheme)
    };

    internal static int AddMessageFlags(
        int flags,
        bool suppressEmbeds,
        bool suppressNotifications,
        bool voiceMessage,
        bool componentsV2)
    {
        if (suppressEmbeds) flags |= 1 << 2;
        if (suppressNotifications) flags |= 1 << 12;
        if (voiceMessage) flags |= 1 << 13;
        if (componentsV2) flags |= ComponentsV2Flag;
        return flags;
    }

    internal static string ParseObservedEmbeds(JsonElement message)
    {
        if (!message.TryGetProperty("embeds", out var embeds) || embeds.ValueKind == JsonValueKind.Null)
        {
            return "[]";
        }

        if (embeds.ValueKind != JsonValueKind.Array || embeds.GetArrayLength() > 10)
        {
            throw InvalidResponse("message embeds");
        }

        var values = embeds.EnumerateArray().Select(ParseObservedEmbed).ToArray();
        return JsonSerializer.Serialize(values, CatalogJson.Options);
    }

    internal static string ParseObservedComponents(JsonElement message)
    {
        if (!message.TryGetProperty("components", out var components) || components.ValueKind == JsonValueKind.Null)
        {
            return "[]";
        }

        if (components.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse("message components");
        }

        var count = 0;
        var values = components.EnumerateArray().Select(component => ParseObservedComponent(component, ref count)).ToArray();
        if (count > 40)
        {
            throw InvalidResponse("message component count");
        }

        return JsonSerializer.Serialize(values, CatalogJson.Options);
    }

    internal static SharedClientThemeInput? ParseObservedTheme(JsonElement message)
    {
        if (!message.TryGetProperty("shared_client_theme", out var theme) || theme.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (theme.ValueKind != JsonValueKind.Object ||
            !theme.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse("shared client theme");
        }

        var values = colors.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
                ? NormalizeColorWithoutPrefix(value.GetString() ?? string.Empty)
                : throw InvalidResponse("shared client theme color"))
            .ToImmutableArray();
        var result = new SharedClientThemeInput(
            values,
            RequiredInt32(theme, "gradient_angle"),
            RequiredInt32(theme, "base_mix"),
            BaseThemeName(RequiredInt32(theme, "base_theme")));
        return NormalizeTheme(result);
    }

    private static Dictionary<string, object?> EmbedPayload(RichEmbedInput embed)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        Add(payload, "title", embed.Title);
        Add(payload, "description", embed.Description);
        Add(payload, "url", embed.Url);
        Add(payload, "timestamp", embed.Timestamp?.ToString("O", CultureInfo.InvariantCulture));
        Add(payload, "color", embed.ColorHex is null ? null : Convert.ToInt32(embed.ColorHex[1..], 16));
        if (embed.Footer is not null)
        {
            payload["footer"] = OptionalDictionary(("text", embed.Footer.Text), ("icon_url", embed.Footer.IconUrl));
        }

        if (embed.Image is not null) payload["image"] = MediaPayload(embed.Image);
        if (embed.Thumbnail is not null) payload["thumbnail"] = MediaPayload(embed.Thumbnail);
        if (embed.Author is not null)
        {
            payload["author"] = OptionalDictionary(
                ("name", embed.Author.Name),
                ("url", embed.Author.Url),
                ("icon_url", embed.Author.IconUrl));
        }

        if (embed.Fields.HasValue)
        {
            payload["fields"] = embed.Fields.Value.Select(field => (object)new Dictionary<string, object?>
            {
                ["name"] = field.Name,
                ["value"] = field.Value,
                ["inline"] = field.Inline
            }).ToArray();
        }

        return payload;
    }

    private static Dictionary<string, object?> NormalizeTopLevelComponent(
        StaticComponentInput component,
        IReadOnlyDictionary<string, string> assetFilenames,
        ref int count)
    {
        count++;
        return component.Type.Trim().ToLowerInvariant() switch
        {
            "text_display" => TextDisplayPayload(component.Content),
            "separator" => SeparatorPayload(component.Divider, component.Spacing),
            "media_gallery" => MediaGalleryPayload(component.MediaItems, assetFilenames),
            "file" => FilePayload(component.AssetId, component.Spoiler, assetFilenames),
            "section" => SectionPayload(component.Section, assetFilenames, ref count),
            "container" => ContainerPayload(component.Container, assetFilenames, ref count),
            _ => throw InvalidArgument("Static component type is not supported.")
        };
    }

    private static Dictionary<string, object?> NormalizeContainerChild(
        StaticContainerChildInput component,
        IReadOnlyDictionary<string, string> assetFilenames,
        ref int count)
    {
        count++;
        return component.Type.Trim().ToLowerInvariant() switch
        {
            "text_display" => TextDisplayPayload(component.Content),
            "separator" => SeparatorPayload(component.Divider, component.Spacing),
            "media_gallery" => MediaGalleryPayload(component.MediaItems, assetFilenames),
            "file" => FilePayload(component.AssetId, component.Spoiler, assetFilenames),
            _ => throw InvalidArgument("Static container child type is not supported.")
        };
    }

    private static Dictionary<string, object?> TextDisplayPayload(string? content) => new(StringComparer.Ordinal)
    {
        ["type"] = 10,
        ["content"] = NormalizeComponentText(content, 4_000)
    };

    private static Dictionary<string, object?> SeparatorPayload(bool? divider, int? spacing)
    {
        var normalizedSpacing = spacing ?? 1;
        if (normalizedSpacing is not 1 and not 2)
        {
            throw InvalidArgument("Static separator spacing must be 1 or 2.");
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = 14,
            ["divider"] = divider ?? true,
            ["spacing"] = normalizedSpacing
        };
    }

    private static Dictionary<string, object?> MediaGalleryPayload(
        ImmutableArray<StaticMediaItemInput>? items,
        IReadOnlyDictionary<string, string> assetFilenames)
    {
        var source = items.HasValue && !items.Value.IsDefault ? items.Value : [];
        if (source.Length is < 1 or > 10)
        {
            throw InvalidArgument("Static media galleries require 1 to 10 items.");
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = 12,
            ["items"] = source.Select(item => (object)MediaItemPayload(item, assetFilenames)).ToArray()
        };
    }

    private static Dictionary<string, object?> FilePayload(
        string? assetId,
        bool spoiler,
        IReadOnlyDictionary<string, string> assetFilenames)
    {
        var filename = AssetFilename(assetId, assetFilenames);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = 13,
            ["file"] = new Dictionary<string, object?> { ["url"] = $"attachment://{filename}" },
            ["spoiler"] = spoiler
        };
    }

    private static Dictionary<string, object?> SectionPayload(
        StaticSectionInput? section,
        IReadOnlyDictionary<string, string> assetFilenames,
        ref int count)
    {
        if (section is null || section.TextDisplays.IsDefault || section.TextDisplays.Length is < 1 or > 3 || section.Accessory is null)
        {
            throw InvalidArgument("Static sections require 1 to 3 text displays and one accessory.");
        }

        count += section.TextDisplays.Length + 1;
        var accessory = section.Accessory.Type.Trim().ToLowerInvariant() switch
        {
            "thumbnail" => ThumbnailPayload(section.Accessory, assetFilenames),
            "link_button" => LinkButtonPayload(section.Accessory),
            _ => throw InvalidArgument("Static section accessory must be thumbnail or link_button.")
        };
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = 9,
            ["components"] = section.TextDisplays.Select(text => (object)TextDisplayPayload(text)).ToArray(),
            ["accessory"] = accessory
        };
    }

    private static Dictionary<string, object?> ContainerPayload(
        StaticContainerInput? container,
        IReadOnlyDictionary<string, string> assetFilenames,
        ref int count)
    {
        if (container is null || container.Children.IsDefaultOrEmpty)
        {
            throw InvalidArgument("Static containers require at least one child.");
        }

        var children = new List<object>();
        foreach (var child in container.Children)
        {
            children.Add(NormalizeContainerChild(child, assetFilenames, ref count));
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = 17,
            ["components"] = children.ToArray(),
            ["spoiler"] = container.Spoiler
        };
        Add(payload, "accent_color", container.AccentColorHex is null ? null : Convert.ToInt32(NormalizeColor(container.AccentColorHex)[1..], 16));
        return payload;
    }

    private static Dictionary<string, object?> ThumbnailPayload(
        StaticSectionAccessoryInput accessory,
        IReadOnlyDictionary<string, string> assetFilenames) => new(StringComparer.Ordinal)
        {
            ["type"] = 11,
            ["media"] = new Dictionary<string, object?>
            {
                ["url"] = NormalizeMediaReference(accessory.Url, accessory.AssetId, assetFilenames)
            },
            ["description"] = NormalizeOptionalComponentText(accessory.Description, 1_024),
            ["spoiler"] = accessory.Spoiler
        };

    private static Dictionary<string, object?> LinkButtonPayload(StaticSectionAccessoryInput accessory) => new(StringComparer.Ordinal)
    {
        ["type"] = 2,
        ["style"] = 5,
        ["label"] = NormalizeComponentText(accessory.Label, 80),
        ["url"] = NormalizeWebUrl(accessory.Url ?? string.Empty),
        ["disabled"] = false
    };

    private static Dictionary<string, object?> MediaItemPayload(
        StaticMediaItemInput item,
        IReadOnlyDictionary<string, string> assetFilenames) => new(StringComparer.Ordinal)
        {
            ["media"] = new Dictionary<string, object?>
            {
                ["url"] = NormalizeMediaReference(item.Url, item.AssetId, assetFilenames)
            },
            ["description"] = NormalizeOptionalComponentText(item.Description, 1_024),
            ["spoiler"] = item.Spoiler
        };

    private static object ParseObservedEmbed(JsonElement embed)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        CopyOptionalString(embed, payload, "title", 256);
        CopyOptionalString(embed, payload, "description", 4_096);
        CopyOptionalString(embed, payload, "url", 2_048);
        if (embed.TryGetProperty("timestamp", out var timestamp) && timestamp.ValueKind == JsonValueKind.String &&
            timestamp.TryGetDateTimeOffset(out var parsedTimestamp))
        {
            payload["timestamp"] = NormalizeTimestamp(parsedTimestamp).ToString("O", CultureInfo.InvariantCulture);
        }

        if (embed.TryGetProperty("color", out var color) && color.TryGetInt32(out var colorValue)) payload["color"] = colorValue;
        CopyObservedObject(embed, payload, "footer", ("text", 2_048), ("icon_url", 2_048));
        CopyObservedObject(embed, payload, "image", ("url", 2_048), ("description", 1_024));
        CopyObservedObject(embed, payload, "thumbnail", ("url", 2_048), ("description", 1_024));
        CopyObservedObject(embed, payload, "author", ("name", 256), ("url", 2_048), ("icon_url", 2_048));
        if (embed.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            if (fields.GetArrayLength() > 25) throw InvalidResponse("embed fields");
            payload["fields"] = fields.EnumerateArray().Select(field => (object)new Dictionary<string, object?>
            {
                ["name"] = RequiredString(field, "name", 256),
                ["value"] = RequiredString(field, "value", 1_024),
                ["inline"] = field.TryGetProperty("inline", out var inline) && inline.ValueKind == JsonValueKind.True
            }).ToArray();
        }

        return payload;
    }

    private static Dictionary<string, object?> ParseObservedComponent(JsonElement component, ref int count)
    {
        count++;
        return RequiredInt32(component, "type") switch
        {
            2 => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = 2,
                ["style"] = RequiredInt32(component, "style"),
                ["label"] = RequiredString(component, "label", 80),
                ["url"] = RequiredString(component, "url", 2_048),
                ["disabled"] = component.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True
            },
            9 => ParseObservedSection(component, ref count),
            10 => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = 10,
                ["content"] = RequiredString(component, "content", 4_000)
            },
            11 => ParseObservedThumbnail(component),
            12 => ParseObservedMediaGallery(component),
            13 => ParseObservedFile(component),
            14 => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = 14,
                ["divider"] = !component.TryGetProperty("divider", out var divider) || divider.ValueKind == JsonValueKind.True,
                ["spacing"] = component.TryGetProperty("spacing", out var spacing) && spacing.TryGetInt32(out var spacingValue) ? spacingValue : 1
            },
            17 => ParseObservedContainer(component, ref count),
            _ => throw InvalidResponse("unsupported message component")
        };
    }

    private static Dictionary<string, object?> ParseObservedSection(JsonElement component, ref int count)
    {
        var children = RequiredArray(component, "components");
        var values = new List<object>();
        foreach (var child in children.EnumerateArray())
        {
            values.Add(ParseObservedComponent(child, ref count));
        }

        var accessory = component.TryGetProperty("accessory", out var value) && value.ValueKind == JsonValueKind.Object
            ? ParseObservedComponent(value, ref count)
            : throw InvalidResponse("section accessory");
        return new Dictionary<string, object?>
        {
            ["type"] = 9,
            ["components"] = values.ToArray(),
            ["accessory"] = accessory
        };
    }

    private static Dictionary<string, object?> ParseObservedContainer(JsonElement component, ref int count)
    {
        var children = new List<object>();
        foreach (var child in RequiredArray(component, "components").EnumerateArray())
        {
            children.Add(ParseObservedComponent(child, ref count));
        }

        return new Dictionary<string, object?>
        {
            ["type"] = 17,
            ["components"] = children.ToArray(),
            ["spoiler"] = component.TryGetProperty("spoiler", out var spoiler) && spoiler.ValueKind == JsonValueKind.True,
            ["accent_color"] = component.TryGetProperty("accent_color", out var accent) && accent.TryGetInt32(out var accentValue)
                ? accentValue
                : null
        };
    }

    private static Dictionary<string, object?> ParseObservedThumbnail(JsonElement component) => new(StringComparer.Ordinal)
    {
        ["type"] = 11,
        ["media"] = new Dictionary<string, object?>
        {
            ["url"] = RequiredString(RequiredObject(component, "media"), "url", 2_048)
        },
        ["description"] = OptionalString(component, "description", 1_024),
        ["spoiler"] = component.TryGetProperty("spoiler", out var spoiler) && spoiler.ValueKind == JsonValueKind.True
    };

    private static Dictionary<string, object?> ParseObservedMediaGallery(JsonElement component) => new(StringComparer.Ordinal)
    {
        ["type"] = 12,
        ["items"] = RequiredArray(component, "items").EnumerateArray().Select(item => (object)new Dictionary<string, object?>
        {
            ["media"] = new Dictionary<string, object?>
            {
                ["url"] = RequiredString(RequiredObject(item, "media"), "url", 2_048)
            },
            ["description"] = OptionalString(item, "description", 1_024),
            ["spoiler"] = item.TryGetProperty("spoiler", out var spoiler) && spoiler.ValueKind == JsonValueKind.True
        }).ToArray()
    };

    private static Dictionary<string, object?> ParseObservedFile(JsonElement component) => new(StringComparer.Ordinal)
    {
        ["type"] = 13,
        ["file"] = new Dictionary<string, object?>
        {
            ["url"] = RequiredString(RequiredObject(component, "file"), "url", 2_048)
        },
        ["spoiler"] = component.TryGetProperty("spoiler", out var spoiler) && spoiler.ValueKind == JsonValueKind.True
    };

    private static ImmutableArray<RichEmbedFieldInput> NormalizeFields(
        ImmutableArray<RichEmbedFieldInput> fields,
        ref int totalCharacters)
    {
        var source = fields.IsDefault ? [] : fields;
        if (source.Length > 25)
        {
            throw InvalidArgument("A rich embed supports at most 25 fields.");
        }

        var normalized = ImmutableArray.CreateBuilder<RichEmbedFieldInput>();
        foreach (var field in source)
        {
            normalized.Add(new RichEmbedFieldInput(
                NormalizeRequiredText(field.Name, 256, ref totalCharacters),
                NormalizeRequiredText(field.Value, 1_024, ref totalCharacters),
                field.Inline));
        }

        return normalized.ToImmutable();
    }

    private static RichEmbedMediaInput? NormalizeMedia(RichEmbedMediaInput? media) => media is null
        ? null
        : new RichEmbedMediaInput(
            NormalizeMediaUrl(media.Url),
            NormalizeOptionalStandaloneText(media.Description, 1_024));

    private static string? NormalizeOptionalText(string? value, int maximum, ref int total)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = NormalizeText(value, maximum, allowEmpty: true);
        total += normalized.Length;
        return normalized;
    }

    private static string NormalizeRequiredText(string value, int maximum, ref int total)
    {
        var normalized = NormalizeText(value, maximum, allowEmpty: false);
        total += normalized.Length;
        return normalized;
    }

    private static string? NormalizeOptionalStandaloneText(string? value, int maximum) =>
        value is null ? null : NormalizeText(value, maximum, allowEmpty: true);

    private static string NormalizeText(string? value, int maximum, bool allowEmpty)
    {
        var normalized = value ?? string.Empty;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(normalized)) || normalized.Length > maximum ||
            normalized.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
        {
            throw InvalidArgument("Rich message text is empty, too long, or contains invalid control characters.");
        }

        return normalized;
    }

    private static string NormalizeComponentText(string? value, int maximum) =>
        NormalizeText(value, maximum, allowEmpty: false);

    private static string? NormalizeOptionalComponentText(string? value, int maximum) =>
        value is null ? null : NormalizeText(value, maximum, allowEmpty: true);

    private static string NormalizeColor(string value) => $"#{NormalizeColorWithoutPrefix(value)}";

    private static string NormalizeColorWithoutPrefix(string value)
    {
        var normalized = value.Trim().TrimStart('#').ToUpperInvariant();
        return normalized.Length == 6 && normalized.All(char.IsAsciiHexDigit)
            ? normalized
            : throw InvalidArgument("Color must be a six-digit RGB hex value.");
    }

    internal static string NormalizeWebUrl(string value)
    {
        if (value.Length > 2_048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback ||
            System.Net.IPAddress.TryParse(uri.Host, out _))
        {
            throw InvalidArgument("External rich-message URLs must use HTTPS with a non-IP public hostname.");
        }

        return uri.AbsoluteUri;
    }

    private static string NormalizeMediaUrl(string value) => value.StartsWith("attachment://", StringComparison.Ordinal)
        ? NormalizeAttachmentUrl(value)
        : NormalizeWebUrl(value);

    private static string NormalizeAttachmentUrl(string value)
    {
        var filename = value["attachment://".Length..];
        return !string.IsNullOrWhiteSpace(filename) && filename.Length <= 256 &&
            filename.All(character => !char.IsControl(character) && character is not '/' and not '\\')
                ? value
                : throw InvalidArgument("Attachment URL contains an invalid filename.");
    }

    private static string NormalizeMediaReference(
        string? url,
        string? assetId,
        IReadOnlyDictionary<string, string> assetFilenames)
    {
        if ((url is null) == (assetId is null))
        {
            throw InvalidArgument("Static media requires exactly one HTTPS URL or approved asset ID.");
        }

        return assetId is null
            ? NormalizeWebUrl(url!)
            : $"attachment://{AssetFilename(assetId, assetFilenames)}";
    }

    private static string AssetFilename(string? assetId, IReadOnlyDictionary<string, string> assetFilenames)
    {
        var normalized = NormalizeAssetId(assetId ?? string.Empty);
        return assetFilenames.TryGetValue(normalized, out var filename)
            ? filename
            : throw InvalidArgument("Static component references an asset not included in the message asset list.");
    }

    private static string NormalizeAssetId(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is >= 1 and <= 128 && normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? normalized
            : throw InvalidArgument("Asset ID is invalid.");
    }

    private static byte[] DecodeBase64(string value, string field)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw InvalidArgument($"{field} must be valid base64.");
        }
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private static Dictionary<string, object?> MediaPayload(RichEmbedMediaInput media) =>
        OptionalDictionary(("url", media.Url), ("description", media.Description));

    private static Dictionary<string, object?> OptionalDictionary(params (string Name, object? Value)[] values)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var value in values) Add(result, value.Name, value.Value);
        return result;
    }

    private static void Add(Dictionary<string, object?> payload, string name, object? value)
    {
        if (value is not null) payload[name] = value;
    }

    private static int BaseThemeValue(string value) => value switch
    {
        "unset" => 0,
        "dark" => 1,
        "light" => 2,
        "darker" => 3,
        "midnight" => 4,
        _ => throw InvalidArgument("Shared client theme base is invalid.")
    };

    private static string BaseThemeName(int value) => value switch
    {
        0 => "unset",
        1 => "dark",
        2 => "light",
        3 => "darker",
        4 => "midnight",
        _ => throw InvalidResponse("shared client theme base")
    };

    private static void CopyOptionalString(
        JsonElement source,
        Dictionary<string, object?> target,
        string name,
        int maximum)
    {
        var value = OptionalString(source, name, maximum);
        if (value is not null) target[name] = value;
    }

    private static void CopyObservedObject(
        JsonElement source,
        Dictionary<string, object?> target,
        string name,
        params (string Name, int Maximum)[] fields)
    {
        if (!source.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Object) throw InvalidResponse(name);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var text = OptionalString(value, field.Name, field.Maximum);
            if (text is not null) result[field.Name] = text;
        }

        target[name] = result;
    }

    private static JsonElement RequiredArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw InvalidResponse(name);

    private static JsonElement RequiredObject(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw InvalidResponse(name);

    private static string RequiredString(JsonElement root, string name, int maximum) =>
        OptionalString(root, name, maximum) ?? throw InvalidResponse(name);

    private static string? OptionalString(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String) throw InvalidResponse(name);
        var text = value.GetString() ?? string.Empty;
        return text.Length <= maximum ? text : throw InvalidResponse(name);
    }

    private static int RequiredInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : throw InvalidResponse(name);

    private static StewardException InvalidArgument(string message) =>
        new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException InvalidResponse(string resource) =>
        new(StewardErrorCodes.DiscordUnavailable, $"Discord returned invalid {resource} data.");
}
