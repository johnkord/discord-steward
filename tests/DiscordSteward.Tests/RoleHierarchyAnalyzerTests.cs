using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Permissions;

namespace DiscordSteward.Tests;

public sealed class RoleHierarchyAnalyzerTests
{
    private const ulong GuildId = 100000000000000001UL;

    [Fact]
    public async Task AnalyzeAsync_ExplainsManageableAndBlockedRolesWithoutMembers()
    {
        var snapshot = GuildSnapshot.Create(
            GuildId,
            "Guild",
            ownerId: 1,
            description: null,
            DateTimeOffset.UnixEpoch,
            [],
            [
                CreateRole(30, "Above", 4),
                CreateRole(20, "Bot", 3, protectedRole: true),
                CreateRole(10, "Managed", 2, managed: true),
                CreateRole(5, "Assignable", 1),
                CreateRole(GuildId, "@everyone", 0, everyone: true)
            ],
            [],
            []);
        var inspector = new FakeInspector(new BotEffectivePermissions(
            GuildId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "42",
            ["ManageRoles", "ViewChannel"],
            "20",
            "Bot",
            3,
            HasAdministrator: false));
        var analyzer = new RoleHierarchyAnalyzer(
            new InMemoryDiscordGuildReader(snapshot),
            inspector,
            new StewardRuntimeSettings(GuildId, DiscordTokenConfigured: true));

        var report = await analyzer.AnalyzeAsync(CancellationToken.None);

        Assert.Equal("manageable", report.Roles.Single(role => role.RoleId == "5").DecisionCode);
        Assert.Equal("denied_managed_role", report.Roles.Single(role => role.RoleId == "10").DecisionCode);
        Assert.Equal("denied_role_hierarchy", report.Roles.Single(role => role.RoleId == "30").DecisionCode);
        Assert.DoesNotContain(report.Roles, role => role.RoleId == "42");
    }

    private static RoleSnapshot CreateRole(
        ulong id,
        string name,
        int position,
        bool managed = false,
        bool everyone = false,
        bool protectedRole = false) => RoleSnapshot.Create(
            GuildId,
            id,
            name,
            position,
            managed,
            everyone,
            protectedRole ? new ResourceProtection(true, ["resource_id"]) : ResourceProtection.None,
            [],
            colorHex: null,
            isHoisted: false,
            isMentionable: false);

    private sealed class FakeInspector(BotEffectivePermissions result) : IDiscordBotInspector
    {
        public Task<BotEffectivePermissions> GetEffectivePermissionsAsync(
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
