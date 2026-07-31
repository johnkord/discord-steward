using DiscordSteward.Configuration;
using DiscordSteward.Domain;

namespace DiscordSteward.Tests;

public sealed class GuildSnapshotTests
{
    [Fact]
    public void RoleStateDigest_ChangesWithIconAndUnicodeEmoji()
    {
        var plain = RoleSnapshot.Create(
            1,
            2,
            "role",
            1,
            false,
            false,
            ResourceProtection.None,
            ["ViewChannel"],
            null,
            false,
            false);
        var icon = RoleSnapshot.Create(
            1,
            2,
            "role",
            1,
            false,
            false,
            ResourceProtection.None,
            ["ViewChannel"],
            null,
            false,
            false,
            iconHash: "abc",
            unicodeEmoji: null);
        var emoji = RoleSnapshot.Create(
            1,
            2,
            "role",
            1,
            false,
            false,
            ResourceProtection.None,
            ["ViewChannel"],
            null,
            false,
            false,
            iconHash: null,
            unicodeEmoji: ":)");

        Assert.NotEqual(plain.StateDigest, icon.StateDigest);
        Assert.NotEqual(plain.StateDigest, emoji.StateDigest);
        Assert.NotEqual(icon.StateDigest, emoji.StateDigest);
    }

    private const ulong GuildId = 100000000000000001UL;

    [Fact]
    public void Create_NormalizesOrderingAndBoundsUntrustedText()
    {
        var overwrite = PermissionOverwriteSnapshot.Create(
            "ROLE",
            GuildId,
            ["ViewChannel", "ManageChannels", "ViewChannel"],
            ["SendMessages"]);
        var lowRole = CreateRole(2, "low", position: 1, ["ViewChannel"]);
        var highRole = CreateRole(3, "high", position: 10, ["ViewChannel", "Administrator"]);
        var longDescription = new string('x', 1_100) + "\U0001F600";
        var snapshot = GuildSnapshot.Create(
            GuildId,
            "Guild",
            ownerId: 9,
            longDescription,
            DateTimeOffset.UnixEpoch,
            ["COMMUNITY", "ANIMATED_ICON", "COMMUNITY"],
            [lowRole, highRole],
            [
                CategorySnapshot.Create(GuildId, 20, "second", 2, []),
                CategorySnapshot.Create(GuildId, 10, "first", 1, [])
            ],
            [
                ChannelSnapshot.Create(GuildId, 30, "second", "text", 2, 20, null, false, 0, []),
                ChannelSnapshot.Create(GuildId, 25, "first", "text", 1, 10, null, false, 0, [overwrite])
            ]);

        Assert.Equal(["high", "low"], snapshot.Roles.Select(role => role.Name));
        Assert.Equal(["first", "second"], snapshot.Categories.Select(category => category.Name));
        Assert.Equal(["first", "second"], snapshot.Channels.Select(channel => channel.Name));
        Assert.Equal(["ANIMATED_ICON", "COMMUNITY"], snapshot.Features.ToArray());
        Assert.Equal(["ManageChannels", "ViewChannel"], overwrite.AllowedPermissions.ToArray());
        Assert.Equal("role", overwrite.TargetType);
        Assert.Equal(1_024, snapshot.Description?.Length);
    }

    [Fact]
    public void Policy_ProtectsExactIdsAndConfiguredPrefixes()
    {
        var policy = StewardPolicy.Create(
            ["42", "invalid"],
            ["discord-steward"],
            ["Administrator"]);

        var exact = policy.GetProtection("42", "ordinary");
        var prefixed = policy.GetProtection("43", "Discord-Steward Observer");
        var ordinary = policy.GetProtection("44", "ordinary");

        Assert.True(exact.IsProtected);
        Assert.Equal(["resource_id"], exact.Reasons.ToArray());
        Assert.True(prefixed.IsProtected);
        Assert.Equal(["name_prefix:discord-steward"], prefixed.Reasons.ToArray());
        Assert.False(ordinary.IsProtected);
        Assert.Empty(ordinary.Reasons);
        Assert.DoesNotContain("invalid", policy.ProtectedResourceIds);
    }

    [Fact]
    public void DefaultPolicy_ProtectsDiscStewardRoleName()
    {
        var protection = StewardPolicy.CreateDefault().GetProtection("42", "DiscSteward");

        Assert.True(protection.IsProtected);
        Assert.Equal(["name_prefix:discsteward"], protection.Reasons.ToArray());
    }

    [Fact]
    public void ChannelCreate_NormalizesEmptyTopicToNoTopic()
    {
        var channel = ChannelSnapshot.Create(
            GuildId,
            20,
            "general",
            "text",
            0,
            null,
            topic: string.Empty,
            isNsfw: false,
            slowModeSeconds: 0,
            permissionOverwrites: []);

        Assert.Null(channel.Topic);
    }

    private static RoleSnapshot CreateRole(
        ulong id,
        string name,
        int position,
        IEnumerable<string> permissions) => RoleSnapshot.Create(
            GuildId,
            id,
            name,
            position,
            isManaged: false,
            isEveryone: false,
            ResourceProtection.None,
            permissions,
            colorHex: null,
            isHoisted: false,
            isMentionable: false);
}
