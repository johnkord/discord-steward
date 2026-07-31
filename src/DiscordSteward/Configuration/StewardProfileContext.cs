using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiscordSteward.Configuration;

public sealed record StewardProfileContext(
    string Name,
    bool IsExplicit,
    string? BotTokenSecretName)
{
    public string Digest => ComputeDigest(Name, IsExplicit, BotTokenSecretName);

    public static StewardProfileContext Create(
        string? name,
        bool isExplicit,
        string? botTokenSecretName)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name)
            ? isExplicit ? throw new InvalidOperationException("Explicit Steward profiles require Steward:ProfileName.") : "legacy"
            : NormalizeIdentifier(name, "profile name");
        var normalizedSecretName = string.IsNullOrWhiteSpace(botTokenSecretName)
            ? null
            : NormalizeIdentifier(botTokenSecretName, "bot-token secret name");
        return new StewardProfileContext(normalizedName, isExplicit, normalizedSecretName);
    }

    public static string? ResolveBotToken(
        string? directToken,
        string? botTokenSecretName,
        Func<string, string?> secretLookup)
    {
        ArgumentNullException.ThrowIfNull(secretLookup);
        if (!string.IsNullOrWhiteSpace(directToken))
        {
            return directToken;
        }

        if (string.IsNullOrWhiteSpace(botTokenSecretName))
        {
            return null;
        }

        var normalizedSecretName = NormalizeIdentifier(botTokenSecretName, "bot-token secret name");
        return secretLookup(normalizedSecretName);
    }

    private static string NormalizeIdentifier(string value, string description)
    {
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 64 ||
            normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidOperationException(
                $"Steward {description} must contain 1 to 64 ASCII letters, digits, periods, underscores, or hyphens.");
        }

        return normalized;
    }

    private static string ComputeDigest(string name, bool isExplicit, string? botTokenSecretName)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteBoolean("isExplicit", isExplicit);
            writer.WriteString("botTokenSecretName", botTokenSecretName);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
