using System.Collections.Immutable;
using DiscordSteward.Configuration;
using DiscordSteward.Domain;

namespace DiscordSteward.Linting;

public sealed class GuildLinter(StewardPolicy policy)
{
    private static readonly ImmutableHashSet<string> SensitiveChannelPermissions =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ManageChannels",
            "ManageMessages",
            "ManageRoles",
            "ManageWebhooks",
            "MentionEveryone");

    public GuildLintReport Lint(GuildSnapshot snapshot)
    {
        var findings = new List<GuildLintFinding>();

        AddRoleFindings(snapshot, findings);
        AddDuplicateRoleFindings(snapshot, findings);
        AddChannelFindings(snapshot, findings);
        AddDuplicateChannelFindings(snapshot, findings);
        AddCategoryDriftFindings(snapshot, findings);

        var ordered = findings
            .OrderBy(finding => SeverityOrder(finding.Severity))
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.ResourceType, StringComparer.Ordinal)
            .ThenBy(finding => finding.ResourceId, StringComparer.Ordinal)
            .ToImmutableArray();

        return new GuildLintReport(ordered.Length, ordered);
    }

    private void AddRoleFindings(GuildSnapshot snapshot, List<GuildLintFinding> findings)
    {
        foreach (var role in snapshot.Roles)
        {
            if (role.Permissions.Contains("Administrator", StringComparer.Ordinal))
            {
                findings.Add(CreateFinding(
                    "role_administrator",
                    "critical",
                    "role",
                    role.Id,
                    role.Name,
                    "Role grants Administrator, which bypasses all channel permission overwrites.",
                    evidence: ["Administrator"]));
            }

            var elevatedPermissions = role.Permissions
                .Where(policy.DeniedPermissions.Contains)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray();

            if (role.IsEveryone && !elevatedPermissions.IsEmpty)
            {
                findings.Add(CreateFinding(
                    "everyone_role_elevated_permissions",
                    "warning",
                    "role",
                    role.Id,
                    role.Name,
                    "The @everyone role grants permissions denied by Steward policy to every guild member.",
                    evidence: elevatedPermissions));
            }

            if (role.IsManaged)
            {
                findings.Add(CreateFinding(
                    elevatedPermissions.IsEmpty ? "managed_role" : "managed_role_elevated_permissions",
                    elevatedPermissions.IsEmpty ? "info" : "warning",
                    "role",
                    role.Id,
                    role.Name,
                    elevatedPermissions.IsEmpty
                        ? "Role is managed by Discord or an integration and cannot be edited directly."
                        : "Managed role grants denied permissions and can be changed only by its integration provider.",
                    evidence: elevatedPermissions));
            }

            if (role.IsProtected)
            {
                findings.Add(CreateFinding(
                    elevatedPermissions.IsEmpty ? "protected_role" : "protected_role_elevated_permissions",
                    elevatedPermissions.IsEmpty ? "info" : "warning",
                    "role",
                    role.Id,
                    role.Name,
                    elevatedPermissions.IsEmpty
                        ? "Role is protected by Steward policy."
                        : "Protected role grants permissions denied by Steward policy.",
                    evidence: elevatedPermissions));

                if (!role.ProtectionReasons.Contains("resource_id", StringComparer.Ordinal))
                {
                    findings.Add(CreateFinding(
                        "protected_resource_name_only",
                        "warning",
                        "role",
                        role.Id,
                        role.Name,
                        "Resource is protected only by a name prefix; add its exact ID to protectedResourceIds.",
                        evidence: role.ProtectionReasons));
                }
            }
            else if (!role.IsEveryone && !role.IsManaged && !elevatedPermissions.IsEmpty)
            {
                findings.Add(CreateFinding(
                    "role_elevated_permissions",
                    "warning",
                    "role",
                    role.Id,
                    role.Name,
                    "Role grants permissions denied by Steward policy; role hierarchy manageability is not asserted by this snapshot.",
                    evidence: elevatedPermissions));
            }
        }
    }

    private static void AddDuplicateRoleFindings(
        GuildSnapshot snapshot,
        List<GuildLintFinding> findings)
    {
        foreach (var group in snapshot.Roles
                     .GroupBy(role => role.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var roles = group.OrderBy(role => role.Id, StringComparer.Ordinal).ToArray();
            findings.Add(CreateFinding(
                "duplicate_role_name",
                "warning",
                "role",
                roles[0].Id,
                roles[0].Name,
                "Multiple roles have the same case-insensitive display name; use exact IDs for identity.",
                relatedResourceIds: roles.Select(role => role.Id)));
        }
    }

    private void AddChannelFindings(GuildSnapshot snapshot, List<GuildLintFinding> findings)
    {
        foreach (var category in snapshot.Categories)
        {
            AddOverwriteFindings(
                snapshot.Id,
                "category",
                category.Id,
                category.Name,
                category.PermissionOverwrites,
                findings);
            AddNameOnlyProtectionFinding("category", category.Id, category.Name, findings);
        }

        foreach (var channel in snapshot.Channels)
        {
            AddOverwriteFindings(
                snapshot.Id,
                "channel",
                channel.Id,
                channel.Name,
                channel.PermissionOverwrites,
                findings);
            AddNameOnlyProtectionFinding("channel", channel.Id, channel.Name, findings);
        }
    }

    private static void AddOverwriteFindings(
        string guildId,
        string resourceType,
        string resourceId,
        string resourceName,
        ImmutableArray<PermissionOverwriteSnapshot> overwrites,
        List<GuildLintFinding> findings)
    {
        var memberOverwrites = overwrites
            .Where(overwrite => string.Equals(overwrite.TargetType, "member", StringComparison.Ordinal))
            .ToImmutableArray();

        if (!memberOverwrites.IsEmpty)
        {
            var summary = PermissionOverwriteSummary.Create(memberOverwrites);
            findings.Add(CreateFinding(
                "member_specific_overwrite",
                "warning",
                resourceType,
                resourceId,
                resourceName,
                "Resource has member-specific permission overwrites that diverge from role-only policy.",
                evidence:
                [
                    $"member_overwrite_count:{summary.MemberOverwriteCount}",
                    $"member_overwrite_digest:{summary.MemberOverwriteDigest}"
                ]));
        }

        var publicSensitivePermissions = overwrites
            .Where(overwrite =>
                string.Equals(overwrite.TargetType, "role", StringComparison.Ordinal) &&
                string.Equals(overwrite.TargetId, guildId, StringComparison.Ordinal))
            .SelectMany(overwrite => overwrite.AllowedPermissions)
            .Where(SensitiveChannelPermissions.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        if (!publicSensitivePermissions.IsEmpty)
        {
            findings.Add(CreateFinding(
                "public_sensitive_permission",
                "critical",
                resourceType,
                resourceId,
                resourceName,
                "The @everyone overwrite explicitly allows sensitive channel permissions.",
                evidence: publicSensitivePermissions));
        }
    }

    private void AddNameOnlyProtectionFinding(
        string resourceType,
        string resourceId,
        string resourceName,
        List<GuildLintFinding> findings)
    {
        var protection = policy.GetProtection(resourceId, resourceName);
        if (protection.IsProtected && !protection.Reasons.Contains("resource_id", StringComparer.Ordinal))
        {
            findings.Add(CreateFinding(
                "protected_resource_name_only",
                "warning",
                resourceType,
                resourceId,
                resourceName,
                "Resource is protected only by a name prefix; add its exact ID to protectedResourceIds.",
                evidence: protection.Reasons));
        }
    }

    private static void AddDuplicateChannelFindings(
        GuildSnapshot snapshot,
        List<GuildLintFinding> findings)
    {
        var resources = snapshot.Categories
            .Select(category => new NamedResource("category", category.Id, category.Name))
            .Concat(snapshot.Channels.Select(channel => new NamedResource("channel", channel.Id, channel.Name)));

        foreach (var group in resources
                     .GroupBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var duplicates = group.OrderBy(resource => resource.Id, StringComparer.Ordinal).ToArray();
            findings.Add(CreateFinding(
                "duplicate_channel_name",
                "warning",
                duplicates[0].Type,
                duplicates[0].Id,
                duplicates[0].Name,
                "Multiple channels or categories have the same case-insensitive display name; use exact IDs for identity.",
                relatedResourceIds: duplicates.Select(resource => resource.Id)));
        }
    }

    private static void AddCategoryDriftFindings(
        GuildSnapshot snapshot,
        List<GuildLintFinding> findings)
    {
        var categories = snapshot.Categories.ToDictionary(category => category.Id, StringComparer.Ordinal);
        foreach (var channel in snapshot.Channels.Where(channel => channel.CategoryId is not null))
        {
            if (categories.TryGetValue(channel.CategoryId!, out var category) &&
                !OverwritesEqual(channel.PermissionOverwrites, category.PermissionOverwrites))
            {
                findings.Add(CreateFinding(
                    "category_overwrite_drift",
                    "warning",
                    "channel",
                    channel.Id,
                    channel.Name,
                    "Channel permission overwrites differ from its parent category overwrites.",
                    relatedResourceIds: [category.Id]));
            }
        }
    }

    private static bool OverwritesEqual(
        ImmutableArray<PermissionOverwriteSnapshot> left,
        ImmutableArray<PermissionOverwriteSnapshot> right) =>
        left.Length == right.Length && left.Zip(right).All(pair =>
            string.Equals(pair.First.TargetType, pair.Second.TargetType, StringComparison.Ordinal) &&
            string.Equals(pair.First.TargetId, pair.Second.TargetId, StringComparison.Ordinal) &&
            pair.First.AllowedPermissions.SequenceEqual(pair.Second.AllowedPermissions, StringComparer.Ordinal) &&
            pair.First.DeniedPermissions.SequenceEqual(pair.Second.DeniedPermissions, StringComparer.Ordinal));

    private static GuildLintFinding CreateFinding(
        string code,
        string severity,
        string resourceType,
        string resourceId,
        string resourceName,
        string explanation,
        IEnumerable<string>? relatedResourceIds = null,
        IEnumerable<string>? evidence = null) => new(
            code,
            severity,
            resourceType,
            resourceId,
            resourceName,
            explanation,
            (relatedResourceIds ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            (evidence ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray());

    private static int SeverityOrder(string severity) => severity switch
    {
        "critical" => 0,
        "warning" => 1,
        _ => 2
    };

    private sealed record NamedResource(string Type, string Id, string Name);
}

public sealed record GuildLintReport(int FindingCount, ImmutableArray<GuildLintFinding> Findings);

public sealed record GuildLintFinding(
    string Code,
    string Severity,
    string ResourceType,
    string ResourceId,
    string ResourceName,
    string Explanation,
    ImmutableArray<string> RelatedResourceIds,
    ImmutableArray<string> Evidence);
