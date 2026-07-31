using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiscordSteward.Operations;

internal static class OperationCanonicalizer
{
    internal static string Serialize(ChannelUpdateInvocation invocation)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("kind", OperationKinds.ChannelUpdate);
            writer.WriteString("requestId", invocation.RequestId);
            writer.WriteString("channelId", invocation.ChannelId);
            writer.WriteString("reason", invocation.Reason);
            writer.WriteString("expectedStateDigest", invocation.ExpectedStateDigest);
            writer.WriteStartObject("set");
            if (invocation.Set.Name is not null)
            {
                writer.WriteString("name", invocation.Set.Name);
            }

            if (invocation.Set.Topic is not null)
            {
                writer.WriteString("topic", invocation.Set.Topic);
            }

            if (invocation.Set.SlowModeSeconds.HasValue)
            {
                writer.WriteNumber("slowModeSeconds", invocation.Set.SlowModeSeconds.Value);
            }

            writer.WriteEndObject();
            writer.WriteStartArray("clear");
            foreach (var field in invocation.Clear.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(field);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    internal static string Serialize(CatalogMutationInvocation invocation)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("kind", invocation.ToolName);
            writer.WriteString("requestId", invocation.RequestId);
            writer.WriteString("resourceId", invocation.ResourceId);
            writer.WriteString("reason", invocation.Reason);
            writer.WritePropertyName("arguments");
            WriteCanonicalElement(writer, invocation.Arguments);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    internal static CatalogMutationInvocation DeserializeCatalogMutation(string canonicalJson)
    {
        try
        {
            using var document = JsonDocument.Parse(canonicalJson);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
            {
                throw new JsonException();
            }

            return new CatalogMutationInvocation(
                root.GetProperty("requestId").GetString() ?? throw new JsonException(),
                root.GetProperty("kind").GetString() ?? throw new JsonException(),
                root.GetProperty("resourceId").GetString() ?? throw new JsonException(),
                root.GetProperty("reason").GetString() ?? throw new JsonException(),
                root.GetProperty("arguments").Clone());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new DiscordSteward.Errors.StewardException(
                DiscordSteward.Errors.StewardErrorCodes.AuditUnavailable,
                "The stored operation invocation is invalid.");
        }
    }

    internal static string ComputeDigest(string canonicalJson) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalJson)));

    internal static string SerializeMetadata(JsonObject metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        using var document = JsonDocument.Parse(metadata.ToJsonString());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalElement(writer, document.RootElement);
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    internal static string NormalizeRequestId(string requestId) =>
        Guid.TryParse(requestId, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D", CultureInfo.InvariantCulture)
            : throw new ArgumentException("Request ID must be a non-empty UUID.", nameof(requestId));

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
