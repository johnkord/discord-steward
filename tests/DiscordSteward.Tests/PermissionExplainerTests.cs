using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Permissions;

namespace DiscordSteward.Tests;

public sealed class PermissionExplainerTests
{
    private const ulong GuildId = 100000000000000001UL;

    [Fact]
    public void ExplainRole_AppliesEveryoneThenCombinedRoleOverwrite()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true,
            ["AttachFiles", "SendMessages", "ViewChannel"]);
        var role = CreateRole(10, "role", 1, isEveryone: false, ["MentionEveryone"]);
        var channel = ChannelSnapshot.Create(
            GuildId,
            20,
            "channel",
            "text",
            0,
            null,
            null,
            false,
            0,
            [
                PermissionOverwriteSnapshot.Create("role", GuildId, ["ReadMessageHistory"], ["SendMessages"]),
                PermissionOverwriteSnapshot.Create("role", 10, ["SendMessages"], ["AttachFiles"])
            ]);

        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone, role], [channel]),
            "10",
            "20");

        Assert.Equal(
            ["MentionEveryone", "ReadMessageHistory", "SendMessages", "ViewChannel"],
            explanation.PermissionsAfterOverwrites.ToArray());
        Assert.Empty(explanation.ImplicitEffects);
        Assert.Equal(
            explanation.PermissionsAfterOverwrites.ToArray(),
            explanation.EffectivePermissions.ToArray());
        Assert.Equal(
            ["guild_everyone", "guild_roles", "channel_everyone_overwrite", "channel_role_overwrites"],
            explanation.Steps.Select(step => step.Code));
    }

    [Fact]
    public void ExplainRole_MissingSendMessages_ReportsAndFiltersImplicitPermissions()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true,
            ["AttachFiles", "EmbedLinks", "MentionEveryone", "SendTTSMessages", "ViewChannel"]);
        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone], [CreateChannel(20, "text", [])]),
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "20");

        var effect = Assert.Single(explanation.ImplicitEffects);
        Assert.Equal("send_messages_required", effect.Code);
        Assert.Equal(
            ["AttachFiles", "EmbedLinks", "MentionEveryone", "SendTTSMessages"],
            effect.IgnoredPermissions.ToArray());
        Assert.Equal(["ViewChannel"], explanation.EffectivePermissions.ToArray());
    }

    [Fact]
    public void ExplainRole_MissingViewChannel_FiltersChannelButPreservesGuildPermissions()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true,
            ["KickMembers", "SendMessages"]);
        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone], [CreateChannel(20, "text", [])]),
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "20");

        var effect = Assert.Single(explanation.ImplicitEffects);
        Assert.Equal("view_channel_required", effect.Code);
        Assert.Equal(["SendMessages"], effect.IgnoredPermissions.ToArray());
        Assert.Equal(["KickMembers"], explanation.EffectivePermissions.ToArray());
    }

    [Fact]
    public void ExplainRole_AdministratorBypassesChannelDenies()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true, ["ViewChannel"]);
        var administrator = CreateRole(10, "admin", 1, isEveryone: false, ["Administrator"]);
        var deny = PermissionOverwriteSnapshot.Create(
            "role",
            GuildId,
            [],
            ["ViewChannel", "SendMessages"]);

        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone, administrator], [CreateChannel(20, "text", [deny])]),
            "10",
            "20");

        Assert.True(explanation.AdministratorBypass);
        Assert.Contains("ManageGuild", explanation.EffectivePermissions);
        Assert.Contains("ViewChannel", explanation.EffectivePermissions);
        Assert.Contains(explanation.Steps, step => step.Code == "channel_overwrites_bypassed");
    }

    [Fact]
    public void ExplainRole_GuildOwnerReceivesAllPermissions()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true, []);

        var explanation = PermissionExplainer.ExplainRoleForSubject(
            CreateSnapshot([everyone], []),
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            channelId: null,
            isGuildOwner: true);

        Assert.True(explanation.GuildOwnerBypass);
        Assert.Contains("Administrator", explanation.EffectivePermissions);
        Assert.Contains("ManageGuild", explanation.EffectivePermissions);
        Assert.Contains(explanation.Steps, step => step.Code == "guild_owner_bypass");
    }

    [Fact]
    public void ExplainRole_MissingConnect_FiltersVoiceInteractionPermissions()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true,
            ["ManageChannels", "Speak", "UseVAD", "ViewChannel"]);

        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone], [CreateChannel(20, "voice", [])]),
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "20");

        var effect = Assert.Single(explanation.ImplicitEffects);
        Assert.Equal("connect_required", effect.Code);
        Assert.Equal(["ManageChannels", "Speak", "UseVAD"], effect.IgnoredPermissions.ToArray());
        Assert.Equal(["ViewChannel"], explanation.EffectivePermissions.ToArray());
    }

    [Fact]
    public void ExplainRole_ExcludesMemberOverwriteAndReportsCount()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true, ["ViewChannel"]);
        var memberOverwrite = PermissionOverwriteSnapshot.Create("member", 99, ["SendMessages"], []);

        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone], [CreateChannel(20, "text", [memberOverwrite])]),
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "20");

        Assert.Equal(1, explanation.ExcludedMemberOverwriteCount);
        Assert.DoesNotContain("SendMessages", explanation.EffectivePermissions);
        Assert.Contains(explanation.Limitations, limitation => limitation.Contains("Member-specific", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplainMember_AppliesAllRolesThenExactMemberOverwrite()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true, ["ViewChannel"]);
        var role = CreateRole(10, "member-role", 1, isEveryone: false, ["AttachFiles"]);
        var channel = CreateChannel(
            20,
            "text",
            [
                PermissionOverwriteSnapshot.Create("role", 10, ["SendMessages"], ["AttachFiles"]),
                PermissionOverwriteSnapshot.Create("member", 99, ["AttachFiles"], ["SendMessages"])
            ]);
        var member = new MemberView(
            "99",
            "member",
            "member",
            Nickname: null,
            RoleIds: ["10"],
            JoinedAt: null,
            TimedOutUntil: null,
            Flags: "None",
            FlagsValue: 0,
            Hierarchy: 1,
            IsPending: false,
            new MemberVoiceStateView("99", null, false, false, false, false, false, false, false, null));

        var explanation = PermissionExplainer.ExplainMember(
            CreateSnapshot([everyone, role], [channel]),
            member,
            "20");

        Assert.Contains("AttachFiles", explanation.PermissionsAfterOverwrites);
        Assert.DoesNotContain("AttachFiles", explanation.EffectivePermissions);
        Assert.DoesNotContain("SendMessages", explanation.EffectivePermissions);
        Assert.Contains(explanation.Steps, step => step.Code == "channel_member_overwrite");
        Assert.Contains(explanation.ImplicitEffects, effect => effect.Code == "send_messages_required");
    }

    [Fact]
    public void ExplainRole_ThreadInheritsParentOverwritesAndRequiresThreadSendPermission()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true,
            ["AttachFiles", "SendMessages", "ViewChannel"]);
        var role = CreateRole(10, "role", 1, isEveryone: false, []);
        var parentOverwrite = PermissionOverwriteSnapshot.Create(
            "role",
            10,
            ["SendMessagesInThreads"],
            []);
        var parent = CreateChannel(30, "text", [parentOverwrite]);
        var thread = ChannelSnapshot.Create(
            GuildId,
            20,
            "thread",
            "thread",
            0,
            null,
            null,
            false,
            0,
            [],
            parentChannelId: 30);

        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone, role], [thread, parent]),
            "10",
            "20");

        Assert.Equal("30", explanation.Channel?.ParentChannelId);
        Assert.Contains(explanation.Steps, step => step.Code == "thread_parent_inheritance");
        Assert.Contains("AttachFiles", explanation.EffectivePermissions);
        Assert.Contains("SendMessagesInThreads", explanation.EffectivePermissions);
        Assert.DoesNotContain("SendMessages", explanation.EffectivePermissions);
        Assert.Contains(explanation.ImplicitEffects, effect => effect.Code == "thread_send_messages_not_inherited");
    }

    [Fact]
    public void ExplainRole_ThreadWithoutParent_FailsClosed()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true, ["ViewChannel"]);
        var thread = ChannelSnapshot.Create(
            GuildId,
            20,
            "thread",
            "thread",
            0,
            null,
            null,
            false,
            0,
            []);

        Assert.Throws<InvalidOperationException>(() => PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone], [thread]),
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "20"));
    }

    [Fact]
    public void ExplainRole_UnknownChannelKind_MarksEffectivePermissionsIncomplete()
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true,
            ["SendMessages", "ViewChannel"]);

        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone], [CreateChannel(20, "unknown", [])]),
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "20");

        Assert.False(explanation.EffectivePermissionsComplete);
        Assert.Contains(explanation.Limitations, limitation => limitation.Contains("unknown channel kind", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("announcement")]
    [InlineData("forum")]
    [InlineData("media")]
    public void ExplainRole_TextLikeKindsApplySendMessageDependencies(string kind)
    {
        var everyone = CreateRole(GuildId, "@everyone", 0, isEveryone: true,
            ["AttachFiles", "ViewChannel"]);

        var explanation = PermissionExplainer.ExplainRole(
            CreateSnapshot([everyone], [CreateChannel(20, kind, [])]),
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "20");

        Assert.Contains(explanation.ImplicitEffects, effect => effect.Code == "send_messages_required");
        Assert.DoesNotContain("AttachFiles", explanation.EffectivePermissions);
    }

    [Fact]
    public void PermissionCatalog_MatchesPinnedDiscordNetGuildPermissions()
    {
        var discordNetNames = Enum.GetNames<global::Discord.GuildPermission>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(discordNetNames, PermissionCatalog.All.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(false, 2, 1, false, "denied_missing_manage_roles")]
    [InlineData(true, 1, 2, false, "denied_role_hierarchy")]
    [InlineData(true, 2, 1, true, "eligible_by_discord_hierarchy")]
    public void AssessRoleHierarchy_EnforcesPermissionAndPosition(
        bool hasManageRoles,
        int actorPosition,
        int targetPosition,
        bool expectedCanManage,
        string expectedOutcome)
    {
        var actor = CreateRole(50, "actor", actorPosition, isEveryone: false, []);
        var target = CreateRole(51, "target", targetPosition, isEveryone: false, []);

        var assessment = PermissionExplainer.AssessRoleHierarchy(
            actor,
            hasManageRoles ? ["ManageRoles"] : [],
            target);

        Assert.Equal(expectedCanManage, assessment.CanManage);
        Assert.Equal(expectedOutcome, assessment.Outcome);
    }

    private static GuildSnapshot CreateSnapshot(
        IEnumerable<RoleSnapshot> roles,
        IEnumerable<ChannelSnapshot> channels) => GuildSnapshot.Create(
            GuildId,
            "Guild",
            ownerId: 1,
            description: null,
            DateTimeOffset.UnixEpoch,
            [],
            roles,
            [],
            channels);

    private static ChannelSnapshot CreateChannel(
        ulong id,
        string kind,
        IEnumerable<PermissionOverwriteSnapshot> overwrites) => ChannelSnapshot.Create(
            GuildId,
            id,
            "channel",
            kind,
            0,
            null,
            null,
            false,
            0,
            overwrites);

    private static RoleSnapshot CreateRole(
        ulong id,
        string name,
        int position,
        bool isEveryone,
        IEnumerable<string> permissions) => RoleSnapshot.Create(
            GuildId,
            id,
            name,
            position,
            isManaged: false,
            isEveryone,
            ResourceProtection.None,
            permissions,
            colorHex: null,
            isHoisted: false,
            isMentionable: false);
}
