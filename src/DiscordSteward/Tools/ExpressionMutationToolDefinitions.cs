using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class CreateGuildExpressionTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_guild_expression", Title = "Create guild expression", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one emoji, sticker, or soundboard sound from an immutable digest-bound local asset.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact type, asset, metadata, and expression-list digest.")] CreateGuildExpressionInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_guild_expression", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateGuildExpressionTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_guild_expression", Title = "Update guild expression", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates supported metadata on one exact typed expression after full state validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact expression snowflake ID.")] string expression_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact type, current digest, and complete desired metadata.")] UpdateGuildExpressionInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_guild_expression", request_id, expression_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteGuildExpressionTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_guild_expression", Title = "Delete guild expression", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact typed expression after full state validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact expression snowflake ID.")] string expression_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact type and current expression-state digest.")] DeleteGuildExpressionInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_guild_expression", request_id, expression_id, reason, input, cancellationToken);
}
