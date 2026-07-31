using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Snapshots;

namespace DiscordSteward.Tools;

public sealed class ConfigurationSnapshotTools(
    ConfigurationSnapshotService service,
    StewardRuntimeSettings settings)
{
    public Task<ToolResultEnvelope<ConfigurationSnapshotCapture>> CaptureAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => service.CaptureAsync(cancellationToken), cancellationToken);

    public Task<ToolResultEnvelope<ConfigurationComparison>> CompareAsync(
        string snapshotId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => service.CompareAsync(snapshotId, cancellationToken), cancellationToken);

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
