using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace DiscordSteward.Domain;

public sealed record PermissionOverwriteSummary(
    ImmutableArray<RolePermissionOverwriteView> RoleOverwrites,
    int MemberOverwriteCount,
    string? MemberOverwriteDigest)
{
    public static PermissionOverwriteSummary Create(
        IEnumerable<PermissionOverwriteSnapshot> overwrites)
    {
        ArgumentNullException.ThrowIfNull(overwrites);
        var normalized = overwrites
            .OrderBy(overwrite => overwrite.TargetType, StringComparer.Ordinal)
            .ThenBy(overwrite => overwrite.TargetId, StringComparer.Ordinal)
            .ToImmutableArray();
        var roleOverwrites = normalized
            .Where(overwrite => string.Equals(overwrite.TargetType, "role", StringComparison.Ordinal))
            .Select(overwrite => new RolePermissionOverwriteView(
                overwrite.TargetId,
                overwrite.AllowedPermissions,
                overwrite.DeniedPermissions))
            .ToImmutableArray();
        var memberOverwrites = normalized
            .Where(overwrite => string.Equals(overwrite.TargetType, "member", StringComparison.Ordinal))
            .ToImmutableArray();

        return new PermissionOverwriteSummary(
            roleOverwrites,
            memberOverwrites.Length,
            memberOverwrites.IsEmpty ? null : ComputeDigest(memberOverwrites));
    }

    private static string ComputeDigest(ImmutableArray<PermissionOverwriteSnapshot> overwrites)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("memberOverwrites");
            foreach (var overwrite in overwrites)
            {
                WriteOverwrite(writer, overwrite);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    internal static void WriteOverwrite(
        Utf8JsonWriter writer,
        PermissionOverwriteSnapshot overwrite)
    {
        writer.WriteStartObject();
        writer.WriteString("targetType", overwrite.TargetType);
        writer.WriteString("targetId", overwrite.TargetId);
        writer.WriteStartArray("allowedPermissions");
        foreach (var permission in overwrite.AllowedPermissions)
        {
            writer.WriteStringValue(permission);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("deniedPermissions");
        foreach (var permission in overwrite.DeniedPermissions)
        {
            writer.WriteStringValue(permission);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

public sealed record RolePermissionOverwriteView(
    string TargetId,
    ImmutableArray<string> AllowedPermissions,
    ImmutableArray<string> DeniedPermissions);
