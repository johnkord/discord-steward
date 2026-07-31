namespace DiscordSteward.Operations;

internal interface ICatalogMutationFamilyHandler : ICatalogMutationAdapter;

internal sealed class CatalogMutationAdapter(
    IEnumerable<ICatalogMutationFamilyHandler> handlers) : ICatalogMutationAdapter
{
    private readonly ICatalogMutationFamilyHandler[] _handlers = handlers.ToArray();

    public bool Supports(string toolName) => _handlers.Any(handler => handler.Supports(toolName));

    public Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken) =>
        Resolve(invocation.ToolName).PrepareAsync(invocation, cancellationToken);

    public Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken) =>
        Resolve(invocation.ToolName).WriteAsync(invocation, preparation, cancellationToken);

    public Task<CatalogMutationObservation> ObserveAsync(
        CatalogMutationInvocation invocation,
        string beforeStateJson,
        string desiredStateJson,
        string? priorReceiptJson,
        CancellationToken cancellationToken) =>
        Resolve(invocation.ToolName).ObserveAsync(
            invocation,
            beforeStateJson,
            desiredStateJson,
            priorReceiptJson,
            cancellationToken);

    private ICatalogMutationFamilyHandler Resolve(string toolName) =>
        _handlers.SingleOrDefault(handler => handler.Supports(toolName))
        ?? throw new InvalidOperationException($"No typed mutation handler is registered for {toolName}.");
}
