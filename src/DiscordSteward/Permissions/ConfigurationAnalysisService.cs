using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;

namespace DiscordSteward.Permissions;

public sealed record ChannelRoleAccess(
    string RoleId,
    string RoleName,
    bool CanView,
    bool CanSend,
    bool CanConnect,
    ImmutableArray<string> EffectivePermissions);

public sealed record ChannelAccessMatrix(
    string ChannelId,
    string ChannelName,
    string ChannelKind,
    ImmutableArray<ChannelRoleAccess> Roles);

public sealed record PrivilegeEscalationPath(
    string Code,
    string Severity,
    string RoleId,
    string RoleName,
    ImmutableArray<string> EnablingPermissions,
    string Explanation);

public sealed record PrivilegeEscalationAnalysis(
    int FindingCount,
    ImmutableArray<PrivilegeEscalationPath> Findings);

public sealed record ModerationConfigurationAnalysis(
    ImmutableArray<string> AdministratorRoleIds,
    ImmutableArray<string> ModeratorRoleIds,
    int AutoModRuleCount,
    int EnabledAutoModRuleCount,
    ImmutableArray<string> Warnings);

public sealed record OnboardingConfigurationAnalysis(
    bool IsEnabled,
    bool IsBelowRequirements,
    int DefaultChannelCount,
    int PublicSendableDefaultChannelCount,
    ImmutableArray<string> MissingRoleIds,
    ImmutableArray<string> MissingChannelIds,
    ImmutableArray<string> Warnings);

public sealed record CascadeReference(
    string ResourceType,
    string ResourceId,
    string Relationship);

public sealed record DeletionImpactAnalysis(
    string ResourceType,
    string ResourceId,
    string CascadeDigest,
    ImmutableArray<string> Blockers,
    ImmutableArray<CascadeReference> Cascade);

