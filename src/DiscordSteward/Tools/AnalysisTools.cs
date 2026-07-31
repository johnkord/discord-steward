using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Permissions;

namespace DiscordSteward.Tools;

public sealed class AnalysisTools(
    RoleHierarchyAnalyzer roleHierarchyAnalyzer,
    ConfigurationAnalysisService analysisService,
    StewardRuntimeSettings settings)
{
    public Task<ToolResultEnvelope<RoleHierarchyReport>> AnalyzeRoleHierarchyAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => roleHierarchyAnalyzer.AnalyzeAsync(cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<ChannelAccessMatrix>> AnalyzeChannelAccessAsync(
        string channelId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => analysisService.AnalyzeChannelAccessAsync(channelId, cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<PrivilegeEscalationAnalysis>> AnalyzePrivilegeEscalationPathsAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => analysisService.AnalyzePrivilegeEscalationPathsAsync(cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<ModerationConfigurationAnalysis>> AnalyzeModerationConfigurationAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => analysisService.AnalyzeModerationConfigurationAsync(cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<OnboardingConfigurationAnalysis>> AnalyzeOnboardingConfigurationAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => analysisService.AnalyzeOnboardingConfigurationAsync(cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<DeletionImpactAnalysis>> AnalyzeDeletionImpactAsync(
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => analysisService.AnalyzeDeletionImpactAsync(
            resourceType,
            resourceId,
            cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<MemberPermissionExplanation>> ExplainMemberPermissionsAsync(
        string userId,
        string? channelId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => analysisService.ExplainMemberPermissionsAsync(
            userId,
            channelId,
            cancellationToken), cancellationToken);

    private async Task<ToolResultEnvelope<T>> ExecuteAsync<T>(
        Func<Task<T>> analysis,
        CancellationToken cancellationToken)
        where T : class
    {
        var operationId = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        GuildReference? guild = settings.GuildId.HasValue
            ? new GuildReference(settings.GuildId.Value.ToString(CultureInfo.InvariantCulture), Name: null)
            : null;
        try
        {
            return new ToolResultEnvelope<T>(
                1,
                operationId,
                "ok",
                guild,
                await analysis().ConfigureAwait(false),
                Warnings: [],
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException exception)
        {
            return new ToolResultEnvelope<T>(
                1,
                operationId,
                "error",
                guild,
                Data: null,
                Warnings: [],
                new ToolError(exception.Code, exception.Message, operationId));
        }
    }
}
