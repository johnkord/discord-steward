using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class CreateAutoModRuleTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_automod_rule", Title = "Create AutoMod rule", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one explicit AutoMod rule bound to the current rule-list digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete trigger, actions, exemptions, and rule-list precondition.")] CreateAutoModRuleInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_automod_rule", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateAutoModRuleTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_automod_rule", Title = "Update AutoMod rule", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Replaces one rule's mutable configuration while preserving its immutable trigger type.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact AutoMod rule snowflake ID.")] string rule_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete mutable configuration and exact current state digest.")] UpdateAutoModRuleInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_automod_rule", request_id, rule_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetAutoModRuleEnabledTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_automod_rule_enabled", Title = "Set AutoMod rule enabled", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Enables or disables one exact AutoMod rule without changing any other field.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact AutoMod rule snowflake ID.")] string rule_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current state digest and desired enabled value.")] SetAutoModRuleEnabledInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_automod_rule_enabled", request_id, rule_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteAutoModRuleTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_automod_rule", Title = "Delete AutoMod rule", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact AutoMod rule after validating its full state digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact AutoMod rule snowflake ID.")] string rule_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current rule-state evidence.")] DeleteAutoModRuleInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_automod_rule", request_id, rule_id, reason, input, cancellationToken);
}
