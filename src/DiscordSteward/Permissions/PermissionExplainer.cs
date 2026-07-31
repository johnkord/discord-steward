using System.Collections.Immutable;
using DiscordSteward.Discord;
using DiscordSteward.Domain;

namespace DiscordSteward.Permissions;

public static class PermissionExplainer
{
    public static RolePermissionExplanation ExplainRole(
        GuildSnapshot snapshot,
        string roleId,
        string? channelId = null) =>
        ExplainRoleForSubject(snapshot, roleId, channelId, isGuildOwner: false);

    public static MemberPermissionExplanation ExplainMember(
        GuildSnapshot snapshot,
        MemberView member,
        string? channelId = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(member);
        var everyoneRole = snapshot.Roles.SingleOrDefault(role => role.IsEveryone)
            ?? throw new InvalidOperationException("The guild snapshot does not contain an @everyone role.");
        var selectedRoles = snapshot.Roles
            .Where(role => member.RoleIds.Contains(role.Id, StringComparer.Ordinal))
            .OrderBy(role => role.Id, StringComparer.Ordinal)
            .ToArray();
        var missingRoleIds = member.RoleIds
            .Except(selectedRoles.Select(role => role.Id), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var channel = channelId is null ? null : FindChannel(snapshot, channelId);
        var steps = ImmutableArray.CreateBuilder<PermissionTraceStep>();
        var limitations = ImmutableArray.CreateBuilder<string>();
        var permissions = CreateSet(everyoneRole.Permissions);
        steps.Add(CreateStep(
            "guild_everyone",
            [everyoneRole.Id],
            everyoneRole.Permissions,
            [],
            permissions,
            "Start with guild permissions granted by the @everyone role."));

        var rolePermissions = Snapshot(selectedRoles.SelectMany(role => role.Permissions));
        permissions.UnionWith(rolePermissions);
        steps.Add(CreateStep(
            "guild_roles",
            selectedRoles.Select(role => role.Id),
            rolePermissions,
            [],
            permissions,
            "Union permissions from every exact role assigned to the member."));

        var rawBasePermissions = Snapshot(permissions);
        var ownerBypass = string.Equals(member.UserId, snapshot.OwnerId, StringComparison.Ordinal);
        if (ownerBypass)
        {
            permissions.UnionWith(PermissionCatalog.All);
            steps.Add(CreateStep(
                "guild_owner_bypass",
                [member.UserId],
                PermissionCatalog.All,
                [],
                permissions,
                "The guild owner receives all known permissions before overwrites."));
        }

        var administratorBypass = !ownerBypass && permissions.Contains("Administrator");
        if (administratorBypass)
        {
            permissions.UnionWith(PermissionCatalog.All);
            steps.Add(CreateStep(
                "administrator_bypass",
                selectedRoles.Where(role => role.Permissions.Contains("Administrator", StringComparer.Ordinal)).Select(role => role.Id),
                PermissionCatalog.All,
                [],
                permissions,
                "Administrator grants all known permissions and bypasses channel overwrites."));
        }

        var expandedBasePermissions = Snapshot(permissions);
        if (channel is not null && !ownerBypass && !administratorBypass)
        {
            ApplyEveryoneOverwrite(snapshot.Id, channel, permissions, steps);
            ApplyRoleOverwrites(selectedRoles.Select(role => role.Id).ToArray(), channel, permissions, steps);
            ApplyMemberOverwrite(member.UserId, channel, permissions, steps);
        }
        else if (channel is not null)
        {
            steps.Add(CreateStep(
                "channel_overwrites_bypassed",
                [channel.Id],
                [],
                [],
                permissions,
                ownerBypass
                    ? "Guild-owner permissions bypass all channel overwrites."
                    : "Administrator permissions bypass all channel overwrites."));
        }

        var permissionsAfterOverwrites = Snapshot(permissions);
        var effectivePermissions = CreateSet(permissions);
        var implicitEffects = channel is null
            ? ImmutableArray<ImplicitPermissionEffect>.Empty
            : ApplyImplicitRules(channel, effectivePermissions);
        if (!missingRoleIds.IsEmpty)
        {
            limitations.Add("One or more member role IDs were absent from the configuration snapshot.");
        }

        return new MemberPermissionExplanation(
            new PermissionMemberReference(
                member.UserId,
                member.DisplayName,
                member.RoleIds,
                member.TimedOutUntil),
            channel is null
                ? null
                : new PermissionChannelReference(channel.Id, channel.Name, channel.Kind, channel.ParentChannelId),
            ownerBypass,
            administratorBypass,
            rawBasePermissions,
            expandedBasePermissions,
            steps.ToImmutable(),
            permissionsAfterOverwrites,
            implicitEffects,
            Snapshot(effectivePermissions),
            missingRoleIds,
            limitations.ToImmutable());
    }

    internal static RolePermissionExplanation ExplainRoleForSubject(
        GuildSnapshot snapshot,
        string roleId,
        string? channelId,
        bool isGuildOwner)
    {
        var everyoneRole = snapshot.Roles.SingleOrDefault(role => role.IsEveryone)
            ?? throw new InvalidOperationException("The guild snapshot does not contain an @everyone role.");
        var role = snapshot.Roles.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, roleId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The role is not present in the guild snapshot.", nameof(roleId));
        var channel = channelId is null ? null : FindChannel(snapshot, channelId);
        var steps = ImmutableArray.CreateBuilder<PermissionTraceStep>();
        var limitations = ImmutableArray.CreateBuilder<string>();

        var permissions = CreateSet(everyoneRole.Permissions);
        steps.Add(CreateStep(
            "guild_everyone",
            [everyoneRole.Id],
            everyoneRole.Permissions,
            [],
            permissions,
            "Start with guild permissions granted by the @everyone role."));

        var selectedRoleIds = role.IsEveryone ? Array.Empty<string>() : [role.Id];
        var selectedRolePermissions = role.IsEveryone ? [] : role.Permissions;
        permissions.UnionWith(selectedRolePermissions);
        steps.Add(CreateStep(
            "guild_roles",
            selectedRoleIds,
            selectedRolePermissions,
            [],
            permissions,
            "Union permissions from the selected role; role position does not affect permission collisions."));

        var rawBasePermissions = Snapshot(permissions);
        if (isGuildOwner)
        {
            permissions.UnionWith(PermissionCatalog.All);
            steps.Add(CreateStep(
                "guild_owner_bypass",
                [],
                PermissionCatalog.All,
                [],
                permissions,
                "Guild owners receive all known permissions before channel overwrites."));
        }

        var administratorBypass = !isGuildOwner && permissions.Contains("Administrator");
        if (administratorBypass)
        {
            permissions.UnionWith(PermissionCatalog.All);
            steps.Add(CreateStep(
                "administrator_bypass",
                [role.Id],
                PermissionCatalog.All,
                [],
                permissions,
                "Administrator grants all known permissions and bypasses channel overwrites."));
        }

        var expandedBasePermissions = Snapshot(permissions);
        var excludedMemberOverwriteCount = 0;
        if (channel is not null && !isGuildOwner && !administratorBypass)
        {
            if (channel.ParentChannelId is not null)
            {
                steps.Add(CreateStep(
                    "thread_parent_inheritance",
                    [channel.ParentChannelId],
                    [],
                    [],
                    permissions,
                    "Threads inherit permission overwrites from their parent channel."));
            }

            ApplyEveryoneOverwrite(snapshot.Id, channel, permissions, steps);
            ApplyRoleOverwrites(selectedRoleIds, channel, permissions, steps);
            excludedMemberOverwriteCount = channel.PermissionOverwrites.Count(overwrite =>
                string.Equals(overwrite.TargetType, "member", StringComparison.Ordinal));
            if (excludedMemberOverwriteCount > 0)
            {
                limitations.Add("Member-specific overwrites are intentionally excluded from role-only analysis.");
            }
        }
        else if (channel is not null)
        {
            steps.Add(CreateStep(
                "channel_overwrites_bypassed",
                [channel.Id],
                [],
                [],
                permissions,
                isGuildOwner
                    ? "Guild-owner permissions bypass all channel overwrites."
                    : "Administrator permissions bypass all channel overwrites."));
        }

        var permissionsAfterOverwrites = Snapshot(permissions);
        var effectivePermissions = CreateSet(permissions);
        var implicitEffects = channel is null
            ? ImmutableArray<ImplicitPermissionEffect>.Empty
            : ApplyImplicitRules(channel, effectivePermissions);
        var effectivePermissionsComplete = channel is null ||
            !string.Equals(channel.Kind, "unknown", StringComparison.Ordinal);
        if (!effectivePermissionsComplete)
        {
            limitations.Add("Implicit permission dependencies are not asserted for an unknown channel kind.");
        }

        limitations.Add("This is role-only analysis; guild ownership and member-specific permissions are not inferred from a role ID.");
        if (isGuildOwner || administratorBypass)
        {
            limitations.Add("Owner and Administrator expansion uses the permission names pinned to Discord.Net 3.20.1.");
        }

        return new RolePermissionExplanation(
            AnalysisMode: "role_only",
            new PermissionRoleReference(role.Id, role.Name, role.Position, role.IsManaged, role.IsEveryone),
            channel is null
                ? null
                : new PermissionChannelReference(channel.Id, channel.Name, channel.Kind, channel.ParentChannelId),
            GuildOwnerBypass: isGuildOwner,
            AdministratorBypass: administratorBypass,
            RawBasePermissions: rawBasePermissions,
            ExpandedBasePermissions: expandedBasePermissions,
            Steps: steps.ToImmutable(),
            PermissionsAfterOverwrites: permissionsAfterOverwrites,
            ImplicitEffects: implicitEffects,
            EffectivePermissionsComplete: effectivePermissionsComplete,
            EffectivePermissions: Snapshot(effectivePermissions),
            ExcludedMemberOverwriteCount: excludedMemberOverwriteCount,
            Hierarchy: RoleHierarchyAssessment.Unknown,
            Limitations: limitations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray());
    }

    internal static RoleHierarchyAssessment AssessRoleHierarchy(
        RoleSnapshot actorHighestRole,
        IEnumerable<string> actorPermissions,
        RoleSnapshot targetRole)
    {
        if (!actorPermissions.Contains("ManageRoles", StringComparer.Ordinal))
        {
            return new RoleHierarchyAssessment(
                "denied_missing_manage_roles",
                false,
                "The actor lacks ManageRoles.");
        }

        if (targetRole.IsEveryone)
        {
            return new RoleHierarchyAssessment(
                "denied_everyone_role",
                false,
                "Discord does not allow managing the @everyone role as a normal role.");
        }

        if (targetRole.IsManaged)
        {
            return new RoleHierarchyAssessment(
                "denied_managed_role",
                false,
                "Discord or an integration manages the target role.");
        }

        if (actorHighestRole.Position <= targetRole.Position)
        {
            return new RoleHierarchyAssessment(
                "denied_role_hierarchy",
                false,
                "The actor's highest role must be above the target role.");
        }

        return new RoleHierarchyAssessment(
            "eligible_by_discord_hierarchy",
            true,
            "The actor has ManageRoles and its highest role is above the unmanaged target role.");
    }

    private static void ApplyEveryoneOverwrite(
        string guildId,
        PermissionChannelContext channel,
        SortedSet<string> permissions,
        ImmutableArray<PermissionTraceStep>.Builder steps)
    {
        var overwrite = channel.PermissionOverwrites.SingleOrDefault(candidate =>
            string.Equals(candidate.TargetType, "role", StringComparison.Ordinal) &&
            string.Equals(candidate.TargetId, guildId, StringComparison.Ordinal));
        var allowed = overwrite?.AllowedPermissions ?? [];
        var denied = overwrite?.DeniedPermissions ?? [];
        Apply(permissions, allowed, denied);
        steps.Add(CreateStep(
            "channel_everyone_overwrite",
            overwrite is null ? [] : [overwrite.TargetId],
            allowed,
            denied,
            permissions,
            "Apply @everyone channel denies, then allows."));
    }

    private static void ApplyRoleOverwrites(
        IReadOnlyCollection<string> selectedRoleIds,
        PermissionChannelContext channel,
        SortedSet<string> permissions,
        ImmutableArray<PermissionTraceStep>.Builder steps)
    {
        var overwrites = channel.PermissionOverwrites
            .Where(overwrite =>
                string.Equals(overwrite.TargetType, "role", StringComparison.Ordinal) &&
                selectedRoleIds.Contains(overwrite.TargetId, StringComparer.Ordinal))
            .ToArray();
        var allowed = Snapshot(overwrites.SelectMany(overwrite => overwrite.AllowedPermissions));
        var denied = Snapshot(overwrites.SelectMany(overwrite => overwrite.DeniedPermissions));
        Apply(permissions, allowed, denied);
        steps.Add(CreateStep(
            "channel_role_overwrites",
            overwrites.Select(overwrite => overwrite.TargetId),
            allowed,
            denied,
            permissions,
            "Combine all selected-role denies, combine all selected-role allows, then apply denies before allows."));
    }

    private static void ApplyMemberOverwrite(
        string memberId,
        PermissionChannelContext channel,
        SortedSet<string> permissions,
        ImmutableArray<PermissionTraceStep>.Builder steps)
    {
        var overwrite = channel.PermissionOverwrites.SingleOrDefault(candidate =>
            string.Equals(candidate.TargetType, "member", StringComparison.Ordinal) &&
            string.Equals(candidate.TargetId, memberId, StringComparison.Ordinal));
        var allowed = overwrite?.AllowedPermissions ?? [];
        var denied = overwrite?.DeniedPermissions ?? [];
        Apply(permissions, allowed, denied);
        steps.Add(CreateStep(
            "channel_member_overwrite",
            overwrite is null ? [] : [overwrite.TargetId],
            allowed,
            denied,
            permissions,
            "Apply the exact member overwrite after all role overwrites."));
    }

    private static ImmutableArray<ImplicitPermissionEffect> ApplyImplicitRules(
        PermissionChannelContext channel,
        SortedSet<string> effectivePermissions)
    {
        var effects = ImmutableArray.CreateBuilder<ImplicitPermissionEffect>();
        if (!effectivePermissions.Contains("ViewChannel"))
        {
            AddImplicitEffect(
                effects,
                effectivePermissions,
                "ViewChannel",
                "view_channel_required",
                PermissionCatalog.ChannelScoped,
                "Without ViewChannel, all other channel-scoped permissions are unusable.");
            return effects.ToImmutable();
        }

        if (string.Equals(channel.Kind, "thread", StringComparison.Ordinal))
        {
            var threadIgnored = PermissionCatalog.SendMessageDependent.Add("SendMessages");
            if (!effectivePermissions.Contains("SendMessagesInThreads"))
            {
                AddImplicitEffect(
                    effects,
                    effectivePermissions,
                    "SendMessagesInThreads",
                    "send_messages_in_threads_required",
                    threadIgnored,
                    "Threads require SendMessagesInThreads; SendMessages is not inherited from the parent channel.");
            }
            else if (effectivePermissions.Remove("SendMessages"))
            {
                effects.Add(new ImplicitPermissionEffect(
                    "thread_send_messages_not_inherited",
                    "SendMessagesInThreads",
                    ["SendMessages"],
                    "SendMessages does not apply in threads; SendMessagesInThreads controls thread sending."));
            }
        }
        else if (PermissionCatalog.IsTextLike(channel.Kind) && !effectivePermissions.Contains("SendMessages"))
        {
            AddImplicitEffect(
                effects,
                effectivePermissions,
                "SendMessages",
                "send_messages_required",
                PermissionCatalog.SendMessageDependent,
                "Without SendMessages, Discord ignores TTS, mentions, embeds, and attachments.");
        }

        if (PermissionCatalog.IsVoiceLike(channel.Kind) && !effectivePermissions.Contains("Connect"))
        {
            AddImplicitEffect(
                effects,
                effectivePermissions,
                "Connect",
                "connect_required",
                PermissionCatalog.ConnectDependent,
                "Without Connect, voice or stage interaction permissions are unusable.");
        }

        return effects.ToImmutable();
    }

    private static void AddImplicitEffect(
        ImmutableArray<ImplicitPermissionEffect>.Builder effects,
        SortedSet<string> effectivePermissions,
        string triggerPermission,
        string code,
        IEnumerable<string> dependentPermissions,
        string explanation)
    {
        var ignored = effectivePermissions
            .Intersect(dependentPermissions, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        effectivePermissions.ExceptWith(ignored);
        effects.Add(new ImplicitPermissionEffect(code, triggerPermission, ignored, explanation));
    }

    private static PermissionChannelContext FindChannel(GuildSnapshot snapshot, string channelId)
    {
        var category = snapshot.Categories.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, channelId, StringComparison.Ordinal));
        if (category is not null)
        {
            return new PermissionChannelContext(
                category.Id,
                category.Name,
                "category",
                ParentChannelId: null,
                category.PermissionOverwrites);
        }

        var channel = snapshot.Channels.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, channelId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The channel is not present in the guild snapshot.", nameof(channelId));
        var overwriteSource = channel;
        if (string.Equals(channel.Kind, "thread", StringComparison.Ordinal))
        {
            if (channel.ParentChannelId is null)
            {
                throw new InvalidOperationException("The thread snapshot does not identify its parent channel.");
            }

            overwriteSource = snapshot.Channels.SingleOrDefault(candidate =>
                string.Equals(candidate.Id, channel.ParentChannelId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The thread parent is not present in the guild snapshot.");
        }

        return new PermissionChannelContext(
            channel.Id,
            channel.Name,
            channel.Kind,
            channel.ParentChannelId,
            overwriteSource.PermissionOverwrites);
    }

    private static void Apply(
        SortedSet<string> permissions,
        IEnumerable<string> allowed,
        IEnumerable<string> denied)
    {
        permissions.ExceptWith(denied);
        permissions.UnionWith(allowed);
    }

    private static PermissionTraceStep CreateStep(
        string code,
        IEnumerable<string> sourceResourceIds,
        IEnumerable<string> allowedPermissions,
        IEnumerable<string> deniedPermissions,
        IEnumerable<string> resultingPermissions,
        string explanation) => new(
            code,
            Snapshot(sourceResourceIds),
            Snapshot(allowedPermissions),
            Snapshot(deniedPermissions),
            Snapshot(resultingPermissions),
            explanation);

    private static SortedSet<string> CreateSet(IEnumerable<string> permissions) =>
        new(permissions, StringComparer.Ordinal);

    private static ImmutableArray<string> Snapshot(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();

    private sealed record PermissionChannelContext(
        string Id,
        string Name,
        string Kind,
        string? ParentChannelId,
        ImmutableArray<PermissionOverwriteSnapshot> PermissionOverwrites);
}

public sealed record RolePermissionExplanation(
    string AnalysisMode,
    PermissionRoleReference Role,
    PermissionChannelReference? Channel,
    bool GuildOwnerBypass,
    bool AdministratorBypass,
    ImmutableArray<string> RawBasePermissions,
    ImmutableArray<string> ExpandedBasePermissions,
    ImmutableArray<PermissionTraceStep> Steps,
    ImmutableArray<string> PermissionsAfterOverwrites,
    ImmutableArray<ImplicitPermissionEffect> ImplicitEffects,
    bool EffectivePermissionsComplete,
    ImmutableArray<string> EffectivePermissions,
    int ExcludedMemberOverwriteCount,
    RoleHierarchyAssessment Hierarchy,
    ImmutableArray<string> Limitations);

public sealed record MemberPermissionExplanation(
    PermissionMemberReference Member,
    PermissionChannelReference? Channel,
    bool GuildOwnerBypass,
    bool AdministratorBypass,
    ImmutableArray<string> RawBasePermissions,
    ImmutableArray<string> ExpandedBasePermissions,
    ImmutableArray<PermissionTraceStep> Steps,
    ImmutableArray<string> PermissionsAfterOverwrites,
    ImmutableArray<ImplicitPermissionEffect> ImplicitEffects,
    ImmutableArray<string> EffectivePermissions,
    ImmutableArray<string> MissingRoleIds,
    ImmutableArray<string> Limitations);

public sealed record PermissionMemberReference(
    string UserId,
    string DisplayName,
    ImmutableArray<string> RoleIds,
    DateTimeOffset? TimedOutUntil);

public sealed record PermissionRoleReference(
    string Id,
    string Name,
    int Position,
    bool IsManaged,
    bool IsEveryone);

public sealed record PermissionChannelReference(
    string Id,
    string Name,
    string Kind,
    string? ParentChannelId);

public sealed record PermissionTraceStep(
    string Code,
    ImmutableArray<string> SourceResourceIds,
    ImmutableArray<string> AllowedPermissions,
    ImmutableArray<string> DeniedPermissions,
    ImmutableArray<string> ResultingPermissions,
    string Explanation);

public sealed record ImplicitPermissionEffect(
    string Code,
    string MissingPermission,
    ImmutableArray<string> IgnoredPermissions,
    string Explanation);

public sealed record RoleHierarchyAssessment(string Outcome, bool? CanManage, string Explanation)
{
    public static RoleHierarchyAssessment Unknown { get; } = new(
        "unknown_observer_hierarchy",
        CanManage: null,
        "The snapshot does not identify the observer bot's highest role, so manageability is not asserted.");
}
