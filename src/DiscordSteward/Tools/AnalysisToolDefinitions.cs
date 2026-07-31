using System.ComponentModel;
using DiscordSteward.Permissions;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class AnalyzeChannelAccessTool(AnalysisTools tools)
{
    [McpServerTool(Name = "analyze_channel_access", Title = "Analyze channel access", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Produces a role-oriented effective access matrix for one exact channel or category.")]
    public Task<ToolResultEnvelope<ChannelAccessMatrix>> Analyze(
        [Description("Exact Discord channel or category snowflake ID.")] string channel_id,
        CancellationToken cancellationToken) => tools.AnalyzeChannelAccessAsync(channel_id, cancellationToken);
}

[McpServerToolType]
public sealed class AnalyzePrivilegeEscalationPathsTool(AnalysisTools tools)
{
    [McpServerTool(Name = "analyze_privilege_escalation_paths", Title = "Analyze privilege escalation", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Finds role permission combinations that cross Steward trust boundaries.")]
    public Task<ToolResultEnvelope<PrivilegeEscalationAnalysis>> Analyze(CancellationToken cancellationToken) =>
        tools.AnalyzePrivilegeEscalationPathsAsync(cancellationToken);
}

[McpServerToolType]
public sealed class AnalyzeModerationConfigurationTool(AnalysisTools tools)
{
    [McpServerTool(Name = "analyze_moderation_configuration", Title = "Analyze moderation configuration", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Reviews moderator permissions, Administrator roles, and AutoMod coverage without reading messages.")]
    public Task<ToolResultEnvelope<ModerationConfigurationAnalysis>> Analyze(CancellationToken cancellationToken) =>
        tools.AnalyzeModerationConfigurationAsync(cancellationToken);
}

[McpServerToolType]
public sealed class AnalyzeOnboardingConfigurationTool(AnalysisTools tools)
{
    [McpServerTool(Name = "analyze_onboarding_configuration", Title = "Analyze onboarding configuration", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Checks onboarding role/channel references, public defaults, and Discord enablement requirements.")]
    public Task<ToolResultEnvelope<OnboardingConfigurationAnalysis>> Analyze(CancellationToken cancellationToken) =>
        tools.AnalyzeOnboardingConfigurationAsync(cancellationToken);
}

[McpServerToolType]
public sealed class AnalyzeDeletionImpactTool(AnalysisTools tools)
{
    [McpServerTool(Name = "analyze_deletion_impact", Title = "Analyze deletion impact", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Returns blockers, exact related resource IDs, and a versioned cascade digest for one role, channel, or category.")]
    public Task<ToolResultEnvelope<DeletionImpactAnalysis>> Analyze(
        [Description("Resource type: role, channel, or category.")] string resource_type,
        [Description("Exact Discord resource snowflake ID.")] string resource_id,
        CancellationToken cancellationToken) =>
        tools.AnalyzeDeletionImpactAsync(resource_type, resource_id, cancellationToken);
}

[McpServerToolType]
public sealed class ExplainMemberPermissionsTool(AnalysisTools tools)
{
    [McpServerTool(Name = "explain_member_permissions", Title = "Explain member permissions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Explains one exact member's role union, channel role overwrites, exact member overwrite, implicit effects, timeout, and owner/Administrator bypass.")]
    public Task<ToolResultEnvelope<MemberPermissionExplanation>> Explain(
        [Description("Exact Discord user snowflake ID.")] string user_id,
        CancellationToken cancellationToken,
        [Description("Optional exact channel or category snowflake ID.")] string? channel_id = null) =>
        tools.ExplainMemberPermissionsAsync(user_id, channel_id, cancellationToken);
}
