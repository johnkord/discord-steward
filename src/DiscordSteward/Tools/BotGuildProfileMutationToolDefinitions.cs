using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class UpdateBotGuildProfileTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_bot_guild_profile", Title = "Update bot guild profile", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates Steward's guild-specific nickname, avatar, banner, or bio from an exact prior profile digest and approved image assets.")]
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "MCP wire parameters use snake_case.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete digest-bound profile update and explicit clear list.")] UpdateBotGuildProfileInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("update_bot_guild_profile", request_id, reason, input, cancellationToken);
}
