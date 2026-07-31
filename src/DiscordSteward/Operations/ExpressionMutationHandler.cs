using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

internal sealed class ExpressionMutationHandler(
    DiscordApiTransport transport,
    IDiscordExtendedReader reader,
    IAssetVault assetVault,
    StewardRuntimeSettings settings) : ICatalogMutationFamilyHandler
{
    private static readonly ImmutableHashSet<string> ToolNames = new[]
    {
        "create_guild_expression",
        "update_guild_expression",
        "delete_guild_expression"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (invocation.ToolName == "create_guild_expression")
        {
            var input = Arguments<CreateGuildExpressionInput>(invocation);
            var type = NormalizeType(input.ExpressionType);
            var items = await reader.ListGuildExpressionsAsync(type, cancellationToken).ConfigureAwait(false);
            EnsureDigest(input.ExpectedExpressionsStateDigest, ResourceStateCanonicalizer.ComputeObjectDigest(items));
            var asset = await ValidateAssetAsync(type, input.AssetId, cancellationToken).ConfigureAwait(false);
            var metadata = NormalizeMetadata(type, input.Metadata);
            return new CatalogMutationPreparation(
                State(new ExpressionCollectionState(type, ResourceStateCanonicalizer.ComputeObjectDigest(items))),
                State(new ExpressionCreationExpectation(type, asset.AssetId, metadata)));
        }

        var expressionType = invocation.ToolName == "update_guild_expression"
            ? NormalizeType(Arguments<UpdateGuildExpressionInput>(invocation).ExpressionType)
            : NormalizeType(Arguments<DeleteGuildExpressionInput>(invocation).ExpressionType);
        var current = await reader.GetGuildExpressionAsync(expressionType, invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        return invocation.ToolName switch
        {
            "update_guild_expression" => PrepareUpdate(invocation, current, expressionType),
            "delete_guild_expression" => PrepareDelete(invocation, current),
            _ => throw Unsupported()
        };
    }

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response = invocation.ToolName switch
        {
            "create_guild_expression" => await WriteCreateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_expression" => await WriteUpdateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_guild_expression" => await WriteDeleteAsync(invocation, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };
        return new CatalogMutationWriteReceipt(response?.GetRawText());
    }

    public async Task<CatalogMutationObservation> ObserveAsync(
        CatalogMutationInvocation invocation,
        string beforeStateJson,
        string desiredStateJson,
        string? priorReceiptJson,
        CancellationToken cancellationToken)
    {
        if (invocation.ToolName == "create_guild_expression")
        {
            var input = Arguments<CreateGuildExpressionInput>(invocation);
            var id = CreatedId(priorReceiptJson);
            var observed = await TryGetAsync(input.ExpressionType, id, cancellationToken).ConfigureAwait(false);
            if (observed is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new ExpressionExistenceState(id, Exists: false)));
            }

            var desired = DeserializeState<ExpressionCreationExpectation>(desiredStateJson);
            return new CatalogMutationObservation(
                desired.Matches(observed)
                    ? CatalogMutationObservationKind.DesiredState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        var type = invocation.ToolName == "update_guild_expression"
            ? Arguments<UpdateGuildExpressionInput>(invocation).ExpressionType
            : Arguments<DeleteGuildExpressionInput>(invocation).ExpressionType;
        var current = await TryGetAsync(type, invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (invocation.ToolName == "delete_guild_expression")
        {
            if (current is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.DesiredState,
                    State(new ExpressionExistenceState(invocation.ResourceId, Exists: false)));
            }

            return Observation(current, beforeStateJson, desiredStateJson);
        }

        return current is null
            ? new CatalogMutationObservation(
                CatalogMutationObservationKind.UnexpectedState,
                State(new ExpressionExistenceState(invocation.ResourceId, Exists: false)))
            : Observation(current, beforeStateJson, desiredStateJson);
    }

    private static CatalogMutationPreparation PrepareUpdate(
        CatalogMutationInvocation invocation,
        GuildExpressionView current,
        string type)
    {
        var input = Arguments<UpdateGuildExpressionInput>(invocation);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        var metadata = NormalizeMetadata(type, input.Metadata);
        var desiredBase = new GuildExpressionView(
            type,
            current.Id,
            metadata.Name,
            current.IsAvailable,
            current.IsManaged,
            current.IsAnimated,
            metadata.Description,
            metadata.Tags,
            metadata.RoleIds,
            metadata.Volume,
            metadata.EmojiId ?? metadata.EmojiName);
        var desired = desiredBase with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(desiredBase) };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private static CatalogMutationPreparation PrepareDelete(
        CatalogMutationInvocation invocation,
        GuildExpressionView current)
    {
        var input = Arguments<DeleteGuildExpressionInput>(invocation);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        return new CatalogMutationPreparation(
            State(current),
            State(new ExpressionExistenceState(current.Id, Exists: false)));
    }

    private async Task<JsonElement?> WriteCreateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateGuildExpressionInput>(invocation);
        var type = NormalizeType(input.ExpressionType);
        var asset = await ValidateAssetAsync(type, input.AssetId, cancellationToken).ConfigureAwait(false);
        var metadata = NormalizeMetadata(type, input.Metadata);
        if (type == "sticker")
        {
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(metadata.Name), "name");
            multipart.Add(new StringContent(metadata.Description ?? string.Empty), "description");
            multipart.Add(new StringContent(string.Join(',', metadata.Tags)), "tags");
            var path = await assetVault.GetContentPathAsync(asset.AssetId, cancellationToken).ConfigureAwait(false);
            var content = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(asset.MediaType);
            multipart.Add(content, "file", asset.Filename);
            return await transport.SendContentAsync(
                HttpMethod.Post,
                $"guilds/{GuildId()}/stickers",
                multipart,
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false);
        }

        var data = await AssetDataUriAsync(asset, cancellationToken).ConfigureAwait(false);
        var body = type == "emoji"
            ? new Dictionary<string, object?>
            {
                ["name"] = metadata.Name,
                ["image"] = data,
                ["roles"] = metadata.RoleIds
            }
            : new Dictionary<string, object?>
            {
                ["name"] = metadata.Name,
                ["sound"] = data,
                ["volume"] = metadata.Volume,
                ["emoji_id"] = metadata.EmojiId,
                ["emoji_name"] = metadata.EmojiName
            };
        return await transport.SendJsonAsync(
            HttpMethod.Post,
            CollectionRoute(type),
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonElement?> WriteUpdateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildExpressionInput>(invocation);
        var type = NormalizeType(input.ExpressionType);
        var metadata = NormalizeMetadata(type, input.Metadata);
        var body = type switch
        {
            "emoji" => new Dictionary<string, object?>
            {
                ["name"] = metadata.Name,
                ["roles"] = metadata.RoleIds
            },
            "sticker" => new Dictionary<string, object?>
            {
                ["name"] = metadata.Name,
                ["description"] = metadata.Description,
                ["tags"] = string.Join(',', metadata.Tags)
            },
            _ => new Dictionary<string, object?>
            {
                ["name"] = metadata.Name,
                ["volume"] = metadata.Volume,
                ["emoji_id"] = metadata.EmojiId,
                ["emoji_name"] = metadata.EmojiName
            }
        };
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            ItemRoute(type, invocation.ResourceId),
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteDeleteAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var type = NormalizeType(Arguments<DeleteGuildExpressionInput>(invocation).ExpressionType);
        return transport.SendJsonAsync(
            HttpMethod.Delete,
            ItemRoute(type, invocation.ResourceId),
            body: null,
            AuditReason(invocation),
            cancellationToken);
    }

    private async Task<GuildExpressionView?> TryGetAsync(
        string expressionType,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.GetGuildExpressionAsync(NormalizeType(expressionType), NormalizeSnowflake(id), cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
            return null;
        }
    }

    private async Task<AssetView> ValidateAssetAsync(string type, string assetId, CancellationToken cancellationToken)
    {
        var asset = await assetVault.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        var valid = type switch
        {
            "emoji" => asset.MediaType.StartsWith("image/", StringComparison.Ordinal) && asset.ByteLength <= 256 * 1024,
            "sticker" => asset.MediaType == "image/png" && asset.ByteLength <= 512 * 1024,
            "soundboard" => asset.MediaType is "audio/ogg" or "audio/mpeg" && asset.ByteLength <= 512 * 1024,
            _ => false
        };
        return valid ? asset : throw InvalidArgument("Asset type or size is invalid for the selected expression type.");
    }

    private async Task<string> AssetDataUriAsync(AssetView asset, CancellationToken cancellationToken)
    {
        var path = await assetVault.GetContentPathAsync(asset.AssetId, cancellationToken).ConfigureAwait(false);
        return $"data:{asset.MediaType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false))}";
    }

    private static GuildExpressionMetadataInput NormalizeMetadata(string type, GuildExpressionMetadataInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var nameLimit = type == "soundboard" ? 32 : 100;
        var name = NormalizeText(input.Name, nameLimit, false);
        if (type == "emoji")
        {
            return new GuildExpressionMetadataInput(
                name,
                null,
                [],
                NormalizeIds(input.RoleIds, 100),
                null,
                null,
                null);
        }

        if (type == "sticker")
        {
            var tags = input.Tags.Select(tag => NormalizeText(tag, 100, false)).ToImmutableArray();
            if (tags.IsEmpty || tags.Length > 10) throw InvalidArgument("Stickers require 1 to 10 bounded tag values.");
            return new GuildExpressionMetadataInput(
                name,
                input.Description is null ? null : NormalizeText(input.Description, 100, true),
                tags,
                [],
                null,
                null,
                null);
        }

        if (input.Volume is < 0 or > 1 || input.EmojiId is not null && input.EmojiName is not null)
        {
            throw InvalidArgument("Soundboard volume or emoji metadata is invalid.");
        }

        return new GuildExpressionMetadataInput(
            name,
            null,
            [],
            [],
            input.Volume ?? 1,
            input.EmojiId is null ? null : NormalizeSnowflake(input.EmojiId),
            input.EmojiName is null ? null : NormalizeText(input.EmojiName, 100, false));
    }

    private string CollectionRoute(string type) => type switch
    {
        "emoji" => $"guilds/{GuildId()}/emojis",
        "sticker" => $"guilds/{GuildId()}/stickers",
        _ => $"guilds/{GuildId()}/soundboard-sounds"
    };

    private string ItemRoute(string type, string id) => $"{CollectionRoute(type)}/{NormalizeSnowflake(id)}";

    private static string NormalizeType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "emoji" => "emoji",
        "sticker" => "sticker",
        "soundboard" => "soundboard",
        _ => throw InvalidArgument("Expression type must be emoji, sticker, or soundboard.")
    };

    private static ImmutableArray<string> NormalizeIds(ImmutableArray<string> values, int maximum)
    {
        if (values.IsDefault || values.Length > maximum) throw InvalidArgument("Expression role array exceeds its limit.");
        return values.Select(NormalizeSnowflake).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static string NormalizeText(string value, int maximum, bool allowEmpty)
    {
        var text = value?.Trim() ?? string.Empty;
        return (allowEmpty || text.Length > 0) && text.Length <= maximum && !text.Any(char.IsControl)
            ? text
            : throw InvalidArgument("Expression text is empty, too long, or invalid.");
    }

    private static CatalogMutationObservation Observation<T>(T observed, string before, string desired)
    {
        var json = State(observed);
        return new CatalogMutationObservation(
            JsonEqual(json, desired) ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(json, before) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            json);
    }

    private static bool JsonEqual(string left, string right)
    {
        using var a = JsonDocument.Parse(left);
        using var b = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(a.RootElement, b.RootElement);
    }

    private static string CreatedId(string? receipt)
    {
        if (receipt is null) throw InvalidResponse();
        using var document = JsonDocument.Parse(receipt);
        var name = document.RootElement.TryGetProperty("sound_id", out _) ? "sound_id" : "id";
        return document.RootElement.TryGetProperty(name, out var id) && id.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(id.GetString() ?? string.Empty)
            : throw InvalidResponse();
    }

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options) ?? throw InvalidArgument("Mutation arguments are invalid.");
    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options) ?? throw InvalidResponse();
    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static void EnsureDigest(string? supplied, string observed)
    {
        var value = supplied?.Trim().ToLowerInvariant();
        if (value is not { Length: 64 } || !value.All(char.IsAsciiHexDigit)) throw InvalidArgument("Expected digest is invalid.");
        if (value != observed) throw new StewardException(StewardErrorCodes.StateConflict, "Expression state changed after approval.");
    }

    private static string NormalizeSnowflake(string value) => ChannelApiState.NormalizeSnowflake(value);
    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);
    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);
    private static StewardException InvalidResponse() => new(StewardErrorCodes.DiscordUnavailable, "Discord returned invalid expression state.");
    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "Expression mutation is not supported.");

    private sealed record ExpressionCollectionState(string ExpressionType, string StateDigest);
    private sealed record ExpressionExistenceState(string ExpressionId, bool Exists);
    private sealed record ExpressionCreationExpectation(
        string ExpressionType,
        string AssetId,
        GuildExpressionMetadataInput Metadata)
    {
        internal bool Matches(GuildExpressionView observed) =>
            observed.ExpressionType == ExpressionType && observed.Name == Metadata.Name &&
            observed.Description == Metadata.Description && observed.Tags.SequenceEqual(Metadata.Tags, StringComparer.Ordinal) &&
            observed.RoleIds.SequenceEqual(Metadata.RoleIds, StringComparer.Ordinal) && observed.Volume == Metadata.Volume &&
            observed.Emoji == (Metadata.EmojiId ?? Metadata.EmojiName);
    }
}
