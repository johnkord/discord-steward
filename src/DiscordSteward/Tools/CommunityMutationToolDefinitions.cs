using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class UpdateGuildMetadataTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_guild_metadata", Title = "Update guild metadata", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates explicit guild name, description, locale, boost bar, or digest-bound branding assets.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete state-bound guild metadata set/clear input.")] UpdateGuildMetadataInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("update_guild_metadata", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateGuildSystemRoutingTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_guild_system_routing", Title = "Update guild system routing", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Atomically updates AFK settings, system flags, and exact system/rules/updates/safety channel IDs.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete state-bound guild routing input.")] UpdateGuildSystemRoutingInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("update_guild_system_routing", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateGuildSafetyTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_guild_safety", Title = "Update guild safety", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates explicit verification, notification-default, and content-filter levels after exact state validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete state-bound guild safety enum values.")] UpdateGuildSafetyInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("update_guild_safety", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateGuildAccessControlsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_guild_access_controls", Title = "Update guild access controls", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Atomically sets invite pause and raid-alert suppression while preserving all other guild features.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
    [Description("Globally unique operation UUID.")] string request_id,
    [Description("Printable operator intent.")] string reason,
    [Description("Exact feature-set digest and explicit access-control values.")] UpdateGuildAccessControlsInput input,
    CancellationToken cancellationToken) =>
    tools.ExecuteForGuildAsync("update_guild_access_controls", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateGuildWidgetTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_guild_widget", Title = "Update guild widget", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Enables or disables the guild widget and sets its exact invite channel.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact widget state digest and desired settings.")] UpdateGuildWidgetInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("update_guild_widget", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateWelcomeScreenTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_welcome_screen", Title = "Update welcome screen", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Replaces welcome-screen enablement, description, and up to five exact channel entries.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete state-bound welcome-screen replacement.")] UpdateWelcomeScreenInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("update_welcome_screen", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateOnboardingTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_onboarding", Title = "Update onboarding", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Atomically replaces all onboarding prompts, options, exact grants, defaults, mode, and enablement.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete state-bound onboarding replacement.")] UpdateOnboardingInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("update_onboarding", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetIncidentActionsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_incident_actions", Title = "Set incident actions", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sets or clears temporary invite and member-DM disablement timestamps, rejecting values beyond 24 hours.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact incident-state digest and explicit timestamps/nulls.")] SetIncidentActionsInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("set_incident_actions", request_id, reason, input, cancellationToken);
}
