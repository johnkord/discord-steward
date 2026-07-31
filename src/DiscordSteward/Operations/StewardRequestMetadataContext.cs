using System.Text.Json.Nodes;

namespace DiscordSteward.Operations;

public sealed class StewardRequestMetadataContext
{
    private readonly AsyncLocal<string?> _currentJson = new();

    public string? CurrentJson => _currentJson.Value;

    public IDisposable Push(JsonObject? metadata)
    {
        var previous = _currentJson.Value;
        _currentJson.Value = metadata is null
            ? null
            : OperationCanonicalizer.SerializeMetadata(metadata);
        return new Scope(_currentJson, previous);
    }

    private sealed class Scope(AsyncLocal<string?> currentJson, string? previous) : IDisposable
    {
        public void Dispose() => currentJson.Value = previous;
    }
}
