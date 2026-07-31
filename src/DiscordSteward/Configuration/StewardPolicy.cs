using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordSteward.Domain;

namespace DiscordSteward.Configuration;

public sealed class StewardPolicy
{
    private static readonly string[] DefaultProtectedNamePrefixes =
    [
        "discsteward",
        "discord-steward"
    ];

    private static readonly string[] DefaultDeniedPermissions =
    [
        "Administrator"
    ];

    private StewardPolicy(
        ImmutableHashSet<string> protectedResourceIds,
        ImmutableArray<string> protectedNamePrefixes,
        ImmutableHashSet<string> deniedPermissions,
        StewardAuthorizationMode authorizationMode)
    {
        ProtectedResourceIds = protectedResourceIds;
        ProtectedNamePrefixes = protectedNamePrefixes;
        DeniedPermissions = deniedPermissions;
        AuthorizationMode = authorizationMode;
        Digest = ComputeDigest();
    }

    public ImmutableHashSet<string> ProtectedResourceIds { get; }

    public ImmutableArray<string> ProtectedNamePrefixes { get; }

    public ImmutableHashSet<string> DeniedPermissions { get; }

    public StewardAuthorizationMode AuthorizationMode { get; }

    public bool IsUnrestricted => AuthorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy;

    public string Digest { get; }

    public static StewardPolicy CreateDefault(
        StewardAuthorizationMode authorizationMode = StewardAuthorizationMode.InteractiveApproval) => Create(
        [],
        DefaultProtectedNamePrefixes,
        DefaultDeniedPermissions,
        authorizationMode);

    public static StewardPolicy Create(
        IEnumerable<string?> protectedResourceIds,
        IEnumerable<string?> protectedNamePrefixes,
        IEnumerable<string?> deniedPermissions,
        StewardAuthorizationMode authorizationMode = StewardAuthorizationMode.InteractiveApproval)
    {
        ArgumentNullException.ThrowIfNull(protectedResourceIds);
        ArgumentNullException.ThrowIfNull(protectedNamePrefixes);
        ArgumentNullException.ThrowIfNull(deniedPermissions);

        if (authorizationMode == StewardAuthorizationMode.UnrestrictedAutonomy)
        {
            return new StewardPolicy(
                ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
                [],
                ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
                authorizationMode);
        }

        return new StewardPolicy(
            protectedResourceIds
                .Select(NormalizeSnowflake)
                .Where(id => id is not null)
                .Select(id => id!)
                .ToImmutableHashSet(StringComparer.Ordinal),
            protectedNamePrefixes
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Select(prefix => prefix!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray(),
            deniedPermissions
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Select(permission => permission!.Trim())
                .ToImmutableHashSet(StringComparer.Ordinal),
            authorizationMode);
    }

    public ResourceProtection GetProtection(string resourceId, string resourceName)
    {
        var reasons = ImmutableArray.CreateBuilder<string>();
        if (ProtectedResourceIds.Contains(resourceId))
        {
            reasons.Add("resource_id");
        }

        foreach (var prefix in ProtectedNamePrefixes)
        {
            if (resourceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"name_prefix:{prefix}");
            }
        }

        return new ResourceProtection(reasons.Count > 0, reasons.ToImmutable());
    }

    private static string? NormalizeSnowflake(string? value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;

    private string ComputeDigest()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", IsUnrestricted ? 4 : 3);
            writer.WriteString("authorizationMode", AuthorizationMode.ToString());
            WriteArray(writer, "protectedResourceIds", ProtectedResourceIds);
            WriteArray(writer, "protectedNamePrefixes", ProtectedNamePrefixes);
            WriteArray(writer, "deniedPermissions", DeniedPermissions);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
