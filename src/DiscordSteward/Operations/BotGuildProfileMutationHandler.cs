using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

internal sealed class BotGuildProfileMutationHandler(
    DiscordApiTransport transport,
    IDiscordExtendedReader reader,
    IAssetVault assetVault,
    StewardRuntimeSettings settings) : ICatalogMutationFamilyHandler
{
    public bool Supports(string toolName) => toolName == "update_bot_guild_profile";

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateBotGuildProfileInput>(invocation);
        if (input.Set is null)
        {
            throw InvalidArgument("The typed profile set object is required.");
        }

        var current = await reader.GetBotGuildProfileAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        var clear = NormalizeClear(input.Clear);
        if (input.Set.Nickname is not null && clear.Contains("nickname") ||
            input.Set.AvatarAssetId is not null && clear.Contains("avatar") ||
            input.Set.BannerAssetId is not null && clear.Contains("banner"))
        {
            throw InvalidArgument("A bot guild profile field cannot be set and cleared together.");
        }

        if (input.Set.AvatarAssetId is not null)
        {
            await ValidateImageAssetAsync(input.Set.AvatarAssetId, cancellationToken).ConfigureAwait(false);
        }

        if (input.Set.BannerAssetId is not null)
        {
            await ValidateImageAssetAsync(input.Set.BannerAssetId, cancellationToken).ConfigureAwait(false);
        }

        var desired = new BotGuildProfileExpectation(
            current.BotUserId,
            clear.Contains("nickname") ? null : input.Set.Nickname is null ? current.Nickname : NormalizeText(input.Set.Nickname, 32, false),
            clear.Contains("avatar") ? null : input.Set.AvatarAssetId is null ? current.AvatarHash : "__present__",
            clear.Contains("banner") ? null : input.Set.BannerAssetId is null ? current.BannerHash : "__present__",
            current.Bio);
        if (desired.Matches(current) && input.Set.AvatarAssetId is null && input.Set.BannerAssetId is null)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The bot guild profile already matches the requested state.");
        }

        return new CatalogMutationPreparation(State(current), State(desired));
    }

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        _ = preparation;
        var input = Arguments<UpdateBotGuildProfileInput>(invocation);
        var clear = NormalizeClear(input.Clear);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (input.Set.Nickname is not null) body["nick"] = NormalizeText(input.Set.Nickname, 32, false);
        if (input.Set.AvatarAssetId is not null)
            body["avatar"] = await ImageDataUriAsync(input.Set.AvatarAssetId, cancellationToken).ConfigureAwait(false);
        if (input.Set.BannerAssetId is not null)
            body["banner"] = await ImageDataUriAsync(input.Set.BannerAssetId, cancellationToken).ConfigureAwait(false);
        if (clear.Contains("nickname")) body["nick"] = null;
        if (clear.Contains("avatar")) body["avatar"] = null;
        if (clear.Contains("banner")) body["banner"] = null;
        var response = await transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/members/@me",
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
        return new CatalogMutationWriteReceipt(response?.GetRawText());
    }

    public async Task<CatalogMutationObservation> ObserveAsync(
        CatalogMutationInvocation invocation,
        string beforeStateJson,
        string desiredStateJson,
        string? priorReceiptJson,
        CancellationToken cancellationToken)
    {
        _ = invocation;
        _ = priorReceiptJson;
        var observed = await reader.GetBotGuildProfileAsync(cancellationToken).ConfigureAwait(false);
        var desired = DeserializeState<BotGuildProfileExpectation>(desiredStateJson);
        var before = DeserializeState<BotGuildProfileView>(beforeStateJson);
        return new CatalogMutationObservation(
            desired.Matches(observed)
                ? CatalogMutationObservationKind.DesiredState
                : ProfileEquals(before, observed)
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
            State(observed));
    }

    private async Task<AssetView> ValidateImageAssetAsync(string assetId, CancellationToken cancellationToken)
    {
        var asset = await assetVault.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        return asset.MediaType is "image/png" or "image/jpeg" or "image/gif" or "image/webp" &&
            asset.ByteLength <= 10L * 1024 * 1024
                ? asset
                : throw InvalidArgument("Bot guild profile images must be supported image assets no larger than 10 MiB.");
    }

    private async Task<string> ImageDataUriAsync(string assetId, CancellationToken cancellationToken)
    {
        var asset = await ValidateImageAssetAsync(assetId, cancellationToken).ConfigureAwait(false);
        var path = await assetVault.GetContentPathAsync(asset.AssetId, cancellationToken).ConfigureAwait(false);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return $"data:{asset.MediaType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static ImmutableHashSet<string> NormalizeClear(ImmutableArray<string> clear)
    {
        var source = clear.IsDefault ? [] : clear;
        var normalized = source.Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
            .ToImmutableHashSet(StringComparer.Ordinal);
        return normalized.All(value => value is "nickname" or "avatar" or "banner")
            ? normalized
            : throw InvalidArgument("Bot guild profile clear contains an unsupported field.");
    }

    private static string NormalizeText(string value, int maximum, bool allowEmpty)
    {
        var text = value ?? string.Empty;
        return (allowEmpty || !string.IsNullOrWhiteSpace(text)) && text.Length <= maximum &&
            !text.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t')
                ? text
                : throw InvalidArgument("Bot guild profile text is empty, too long, or invalid.");
    }

    private static bool ProfileEquals(BotGuildProfileView left, BotGuildProfileView right) =>
        left.BotUserId == right.BotUserId && left.Nickname == right.Nickname &&
        left.AvatarHash == right.AvatarHash && left.BannerHash == right.BannerHash && left.Bio == right.Bio;

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options) ?? throw InvalidArgument("Bot guild profile arguments are invalid.");

    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options) ?? throw InvalidResponse();

    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static void EnsureDigest(string supplied, string observed)
    {
        var value = supplied?.Trim().ToLowerInvariant();
        if (value is not { Length: 64 } || !value.All(char.IsAsciiHexDigit)) throw InvalidArgument("Expected profile digest is invalid.");
        if (value != observed) throw new StewardException(StewardErrorCodes.StateConflict, "Bot guild profile changed after approval.");
    }

    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);

    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException InvalidResponse() =>
        new(StewardErrorCodes.AuditUnavailable, "Stored bot guild profile state is invalid.");

    private sealed record BotGuildProfileExpectation(
        string BotUserId,
        string? Nickname,
        string? AvatarHash,
        string? BannerHash,
        string? Bio)
    {
        internal bool Matches(BotGuildProfileView profile) =>
            profile.BotUserId == BotUserId && profile.Nickname == Nickname && profile.Bio == Bio &&
            HashMatches(AvatarHash, profile.AvatarHash) && HashMatches(BannerHash, profile.BannerHash);

        private static bool HashMatches(string? expected, string? observed) =>
            expected == "__present__" ? observed is not null : expected == observed;
    }
}
