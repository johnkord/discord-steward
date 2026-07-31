using System.ComponentModel;
using DiscordSteward.Assets;
using DiscordSteward.Discord;
using DiscordSteward.Permissions;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class ListAssetsTool(LocalCatalogTools tools)
{
    [McpServerTool(Name = "list_assets", Title = "List approved assets", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Lists bounded metadata and digests for regular-file inbox candidates and immutable vault assets.")]
    public Task<ToolResultEnvelope<AssetCatalogView>> List(CancellationToken cancellationToken) =>
        tools.ListAssetsAsync(cancellationToken);
}

[McpServerToolType]
public sealed class RegisterAssetTool(LocalCatalogTools tools)
{
    [McpServerTool(Name = "register_asset", Title = "Register approved asset", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Imports one exact regular-file candidate into the immutable local vault after digest, media type, and length validation.")]
    public Task<ToolResultEnvelope<AssetView>> Register(
        [Description("Candidate ID returned by list_assets.")] string candidate_id,
        [Description("Lowercase SHA-256 digest returned by list_assets.")] string expected_digest,
        [Description("Allowed image or audio media type.")] string media_type,
        [Description("Exact approved byte length.")] long byte_length,
        CancellationToken cancellationToken) =>
        tools.RegisterAssetAsync(candidate_id, expected_digest, media_type, byte_length, cancellationToken);
}

[McpServerToolType]
public sealed class GetAssetTool(LocalCatalogTools tools)
{
    [McpServerTool(Name = "get_asset", Title = "Get approved asset", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns immutable local asset metadata and digest without raw file data.")]
    public Task<ToolResultEnvelope<AssetView>> Get(
        [Description("Exact local asset ID.")] string asset_id,
        CancellationToken cancellationToken) => tools.GetAssetAsync(asset_id, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteAssetTool(LocalCatalogTools tools)
{
    [McpServerTool(Name = "delete_asset", Title = "Delete unreferenced asset", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Deletes one exact local asset only when no durable reference exists.")]
    public Task<ToolResultEnvelope<AssetView>> Delete(
        [Description("Exact local asset ID.")] string asset_id,
        CancellationToken cancellationToken) => tools.DeleteAssetAsync(asset_id, cancellationToken);
}

[McpServerToolType]
public sealed class SimulateAutoModRuleTool(LocalCatalogTools tools)
{
    [McpServerTool(Name = "simulate_automod_rule", Title = "Simulate AutoMod rule", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Evaluates one supplied normalized AutoMod rule against explicit sample text locally without sending the text to Discord.")]
    public Task<ToolResultEnvelope<AutoModSimulationResult>> Simulate(
        [Description("Normalized rule returned by an AutoMod read tool.")] AutoModRuleView rule,
        [Description("Sample type: message or profile.")] string sample_type,
        [Description("Explicit sample text, maximum 4000 characters.")] string sample_text,
        CancellationToken cancellationToken) =>
        tools.SimulateAutoModRuleAsync(rule, sample_type, sample_text, cancellationToken);
}
