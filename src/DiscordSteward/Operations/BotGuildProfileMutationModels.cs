using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record BotGuildProfileSet(
    string? Nickname,
    string? AvatarAssetId,
    string? BannerAssetId);

public sealed record UpdateBotGuildProfileInput(
    string ExpectedStateDigest,
    BotGuildProfileSet Set,
    ImmutableArray<string> Clear);
