using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Domain;
using DiscordSteward.Linting;

namespace DiscordSteward.Tests;

public sealed class GuildLinterTests
{
    private const ulong GuildId = 667956000757776386UL;

    [Fact]
    public void Lint_CoversRequiredRiskCategoriesWithStableEvidence()
    {
        var policy = StewardPolicy.Create(
            [],
            ["discord-steward"],
            ["Administrator", "ManageWebhooks"]);
        var everyoneOverwrite = PermissionOverwriteSnapshot.Create(
            "role",
            GuildId,
            ["ManageChannels"],
            ["SendMessages"]);
        var memberOverwrite = PermissionOverwriteSnapshot.Create(
            "member",
            999,
            ["ViewChannel"],
            []);
        var protectedRole = CreateRole(
            10,
            "discord-steward observer",
            position: 5,
            isManaged: true,
            policy.GetProtection("10", "discord-steward observer"),
            ["Administrator", "ManageWebhooks"]);
        var snapshot = GuildSnapshot.Create(
            GuildId,
            "Guild",
            ownerId: 1,
            description: null,
            DateTimeOffset.UnixEpoch,
            [],
            [
                CreateRole(11, "Duplicate", 2, false, ResourceProtection.None, []),
                protectedRole,
                CreateRole(12, "duplicate", 1, false, ResourceProtection.None, [])
            ],
            [CategorySnapshot.Create(GuildId, 20, "General", 0, [everyoneOverwrite])],
            [
                ChannelSnapshot.Create(
                    GuildId,
                    21,
                    "general",
                    "text",
                    0,
                    20,
                    null,
                    false,
                    0,
                    [memberOverwrite]),
                ChannelSnapshot.Create(
                    GuildId,
                    22,
                    "public",
                    "text",
                    1,
                    null,
                    null,
                    false,
                    0,
                    [everyoneOverwrite])
            ]);

        var report = new GuildLinter(policy).Lint(snapshot);
        var codes = report.Findings.Select(finding => finding.Code).ToArray();

        Assert.Contains("role_administrator", codes);
        Assert.Contains("managed_role_elevated_permissions", codes);
        Assert.Contains("protected_role_elevated_permissions", codes);
        Assert.Contains("protected_resource_name_only", codes);
        Assert.Contains("duplicate_role_name", codes);
        Assert.Contains("duplicate_channel_name", codes);
        Assert.Contains("member_specific_overwrite", codes);
        Assert.Contains("category_overwrite_drift", codes);
        Assert.Contains("public_sensitive_permission", codes);
        Assert.Equal(report.Findings.Length, report.FindingCount);
        Assert.All(report.Findings, finding => Assert.False(string.IsNullOrWhiteSpace(finding.ResourceId)));
    }

    [Fact]
    public void Lint_IsDeterministicAndDoesNotReportSyncedCategoryOverwrites()
    {
        var policy = StewardPolicy.CreateDefault();
        var overwrite = PermissionOverwriteSnapshot.Create(
            "role",
            55,
            ["ViewChannel"],
            ["SendMessages"]);
        var first = CreateSyncedSnapshot(
            [
                CreateRole(3, "third", 3, false, ResourceProtection.None, []),
                CreateRole(2, "second", 2, false, ResourceProtection.None, [])
            ],
            [overwrite]);
        var second = CreateSyncedSnapshot(first.Roles.Reverse(), [overwrite]);

        var firstReport = new GuildLinter(policy).Lint(first);
        var secondReport = new GuildLinter(policy).Lint(second);

        Assert.DoesNotContain(firstReport.Findings, finding => finding.Code == "category_overwrite_drift");
        Assert.Equal(JsonSerializer.Serialize(firstReport), JsonSerializer.Serialize(secondReport));
    }

    [Fact]
    public void Lint_EveryoneRoleWithDeniedPermission_ReportsExactEvidence()
    {
        var policy = StewardPolicy.Create([], [], ["Administrator", "MentionEveryone"]);
        var snapshot = CreateSyncedSnapshot(
            [CreateRole(GuildId, "@everyone", 0, false, ResourceProtection.None, ["MentionEveryone", "ViewChannel"])],
            []);

        var report = new GuildLinter(policy).Lint(snapshot);
        var finding = Assert.Single(report.Findings, candidate =>
            candidate.Code == "everyone_role_elevated_permissions");

        Assert.Equal(GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture), finding.ResourceId);
        Assert.Equal(["MentionEveryone"], finding.Evidence.ToArray());
        Assert.Equal("warning", finding.Severity);
    }

    private static GuildSnapshot CreateSyncedSnapshot(
        IEnumerable<RoleSnapshot> roles,
        IEnumerable<PermissionOverwriteSnapshot> overwrites) => GuildSnapshot.Create(
            GuildId,
            "Guild",
            ownerId: 1,
            description: null,
            DateTimeOffset.UnixEpoch,
            [],
            roles,
            [CategorySnapshot.Create(GuildId, 20, "Category", 0, overwrites)],
            [ChannelSnapshot.Create(GuildId, 21, "Channel", "text", 0, 20, null, false, 0, overwrites)]);

    private static RoleSnapshot CreateRole(
        ulong id,
        string name,
        int position,
        bool isManaged,
        ResourceProtection protection,
        IEnumerable<string> permissions) => RoleSnapshot.Create(
            GuildId,
            id,
            name,
            position,
            isManaged,
            isEveryone: id == GuildId,
            protection,
            permissions,
            colorHex: null,
            isHoisted: false,
            isMentionable: false);
}
