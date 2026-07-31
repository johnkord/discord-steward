using System.Globalization;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Permissions;

namespace DiscordSteward.Tools;

public sealed class LocalCatalogTools(
    IAssetVault assetVault,
    StewardRuntimeSettings settings)
{
    public Task<ToolResultEnvelope<AssetCatalogView>> ListAssetsAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(() => assetVault.ListAsync(cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<AssetView>> RegisterAssetAsync(
        string candidateId,
        string expectedDigest,
        string mediaType,
        long byteLength,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => assetVault.RegisterAsync(
            candidateId,
            expectedDigest,
            mediaType,
            byteLength,
            cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<AssetView>> GetAssetAsync(
        string assetId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => assetVault.GetAsync(assetId, cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<AssetView>> DeleteAssetAsync(
        string assetId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => assetVault.DeleteAsync(assetId, cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<AutoModSimulationResult>> SimulateAutoModRuleAsync(
        AutoModRuleView rule,
        string sampleType,
        string sampleText,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => Task.FromResult(AutoModSimulator.Simulate(rule, sampleType, sampleText)),
            cancellationToken);

    private async Task<ToolResultEnvelope<T>> ExecuteAsync<T>(
        Func<Task<T>> operation,
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
                await operation().ConfigureAwait(false),
                Warnings: [],
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StewardException exception)
        {
            return Error<T>(operationId, guild, exception.Code, exception.Message);
        }
        catch (IOException)
        {
            return Error<T>(
                operationId,
                guild,
                StewardErrorCodes.AuditUnavailable,
                "The local asset vault is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return Error<T>(
                operationId,
                guild,
                StewardErrorCodes.PolicyDenied,
                "The local asset vault path is not accessible.");
        }
    }

    private static ToolResultEnvelope<T> Error<T>(
        string operationId,
        GuildReference? guild,
        string code,
        string message)
        where T : class => new(
            1,
            operationId,
            "error",
            guild,
            Data: null,
            Warnings: [],
            new ToolError(code, message, operationId));
}
