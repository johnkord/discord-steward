using System.Collections.Immutable;

namespace DiscordSteward.Operations;

public sealed record GuildExpressionMetadataInput(
    string Name,
    string? Description,
    ImmutableArray<string> Tags,
    ImmutableArray<string> RoleIds,
    double? Volume,
    string? EmojiId,
    string? EmojiName);

public sealed record CreateGuildExpressionInput(
    string ExpectedExpressionsStateDigest,
    string ExpressionType,
    string AssetId,
    GuildExpressionMetadataInput Metadata);

public sealed record UpdateGuildExpressionInput(
    string ExpectedStateDigest,
    string ExpressionType,
    GuildExpressionMetadataInput Metadata);

public sealed record DeleteGuildExpressionInput(
    string ExpectedStateDigest,
    string ExpressionType);