public sealed class ConfigurationAnalysisService(
    IDiscordGuildReader guildReader,
    IDiscordExtendedReader extendedReader,
    IDiscordMemberReader memberReader,
    StewardRuntimeSettings settings)
{
    public async Task<ChannelAccessMatrix> AnalyzeChannelAccessAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var normalizedId = NormalizeSnowflake(channelId);
        var channel = snapshot.Categories
            .Select(category => new { category.Id, category.Name, Kind = "category" })
            .Concat(snapshot.Channels.Select(item => new { item.Id, item.Name, item.Kind }))
            .SingleOrDefault(item => string.Equals(item.Id, normalizedId, StringComparison.Ordinal))
            ?? throw NotFound();
        var roles = snapshot.Roles.Select(role =>
        {
            var explanation = PermissionExplainer.ExplainRole(snapshot, role.Id, normalizedId);
            var permissions = explanation.EffectivePermissions;
            return new ChannelRoleAccess(
                role.Id,
                role.Name,
                permissions.Contains("ViewChannel", StringComparer.Ordinal),
                permissions.Contains("SendMessages", StringComparer.Ordinal) ||
                    permissions.Contains("SendMessagesInThreads", StringComparer.Ordinal),
                permissions.Contains("Connect", StringComparer.Ordinal),
                permissions);
        }).ToImmutableArray();
        return new ChannelAccessMatrix(channel.Id, channel.Name, channel.Kind, roles);
    }

    public async Task<PrivilegeEscalationAnalysis> AnalyzePrivilegeEscalationPathsAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var findings = ImmutableArray.CreateBuilder<PrivilegeEscalationPath>();
        foreach (var role in snapshot.Roles)
        {
            if (role.Permissions.Contains("Administrator", StringComparer.Ordinal))
            {
                findings.Add(CreatePath(
                    "administrator_bypass",
                    "critical",
                    role,
                    ["Administrator"],
                    "Administrator bypasses every channel overwrite and permission boundary."));
            }

            if (role.Permissions.Contains("ManageRoles", StringComparer.Ordinal) &&
                role.Permissions.Contains("ManageWebhooks", StringComparer.Ordinal))
            {
                findings.Add(CreatePath(
                    "role_and_webhook_control",
                    "critical",
                    role,
                    ["ManageRoles", "ManageWebhooks"],
                    "Role management plus webhook control can create durable posting authority across trust boundaries."));
            }

            if (role.Permissions.Contains("ManageRoles", StringComparer.Ordinal) &&
                role.Permissions.Contains("ManageChannels", StringComparer.Ordinal))
            {
                findings.Add(CreatePath(
                    "role_and_channel_control",
                    "warning",
                    role,
                    ["ManageRoles", "ManageChannels"],
                    "Role and channel control can alter both guild privileges and private-space access."));
            }

            if (role.IsEveryone && role.Permissions.Contains("MentionEveryone", StringComparer.Ordinal))
            {
                findings.Add(CreatePath(
                    "everyone_mass_mention",
                    "warning",
                    role,
                    ["MentionEveryone"],
                    "The @everyone role lets every member trigger mass notifications."));
            }
        }

        var ordered = findings
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.RoleId, StringComparer.Ordinal)
            .ToImmutableArray();
        return new PrivilegeEscalationAnalysis(ordered.Length, ordered);
    }

    public async Task<ModerationConfigurationAnalysis> AnalyzeModerationConfigurationAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var rules = await extendedReader.ListAutoModRulesAsync(cancellationToken).ConfigureAwait(false);
        var administratorRoles = snapshot.Roles
            .Where(role => role.Permissions.Contains("Administrator", StringComparer.Ordinal))
            .Select(role => role.Id)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var moderationPermissions = new[]
        {
            "BanMembers", "KickMembers", "ManageMessages", "ManageRoles", "ModerateMembers"
        };
        var moderatorRoles = snapshot.Roles
            .Where(role => role.Permissions.Intersect(moderationPermissions, StringComparer.Ordinal).Any())
            .Select(role => role.Id)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var warnings = ImmutableArray.CreateBuilder<string>();
        if (!administratorRoles.IsEmpty)
        {
            warnings.Add("One or more roles grant Administrator.");
        }

        if (rules.IsEmpty)
        {
            warnings.Add("No AutoMod rules are configured.");
        }

        return new ModerationConfigurationAnalysis(
            administratorRoles,
            moderatorRoles,
            rules.Length,
            rules.Count(rule => rule.Enabled),
            warnings.ToImmutable());
    }

    public async Task<OnboardingConfigurationAnalysis> AnalyzeOnboardingConfigurationAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        OnboardingView onboarding;
        try
        {
            onboarding = await extendedReader.GetOnboardingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
            return new OnboardingConfigurationAnalysis(
                IsEnabled: false,
                IsBelowRequirements: true,
                DefaultChannelCount: 0,
                PublicSendableDefaultChannelCount: 0,
                MissingRoleIds: [],
                MissingChannelIds: [],
                Warnings: ["Guild onboarding is not configured."]);
        }

        var roleIds = snapshot.Roles.Select(role => role.Id).ToHashSet(StringComparer.Ordinal);
        var channelIds = snapshot.Categories.Select(category => category.Id)
            .Concat(snapshot.Channels.Select(channel => channel.Id))
            .ToHashSet(StringComparer.Ordinal);
        var referencedRoles = onboarding.Prompts.SelectMany(prompt => prompt.Options).SelectMany(option => option.RoleIds);
        var referencedChannels = onboarding.DefaultChannelIds
            .Concat(onboarding.Prompts.SelectMany(prompt => prompt.Options).SelectMany(option => option.ChannelIds));
        var missingRoles = referencedRoles.Except(roleIds, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var missingChannels = referencedChannels.Except(channelIds, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var publicSendable = onboarding.DefaultChannelIds.Count(id =>
        {
            try
            {
                var explanation = PermissionExplainer.ExplainRole(snapshot, snapshot.Id, id);
                return explanation.EffectivePermissions.Contains("ViewChannel", StringComparer.Ordinal) &&
                    explanation.EffectivePermissions.Contains("SendMessages", StringComparer.Ordinal);
            }
            catch (ArgumentException)
            {
                return false;
            }
        });
        var warnings = ImmutableArray.CreateBuilder<string>();
        if (!missingRoles.IsEmpty || !missingChannels.IsEmpty)
        {
            warnings.Add("Onboarding contains references not present in the current guild snapshot.");
        }

        if (onboarding.IsEnabled && (onboarding.DefaultChannelIds.Length < 7 || publicSendable < 5))
        {
            warnings.Add("Enabled onboarding does not meet Discord's documented default-channel requirements.");
        }

        return new OnboardingConfigurationAnalysis(
            onboarding.IsEnabled,
            onboarding.IsBelowRequirements,
            onboarding.DefaultChannelIds.Length,
            publicSendable,
            missingRoles,
            missingChannels,
            warnings.ToImmutable());
    }

    public async Task<DeletionImpactAnalysis> AnalyzeDeletionImpactAsync(
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var normalizedType = resourceType?.Trim().ToLowerInvariant();
        var normalizedId = NormalizeSnowflake(resourceId);
        var blockers = ImmutableArray.CreateBuilder<string>();
        var cascade = ImmutableArray.CreateBuilder<CascadeReference>();
        var autoMod = await extendedReader.ListAutoModRulesAsync(cancellationToken).ConfigureAwait(false);
        OnboardingView? onboarding = null;
        try
        {
            onboarding = await extendedReader.GetOnboardingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
        }

        if (normalizedType == "role")
        {
            var role = snapshot.Roles.SingleOrDefault(item => string.Equals(item.Id, normalizedId, StringComparison.Ordinal))
                ?? throw NotFound();
            if (role.IsEveryone) blockers.Add("The @everyone role cannot be deleted.");
            if (role.IsManaged) blockers.Add("Discord or an integration manages this role.");
            if (role.IsProtected) blockers.Add("Local Steward policy protects this role.");
            var counts = await memberReader.GetRoleMemberCountsAsync(cancellationToken).ConfigureAwait(false);
            var memberCount = counts.SingleOrDefault(item => string.Equals(item.RoleId, normalizedId, StringComparison.Ordinal))?.MemberCount ?? 0;
            if (memberCount > 0) blockers.Add($"Role is assigned to {memberCount.ToString(CultureInfo.InvariantCulture)} member(s).");
            AddOverwriteReferences(snapshot, normalizedId, cascade);
            foreach (var rule in autoMod.Where(rule => rule.ExemptRoleIds.Contains(normalizedId, StringComparer.Ordinal)))
            {
                cascade.Add(new CascadeReference("automod_rule", rule.Id, "exempt_role"));
            }

            if (onboarding is not null)
            {
                foreach (var option in onboarding.Prompts.SelectMany(prompt => prompt.Options).Where(option => option.RoleIds.Contains(normalizedId, StringComparer.Ordinal)))
                {
                    cascade.Add(new CascadeReference("onboarding_option", option.Id, "granted_role"));
                }
            }
        }
        else if (normalizedType is "channel" or "category")
        {
            var category = snapshot.Categories.SingleOrDefault(item => string.Equals(item.Id, normalizedId, StringComparison.Ordinal));
            var channel = snapshot.Channels.SingleOrDefault(item => string.Equals(item.Id, normalizedId, StringComparison.Ordinal));
            if (category is null && channel is null) throw NotFound();
            foreach (var child in snapshot.Channels.Where(item => string.Equals(item.CategoryId, normalizedId, StringComparison.Ordinal)))
            {
                cascade.Add(new CascadeReference("channel", child.Id, "category_child_detached"));
            }

            var threads = await extendedReader.ListActiveThreadsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var thread in threads.Where(item => string.Equals(item.ParentChannelId, normalizedId, StringComparison.Ordinal)))
            {
                cascade.Add(new CascadeReference("thread", thread.Id, "contained_thread"));
            }

            foreach (var rule in autoMod.Where(rule => rule.ExemptChannelIds.Contains(normalizedId, StringComparer.Ordinal)))
            {
                cascade.Add(new CascadeReference("automod_rule", rule.Id, "exempt_channel"));
            }

            if (onboarding is not null)
            {
                if (onboarding.DefaultChannelIds.Contains(normalizedId, StringComparer.Ordinal))
                {
                    blockers.Add("Channel is an onboarding default channel.");
                }

                foreach (var option in onboarding.Prompts.SelectMany(prompt => prompt.Options).Where(option => option.ChannelIds.Contains(normalizedId, StringComparer.Ordinal)))
                {
                    cascade.Add(new CascadeReference("onboarding_option", option.Id, "granted_channel"));
                }
            }
        }
        else
        {
            throw InvalidArgument("Deletion analysis currently supports role, channel, or category resources.");
        }

        var orderedCascade = cascade
            .Distinct()
            .OrderBy(item => item.ResourceType, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId, StringComparer.Ordinal)
            .ThenBy(item => item.Relationship, StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedBlockers = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        return new DeletionImpactAnalysis(
            normalizedType,
            normalizedId,
            ComputeCascadeDigest(normalizedType, normalizedId, orderedBlockers, orderedCascade),
            orderedBlockers,
            orderedCascade);
    }

    public async Task<MemberPermissionExplanation> ExplainMemberPermissionsAsync(
        string userId,
        string? channelId,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var member = await memberReader.GetMemberAsync(NormalizeSnowflake(userId), cancellationToken).ConfigureAwait(false);
        return PermissionExplainer.ExplainMember(
            snapshot,
            member,
            channelId is null ? null : NormalizeSnowflake(channelId));
    }

    private async Task<GuildSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            throw DiscordErrorMapper.NotConfigured();
        }

        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var configuredGuildId = settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);
        return string.Equals(snapshot.Id, configuredGuildId, StringComparison.Ordinal)
            ? snapshot
            : throw new StewardException(
                StewardErrorCodes.GuildMismatch,
                "Discord returned a resource outside the configured guild.");
    }

    private static void AddOverwriteReferences(
        GuildSnapshot snapshot,
        string roleId,
        ImmutableArray<CascadeReference>.Builder cascade)
    {
        foreach (var category in snapshot.Categories.Where(item => item.PermissionOverwrites.Any(overwrite => overwrite.TargetType == "role" && overwrite.TargetId == roleId)))
        {
            cascade.Add(new CascadeReference("category", category.Id, "role_overwrite"));
        }

        foreach (var channel in snapshot.Channels.Where(item => item.PermissionOverwrites.Any(overwrite => overwrite.TargetType == "role" && overwrite.TargetId == roleId)))
        {
            cascade.Add(new CascadeReference("channel", channel.Id, "role_overwrite"));
        }
    }

    private static PrivilegeEscalationPath CreatePath(
        string code,
        string severity,
        RoleSnapshot role,
        IEnumerable<string> permissions,
        string explanation) => new(
            code,
            severity,
            role.Id,
            role.Name,
            permissions.Order(StringComparer.Ordinal).ToImmutableArray(),
            explanation);

    private static string ComputeCascadeDigest(
        string resourceType,
        string resourceId,
        ImmutableArray<string> blockers,
        ImmutableArray<CascadeReference> cascade)
    {
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            resourceType,
            resourceId,
            blockers,
            cascade
        });
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    private static string NormalizeSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : throw InvalidArgument("Discord ID must be a positive snowflake string.");

    private static StewardException NotFound() => new(
        StewardErrorCodes.ResourceNotFound,
        "The requested resource was not found in the configured guild.");

    private static StewardException InvalidArgument(string message) => new(
        StewardErrorCodes.InvalidArgument,
        message);
}
