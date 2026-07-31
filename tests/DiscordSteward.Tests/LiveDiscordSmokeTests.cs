using System.Runtime.CompilerServices;
using System.Text.Json;
using DiscordSteward.Errors;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DiscordSteward.Tests;

public sealed class LiveDiscordSmokeTests(ITestOutputHelper output)
{
    private static readonly string[] DisposableRolePermissions = ["ViewChannel", "SendMessages"];
    private static readonly string[] ViewChannelPermission = ["ViewChannel"];
    private static readonly string[] AppliedOrVerifiedNoChangeOutcomes = ["succeeded", "failed"];
    private static readonly string[] AcceptedInviteJobStatuses = ["processing", "completed"];
    private static readonly string[] AcceptedCapabilityErrors = ["invalid_argument", "discord_permission_denied"];
    private static readonly string[] AutoModSimulationKeywords = ["steward-live-keyword"];
    private static readonly string[] NicknameClear = ["nickname"];

    private sealed record LiveGuildState(
        string Name,
        string Digest,
        string? AfkChannelId,
        int AfkTimeoutSeconds,
        string? SystemChannelId,
        int SystemChannelFlags,
        string? RulesChannelId,
        string? PublicUpdatesChannelId,
        string? SafetyAlertsChannelId,
        int VerificationLevel,
        int DefaultMessageNotifications,
        int ExplicitContentFilter);

    private sealed record LiveWidgetState(bool Enabled, string? ChannelId, string Digest);

    private sealed record LiveGuildAccessState(
        bool InvitesDisabled,
        bool RaidAlertsDisabled,
        string Digest);

    private sealed record LiveWelcomeState(bool Enabled, string? Description, int ChannelCount, string Digest);

    private sealed record LiveOnboardingState(
        bool Enabled,
        string Mode,
        int PromptCount,
        int DefaultChannelCount,
        string Digest);

    private sealed record LiveIncidentState(
        DateTimeOffset? InvitesDisabledUntil,
        DateTimeOffset? DirectMessagesDisabledUntil,
        string Digest);

    [Fact]
    [Trait("Category", "LiveDiscord")]
    public async Task AuthorizedDisposableFixtureInventory_ReportsVisibleResources()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "list_automod_rules",
            "list_scheduled_events",
            "list_guild_expressions",
            "list_invites",
            "list_webhooks"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(guildId, timeout.Token, names, sensitiveReads: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var fixtures = new List<string>();
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var data = snapshot.RootElement.GetProperty("data");
        fixtures.AddRange(data.GetProperty("roles").EnumerateArray()
            .Where(item => item.GetProperty("name").GetString()?.StartsWith("ds-live-", StringComparison.Ordinal) == true)
            .Select(item => $"role:{item.GetProperty("id").GetString()}:{item.GetProperty("name").GetString()}"));
        fixtures.AddRange(data.GetProperty("categories").EnumerateArray()
            .Where(item => item.GetProperty("name").GetString()?.StartsWith("ds-live-", StringComparison.Ordinal) == true)
            .Select(item => $"category:{item.GetProperty("id").GetString()}:{item.GetProperty("name").GetString()}"));
        fixtures.AddRange(data.GetProperty("channels").EnumerateArray()
            .Where(item => item.GetProperty("name").GetString()?.StartsWith("ds-live-", StringComparison.Ordinal) == true)
            .Select(item => $"channel:{item.GetProperty("id").GetString()}:{item.GetProperty("name").GetString()}"));
        using var rules = await CallOkAsync(tools["list_automod_rules"], null, timeout.Token);
        fixtures.AddRange(rules.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("name").GetString()?.StartsWith("ds-live-", StringComparison.Ordinal) == true)
            .Select(item => $"automod:{item.GetProperty("id").GetString()}:{item.GetProperty("name").GetString()}"));
        using var events = await CallOkAsync(tools["list_scheduled_events"], null, timeout.Token);
        fixtures.AddRange(events.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("name").GetString()?.StartsWith("ds-live-", StringComparison.Ordinal) == true)
            .Select(item => $"event:{item.GetProperty("id").GetString()}:{item.GetProperty("name").GetString()}"));
        using var expressions = await CallOkAsync(
            tools["list_guild_expressions"],
            new Dictionary<string, object?> { ["expression_type"] = "emoji" },
            timeout.Token);
        fixtures.AddRange(expressions.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("name").GetString()?.StartsWith("dslive", StringComparison.Ordinal) == true)
            .Select(item => $"emoji:{item.GetProperty("id").GetString()}:{item.GetProperty("name").GetString()}"));
        using var invites = await CallOkAsync(tools["list_invites"], null, timeout.Token);
        using var webhooks = await CallOkAsync(tools["list_webhooks"], null, timeout.Token);
        output.WriteLine("Visible disposable fixtures ({0}): {1}", fixtures.Count, string.Join(" | ", fixtures.Order(StringComparer.Ordinal)));
        output.WriteLine("Current invite count={0}; webhook count={1}.", invites.RootElement.GetProperty("data").GetArrayLength(), webhooks.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    [Trait("Category", "LiveDiscord")]
    public async Task AuthorizedApiExpansionReads_ExerciseAllNewReferenceEndpoints()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_bot_guild_profile",
            "get_guild_preview",
            "get_guild_widget_image",
            "list_voice_regions",
            "list_sticker_packs",
            "get_sticker_pack",
            "list_default_soundboard_sounds",
            "list_channels",
            "list_channel_invites"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(guildId, timeout.Token, names, sensitiveReads: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        using var profile = await CallOkAsync(tools["get_bot_guild_profile"], null, timeout.Token);
        Assert.False(string.IsNullOrWhiteSpace(profile.RootElement.GetProperty("data").GetProperty("botUserId").GetString()));

        using var preview = await CallOkAsync(tools["get_guild_preview"], null, timeout.Token);
        Assert.Equal(guildId, preview.RootElement.GetProperty("data").GetProperty("guildId").GetString());

        using var widgetImage = await CallAsync(tools["get_guild_widget_image"], null, timeout.Token);
        if (widgetImage.RootElement.GetProperty("outcome").GetString() == "ok")
        {
            var widgetData = widgetImage.RootElement.GetProperty("data");
            Assert.Equal("image/png", widgetData.GetProperty("mediaType").GetString());
            Assert.Equal(64, widgetData.GetProperty("sha256").GetString()?.Length);
            Assert.True(widgetData.GetProperty("byteLength").GetInt32() > 0);
        }
        else
        {
            Assert.Equal(
                StewardErrorCodes.ResourceNotFound,
                widgetImage.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        using var globalRegions = await CallOkAsync(
            tools["list_voice_regions"],
            new Dictionary<string, object?> { ["scope"] = "global" },
            timeout.Token);
        using var guildRegions = await CallOkAsync(
            tools["list_voice_regions"],
            new Dictionary<string, object?> { ["scope"] = "guild" },
            timeout.Token);
        Assert.NotEmpty(globalRegions.RootElement.GetProperty("data").EnumerateArray());
        Assert.NotEmpty(guildRegions.RootElement.GetProperty("data").EnumerateArray());

        using var packs = await CallOkAsync(tools["list_sticker_packs"], null, timeout.Token);
        var packItems = packs.RootElement.GetProperty("data");
        Assert.NotEmpty(packItems.EnumerateArray());
        var packId = packItems[0].GetProperty("id").GetString()!;
        using var exactPack = await CallOkAsync(
            tools["get_sticker_pack"],
            new Dictionary<string, object?> { ["pack_id"] = packId },
            timeout.Token);
        Assert.Equal(packId, exactPack.RootElement.GetProperty("data").GetProperty("id").GetString());

        using var sounds = await CallOkAsync(tools["list_default_soundboard_sounds"], null, timeout.Token);
        Assert.NotEmpty(sounds.RootElement.GetProperty("data").EnumerateArray());

        using var channels = await CallOkAsync(tools["list_channels"], null, timeout.Token);
        var textChannel = channels.RootElement.GetProperty("data").EnumerateArray()
            .First(channel => channel.GetProperty("kind").GetString() is "text" or "announcement");
        using var channelInvites = await CallOkAsync(
            tools["list_channel_invites"],
            new Dictionary<string, object?> { ["channel_id"] = textChannel.GetProperty("id").GetString() },
            timeout.Token);

        output.WriteLine(
            "API expansion reads passed: globalRegions={0}, guildRegions={1}, stickerPacks={2}, defaultSounds={3}, channelInvites={4}.",
            globalRegions.RootElement.GetProperty("data").GetArrayLength(),
            guildRegions.RootElement.GetProperty("data").GetArrayLength(),
            packItems.GetArrayLength(),
            sounds.RootElement.GetProperty("data").GetArrayLength(),
            channelInvites.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedBotGuildProfileLifecycle_UpdatesAndRestoresTextFields()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_bot_guild_profile",
            "get_operation",
            "reconcile_operation",
            "update_bot_guild_profile"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var original = await CallOkAsync(tools["get_bot_guild_profile"], null, timeout.Token);
        var originalData = original.RootElement.GetProperty("data");
        var originalNickname = OptionalString(originalData, "nickname");
        var originalBio = OptionalString(originalData, "bio");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        try
        {
            using var updated = await CallMutationSucceededAsync(
                tools["update_bot_guild_profile"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable live bot guild profile update",
                    ["input"] = new
                    {
                        expectedStateDigest = originalData.GetProperty("stateDigest").GetString(),
                        set = new
                        {
                            nickname = $"Steward {suffix}",
                            avatarAssetId = (string?)null,
                            bannerAssetId = (string?)null
                        },
                        clear = Array.Empty<string>()
                    }
                },
                timeout.Token);
            using var observed = await CallOkAsync(tools["get_bot_guild_profile"], null, timeout.Token);
            var observedData = observed.RootElement.GetProperty("data");
            Assert.Equal($"Steward {suffix}", observedData.GetProperty("nickname").GetString());
            Assert.Equal(originalBio, OptionalString(observedData, "bio"));
        }
        finally
        {
            using var current = await CallOkAsync(tools["get_bot_guild_profile"], null, timeout.Token);
            var currentData = current.RootElement.GetProperty("data");
            if (OptionalString(currentData, "nickname") != originalNickname)
            {
                var clear = new List<string>();
                if (originalNickname is null) clear.Add("nickname");
                using var restored = await CallMutationSucceededAsync(
                    tools["update_bot_guild_profile"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["reason"] = "Restore live bot guild profile",
                        ["input"] = new
                        {
                            expectedStateDigest = currentData.GetProperty("stateDigest").GetString(),
                            set = new
                            {
                                nickname = originalNickname,
                                avatarAssetId = (string?)null,
                                bannerAssetId = (string?)null
                            },
                            clear = clear.ToArray()
                        }
                    },
                    timeout.Token);
            }
        }

        using var final = await CallOkAsync(tools["get_bot_guild_profile"], null, timeout.Token);
        Assert.Equal(originalNickname, OptionalString(final.RootElement.GetProperty("data"), "nickname"));
        Assert.Equal(originalBio, OptionalString(final.RootElement.GetProperty("data"), "bio"));
        output.WriteLine("Bot guild profile nickname was updated and restored; bio remained read-only evidence.");
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedBotGuildProfileCleanup_ClearsExactExpectedNickname()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        var expectedNickname = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_PROFILE_CLEANUP_NICKNAME");
        if (string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(expectedNickname) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names = ["get_bot_guild_profile", "get_operation", "reconcile_operation", "update_bot_guild_profile"];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var current = await CallOkAsync(tools["get_bot_guild_profile"], null, timeout.Token);
        var currentData = current.RootElement.GetProperty("data");
        Assert.Equal(expectedNickname, currentData.GetProperty("nickname").GetString());
        using var cleared = await CallMutationSucceededAsync(
            tools["update_bot_guild_profile"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Exact interrupted bot profile cleanup",
                ["input"] = new
                {
                    expectedStateDigest = currentData.GetProperty("stateDigest").GetString(),
                    set = new
                    {
                        nickname = (string?)null,
                        avatarAssetId = (string?)null,
                        bannerAssetId = (string?)null
                    },
                    clear = NicknameClear
                }
            },
            timeout.Token);
        using var final = await CallOkAsync(tools["get_bot_guild_profile"], null, timeout.Token);
        Assert.Null(OptionalString(final.RootElement.GetProperty("data"), "nickname"));
        output.WriteLine("Cleared exact interrupted bot guild nickname {0}.", expectedNickname);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedRoleLifecycle_CreatesMutatesAssignsAndDeletesDisposableRole()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var names = new[]
        {
            "get_guild_configuration_snapshot",
            "get_bot_effective_permissions",
            "get_role",
            "get_member",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_role",
            "update_role_appearance",
            "set_role_permissions",
            "set_member_role",
            "delete_role"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var snapshotData = snapshot.RootElement.GetProperty("data");
        var ownerId = snapshotData.GetProperty("ownerId").GetString()!;
        var rolesDigest = snapshotData.GetProperty("rolesStateDigest").GetString()!;
        var enhancedRoleColors = snapshotData.GetProperty("features").EnumerateArray()
            .Any(feature => string.Equals(
                feature.GetString()?.Replace("_", string.Empty, StringComparison.Ordinal),
                "EnhancedRoleColors",
                StringComparison.OrdinalIgnoreCase));
        using var botPermissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
        var botRolePosition = botPermissions.RootElement.GetProperty("data").GetProperty("observed").GetProperty("highestRolePosition").GetInt32();
        var staleRoles = snapshotData.GetProperty("roles").EnumerateArray()
            .Where(role => role.GetProperty("name").GetString()?.StartsWith("ds-live-role", StringComparison.Ordinal) == true)
            .Select(role => new
            {
                Id = role.GetProperty("id").GetString()!,
                Position = role.GetProperty("position").GetInt32()
            })
            .ToArray();
        if (botRolePosition <= 1 || staleRoles.Any(role => role.Position >= botRolePosition))
        {
            Assert.Fail(
                $"Move the managed DiscSteward role above all disposable roles before rerunning. " +
                $"Bot position={botRolePosition}; blocked fixtures={string.Join(',', staleRoles.Where(role => role.Position >= botRolePosition).Select(role => role.Id))}.");
        }

        foreach (var staleRole in staleRoles)
        {
            await DeleteRoleFixtureAsync(tools, staleRole.Id, timeout.Token);
        }

        if (staleRoles.Length > 0)
        {
            using var refreshed = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
            rolesDigest = refreshed.RootElement.GetProperty("data").GetProperty("rolesStateDigest").GetString()!;
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var created = await CallMutationSucceededAsync(
            tools["create_role"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live role lifecycle create",
                ["expected_roles_state_digest"] = rolesDigest,
                ["name"] = $"ds-live-role-{suffix}",
                ["permissions"] = Array.Empty<string>(),
                ["color_hex"] = "#1ABC9C",
                ["is_hoisted"] = false,
                ["is_mentionable"] = false
            },
            timeout.Token);
        var roleId = created.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;

        using var initialRole = await CallOkAsync(
            tools["get_role"],
            new Dictionary<string, object?> { ["role_id"] = roleId },
            timeout.Token);
        var roleDigest = initialRole.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        if (!enhancedRoleColors)
        {
            using var refusedGradient = await CallMutationErrorAsync(
                tools["update_role_appearance"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["role_id"] = roleId,
                    ["reason"] = "Live enhanced role-color capability refusal",
                    ["expected_state_digest"] = roleDigest,
                    ["set"] = new
                    {
                        name = (string?)null,
                        colorHex = (string?)null,
                        secondaryColorHex = "#9B59B6",
                        tertiaryColorHex = (string?)null,
                        isHoisted = (bool?)null,
                        isMentionable = (bool?)null,
                        iconAssetId = (string?)null,
                        unicodeEmoji = (string?)null
                    },
                    ["clear"] = Array.Empty<string>()
                },
                "invalid_argument",
                timeout.Token);
        }

        using var appearance = await CallMutationSucceededAsync(
            tools["update_role_appearance"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["role_id"] = roleId,
                ["reason"] = "Disposable live role lifecycle appearance",
                ["expected_state_digest"] = roleDigest,
                ["set"] = new
                {
                    name = $"ds-live-role-updated-{suffix}",
                    colorHex = "#3498DB",
                    secondaryColorHex = enhancedRoleColors ? "#9B59B6" : null,
                    tertiaryColorHex = (string?)null,
                    isHoisted = true,
                    isMentionable = true,
                    iconAssetId = (string?)null,
                    unicodeEmoji = (string?)null
                },
                ["clear"] = Array.Empty<string>()
            },
            timeout.Token);

        using var appearedRole = await CallOkAsync(
            tools["get_role"],
            new Dictionary<string, object?> { ["role_id"] = roleId },
            timeout.Token);
        Assert.Equal(
            enhancedRoleColors ? "#9B59B6" : null,
            OptionalString(appearedRole.RootElement.GetProperty("data"), "secondaryColorHex"));
        roleDigest = appearedRole.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var permissions = await CallMutationSucceededAsync(
            tools["set_role_permissions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["role_id"] = roleId,
                ["reason"] = "Disposable live role lifecycle permissions",
                ["expected_state_digest"] = roleDigest,
                ["permissions"] = DisposableRolePermissions
            },
            timeout.Token);

        using var member = await CallOkAsync(
            tools["get_member"],
            new Dictionary<string, object?> { ["user_id"] = ownerId },
            timeout.Token);
        var memberDigest = member.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var added = await CallMutationSucceededAsync(
            tools["set_member_role"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["user_id"] = ownerId,
                ["role_id"] = roleId,
                ["present"] = true,
                ["reason"] = "Disposable live role lifecycle assign",
                ["expected_member_state_digest"] = memberDigest
            },
            timeout.Token);
        using var assignedMember = await CallOkAsync(
            tools["get_member"],
            new Dictionary<string, object?> { ["user_id"] = ownerId },
            timeout.Token);
        Assert.Contains(
            roleId,
            assignedMember.RootElement.GetProperty("data").GetProperty("roleIds").EnumerateArray().Select(value => value.GetString()));
        memberDigest = assignedMember.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var removed = await CallMutationSucceededAsync(
            tools["set_member_role"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["user_id"] = ownerId,
                ["role_id"] = roleId,
                ["present"] = false,
                ["reason"] = "Disposable live role lifecycle unassign",
                ["expected_member_state_digest"] = memberDigest
            },
            timeout.Token);

        using var finalRole = await CallOkAsync(
            tools["get_role"],
            new Dictionary<string, object?> { ["role_id"] = roleId },
            timeout.Token);
        roleDigest = finalRole.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var impact = await CallOkAsync(
            tools["analyze_deletion_impact"],
            new Dictionary<string, object?> { ["resource_type"] = "role", ["resource_id"] = roleId },
            timeout.Token);
        var impactData = impact.RootElement.GetProperty("data");
        Assert.Empty(impactData.GetProperty("blockers").EnumerateArray());
        Assert.Empty(impactData.GetProperty("cascade").EnumerateArray());
        using var deleted = await CallMutationSucceededAsync(
            tools["delete_role"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["role_id"] = roleId,
                ["reason"] = "Disposable live role lifecycle cleanup",
                ["expected_state_digest"] = roleDigest,
                ["expected_cascade_digest"] = impactData.GetProperty("cascadeDigest").GetString()
            },
            timeout.Token);
        using var missing = await CallAsync(
            tools["get_role"],
            new Dictionary<string, object?> { ["role_id"] = roleId },
            timeout.Token);
        Assert.Equal("error", missing.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("resource_not_found", missing.RootElement.GetProperty("error").GetProperty("code").GetString());
        output.WriteLine("Disposable role lifecycle passed and deleted role {0}.", roleId);
    }

    private static async Task DeleteRoleFixtureAsync(
        Dictionary<string, McpClientTool> tools,
        string roleId,
        CancellationToken cancellationToken)
    {
        using var role = await CallOkAsync(
            tools["get_role"],
            new Dictionary<string, object?> { ["role_id"] = roleId },
            cancellationToken);
        using var impact = await CallOkAsync(
            tools["analyze_deletion_impact"],
            new Dictionary<string, object?> { ["resource_type"] = "role", ["resource_id"] = roleId },
            cancellationToken);
        var impactData = impact.RootElement.GetProperty("data");
        Assert.Empty(impactData.GetProperty("blockers").EnumerateArray());
        Assert.Empty(impactData.GetProperty("cascade").EnumerateArray());
        using var deleted = await CallMutationSucceededAsync(
            tools["delete_role"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["role_id"] = roleId,
                ["reason"] = "Disposable live role fixture cleanup",
                ["expected_state_digest"] = role.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                ["expected_cascade_digest"] = impactData.GetProperty("cascadeDigest").GetString()
            },
            cancellationToken);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedRoleCleanup_RemovesExactDisposableFixture()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        var roleId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_CLEANUP_ROLE_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(roleId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_role",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "delete_role"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        await DeleteRoleFixtureAsync(tools, roleId, timeout.Token);
        output.WriteLine("Cleaned exact disposable role {0}.", roleId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedRoleLifecycleContinuation_SetsAssignsUnassignsAndDeletesExactRole()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        var roleId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_ROLE_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(roleId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_role",
            "get_member",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "set_role_permissions",
            "set_member_role",
            "delete_role"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var ownerId = snapshot.RootElement.GetProperty("data").GetProperty("ownerId").GetString()!;
        using var role = await CallOkAsync(
            tools["get_role"],
            new Dictionary<string, object?> { ["role_id"] = roleId },
            timeout.Token);
        using var permissions = await CallMutationSucceededAsync(
            tools["set_role_permissions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["role_id"] = roleId,
                ["reason"] = "Disposable live role continuation permissions",
                ["expected_state_digest"] = role.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                ["permissions"] = DisposableRolePermissions
            },
            timeout.Token);

        using var member = await CallOkAsync(
            tools["get_member"],
            new Dictionary<string, object?> { ["user_id"] = ownerId },
            timeout.Token);
        using var added = await CallMutationSucceededAsync(
            tools["set_member_role"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["user_id"] = ownerId,
                ["role_id"] = roleId,
                ["present"] = true,
                ["reason"] = "Disposable live role continuation assign",
                ["expected_member_state_digest"] = member.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
            },
            timeout.Token);
        using var assignedMember = await CallOkAsync(
            tools["get_member"],
            new Dictionary<string, object?> { ["user_id"] = ownerId },
            timeout.Token);
        Assert.Contains(
            roleId,
            assignedMember.RootElement.GetProperty("data").GetProperty("roleIds").EnumerateArray().Select(value => value.GetString()));
        using var removed = await CallMutationSucceededAsync(
            tools["set_member_role"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["user_id"] = ownerId,
                ["role_id"] = roleId,
                ["present"] = false,
                ["reason"] = "Disposable live role continuation unassign",
                ["expected_member_state_digest"] = assignedMember.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
            },
            timeout.Token);

        await DeleteRoleFixtureAsync(tools, roleId, timeout.Token);
        output.WriteLine("Role continuation passed and deleted exact role {0}.", roleId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedTextChannelLifecycle_CreatesUpdatesPermissionsAndDeletesFixtures()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var names = new[]
        {
            "get_guild_configuration_snapshot",
            "get_channel",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_category",
            "create_text_channel",
            "update_channel",
            "set_role_channel_permissions",
            "remove_role_channel_permissions",
            "delete_channel"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var initialSnapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var channelsDigest = initialSnapshot.RootElement.GetProperty("data").GetProperty("channelsStateDigest").GetString()!;
        using var categoryCreate = await CallMutationSucceededAsync(
            tools["create_category"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live category create",
                ["input"] = new
                {
                    expectedChannelsStateDigest = channelsDigest,
                    name = $"ds-live-category-{suffix}",
                    position = (int?)null,
                    roleOverwrites = Array.Empty<object>()
                }
            },
            timeout.Token);
        var categoryId = categoryCreate.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
        using var category = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = categoryId },
            timeout.Token);
        var categoryDigest = category.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!;

        using var afterCategory = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        channelsDigest = afterCategory.RootElement.GetProperty("data").GetProperty("channelsStateDigest").GetString()!;
        using var channelCreate = await CallMutationSucceededAsync(
            tools["create_text_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live text channel create",
                ["input"] = new
                {
                    expectedChannelsStateDigest = channelsDigest,
                    expectedParentStateDigest = categoryDigest,
                    name = $"ds-live-text-{suffix}",
                    parentId = categoryId,
                    topic = "Disposable Discord Steward live fixture",
                    isNsfw = false,
                    slowModeSeconds = 0,
                    defaultAutoArchiveMinutes = 1440,
                    roleOverwrites = Array.Empty<object>()
                }
            },
            timeout.Token);
        var channelId = channelCreate.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
        using var channel = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            timeout.Token);
        var channelData = channel.RootElement.GetProperty("data");
        using var updated = await CallMutationSucceededAsync(
            tools["update_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live text channel direct update",
                ["expected_state_digest"] = channelData.GetProperty("mutationStateDigest").GetString(),
                ["set"] = new
                {
                    name = $"ds-live-text-updated-{suffix}",
                    topic = "Updated and verified by Discord Steward",
                    slowModeSeconds = 3
                },
                ["clear"] = Array.Empty<string>()
            },
            timeout.Token);

        using var updatedChannel = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            timeout.Token);
        var mutationDigest = updatedChannel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!;
        using var overwrite = await CallMutationSucceededAsync(
            tools["set_role_channel_permissions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live role overwrite set",
                ["input"] = new
                {
                    expectedStateDigest = mutationDigest,
                    targetId = guildId,
                    allow = DisposableRolePermissions,
                    deny = Array.Empty<string>(),
                    inherit = Array.Empty<string>()
                }
            },
            timeout.Token);
        using var overwrittenChannel = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            timeout.Token);
        mutationDigest = overwrittenChannel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!;
        using var removedOverwrite = await CallMutationSucceededAsync(
            tools["remove_role_channel_permissions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live role overwrite remove",
                ["input"] = new { expectedStateDigest = mutationDigest, targetId = guildId }
            },
            timeout.Token);

        await DeleteChannelFixtureAsync(tools, channelId, timeout.Token);
        await DeleteChannelFixtureAsync(tools, categoryId, timeout.Token);
        output.WriteLine("Disposable text channel lifecycle passed and deleted channel {0} plus category {1}.", channelId, categoryId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedChannelVariants_ExerciseMovementOverwritesArchiveVoiceAndCommunityRefusals()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_bot_effective_permissions",
            "get_channel",
            "list_channel_member_overwrites",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_category",
            "create_text_channel",
            "create_announcement_channel",
            "create_voice_channel",
            "create_stage_channel",
            "create_forum_channel",
            "create_media_channel",
            "move_channel",
            "move_channel_and_sync_permissions",
            "reorder_channels",
            "set_member_channel_permissions",
            "remove_member_channel_permissions",
            "replace_channel_permission_overwrites",
            "archive_channel",
            "restore_archived_channel",
            "set_voice_channel_status",
            "follow_announcement_channel",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var initialSnapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var snapshotData = initialSnapshot.RootElement.GetProperty("data");
        var ownerId = snapshotData.GetProperty("ownerId").GetString()!;
        using var botPermissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
        var botRoleId = botPermissions.RootElement.GetProperty("data").GetProperty("observed").GetProperty("highestRoleId").GetString()!;
        var communityEnabled = snapshotData.GetProperty("features").EnumerateArray()
            .Any(feature => string.Equals(feature.GetString(), "COMMUNITY", StringComparison.OrdinalIgnoreCase));

        var activeCategoryId = await CreateLiveChannelAsync(
            tools,
            "create_category",
            $"ds-live-active-{suffix}",
            parentId: null,
            timeout.Token);
        var archiveCategoryId = await CreateLiveChannelAsync(
            tools,
            "create_category",
            $"ds-live-archive-{suffix}",
            parentId: null,
            timeout.Token);
        var textChannelId = await CreateLiveChannelAsync(
            tools,
            "create_text_channel",
            $"ds-live-variant-text-{suffix}",
            activeCategoryId,
            timeout.Token);
        var voiceChannelId = await CreateLiveChannelAsync(
            tools,
            "create_voice_channel",
            $"ds-live-voice-{suffix}",
            activeCategoryId,
            timeout.Token);

        using var voice = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = voiceChannelId },
            timeout.Token);
        using var statusSet = await CallAsync(
            tools["set_voice_channel_status"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = voiceChannelId,
                ["reason"] = "Disposable live voice status set",
                ["input"] = new
                {
                    expectedStateDigest = voice.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    status = "Discord Steward live fixture"
                }
            },
            timeout.Token);
        var statusOutcome = statusSet.RootElement.GetProperty("outcome").GetString();
        Assert.Contains(statusOutcome, AppliedOrVerifiedNoChangeOutcomes);
        if (statusOutcome == "failed")
        {
            Assert.Equal("state_conflict", statusSet.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        else
        {
            using var voiceWithStatus = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = voiceChannelId },
                timeout.Token);
            using var statusCleared = await CallMutationSucceededAsync(
                tools["set_voice_channel_status"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = voiceChannelId,
                    ["reason"] = "Disposable live voice status clear",
                    ["input"] = new
                    {
                        expectedStateDigest = voiceWithStatus.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                        status = (string?)null
                    }
                },
                timeout.Token);
        }

        using var text = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = textChannelId },
            timeout.Token);
        using var memberOverwrite = await CallMutationSucceededAsync(
            tools["set_member_channel_permissions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = textChannelId,
                ["reason"] = "Disposable live member overwrite set",
                ["input"] = new
                {
                    expectedStateDigest = text.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    targetId = ownerId,
                    allow = ViewChannelPermission,
                    deny = Array.Empty<string>(),
                    inherit = Array.Empty<string>()
                }
            },
            timeout.Token);
        using var memberOverwrites = await CallOkAsync(
            tools["list_channel_member_overwrites"],
            new Dictionary<string, object?> { ["channel_id"] = textChannelId, ["limit"] = 10 },
            timeout.Token);
        Assert.Contains(
            memberOverwrites.RootElement.GetProperty("data").GetProperty("items").EnumerateArray(),
            overwrite => overwrite.GetProperty("memberId").GetString() == ownerId);
        using var textWithMemberOverwrite = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = textChannelId },
            timeout.Token);
        using var memberOverwriteRemoved = await CallMutationSucceededAsync(
            tools["remove_member_channel_permissions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = textChannelId,
                ["reason"] = "Disposable live member overwrite remove",
                ["input"] = new
                {
                    expectedStateDigest = textWithMemberOverwrite.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    targetId = ownerId
                }
            },
            timeout.Token);

        using var textWithoutMemberOverwrite = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = textChannelId },
            timeout.Token);
        using var activeCategory = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = activeCategoryId },
            timeout.Token);
        using var overwritesReplaced = await CallMutationSucceededAsync(
            tools["replace_channel_permission_overwrites"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = textChannelId,
                ["reason"] = "Disposable live overwrite replacement",
                ["input"] = new
                {
                    expectedStateDigest = textWithoutMemberOverwrite.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    expectedParentStateDigest = activeCategory.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    expectedSyncedChildren = Array.Empty<object>(),
                    overwrites = new[]
                    {
                        new
                        {
                            targetId = guildId,
                            targetType = "role",
                            allow = ViewChannelPermission,
                            deny = Array.Empty<string>()
                        }
                    }
                }
            },
            timeout.Token);

        using var textBeforeArchive = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = textChannelId },
            timeout.Token);
        using var archiveCategory = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = archiveCategoryId },
            timeout.Token);
        using var archived = await CallMutationSucceededAsync(
            tools["archive_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = textChannelId,
                ["reason"] = "Disposable live channel archive",
                ["input"] = new
                {
                    expectedStateDigest = textBeforeArchive.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    destinationParentId = archiveCategoryId,
                    expectedDestinationParentStateDigest = archiveCategory.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    name = $"ds-live-archived-{suffix}",
                    visibility = "private",
                    overwrites = new[]
                    {
                        new
                        {
                            targetId = guildId,
                            targetType = "role",
                            allow = Array.Empty<string>(),
                            deny = ViewChannelPermission
                        },
                        new
                        {
                            targetId = botRoleId,
                            targetType = "role",
                            allow = ViewChannelPermission,
                            deny = Array.Empty<string>()
                        }
                    }
                }
            },
            timeout.Token);
        using var archivedText = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = textChannelId },
            timeout.Token);
        using var activeCategoryForRestore = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = activeCategoryId },
            timeout.Token);
        using var restored = await CallMutationSucceededAsync(
            tools["restore_archived_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = textChannelId,
                ["reason"] = "Disposable live channel restore",
                ["input"] = new
                {
                    expectedStateDigest = archivedText.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    parentId = activeCategoryId,
                    expectedParentStateDigest = activeCategoryForRestore.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    name = $"ds-live-variant-text-{suffix}",
                    visibility = "public",
                    overwrites = new[]
                    {
                        new
                        {
                            targetId = guildId,
                            targetType = "role",
                            allow = ViewChannelPermission,
                            deny = Array.Empty<string>()
                        }
                    }
                }
            },
            timeout.Token);

        using var voiceBeforeMove = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = voiceChannelId },
            timeout.Token);
        using var snapshotBeforeMove = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        using var archiveForMove = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = archiveCategoryId },
            timeout.Token);
        using var moved = await CallMutationSucceededAsync(
            tools["move_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = voiceChannelId,
                ["reason"] = "Disposable live channel move",
                ["input"] = new
                {
                    expectedStateDigest = voiceBeforeMove.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    expectedChannelsStateDigest = snapshotBeforeMove.RootElement.GetProperty("data").GetProperty("channelsStateDigest").GetString(),
                    destinationParentId = archiveCategoryId,
                    expectedDestinationParentStateDigest = archiveForMove.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    position = 0
                }
            },
            timeout.Token);
        using var voiceBeforeSyncMove = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = voiceChannelId },
            timeout.Token);
        using var snapshotBeforeSyncMove = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        using var activeForSyncMove = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = activeCategoryId },
            timeout.Token);
        using var syncMoved = await CallMutationSucceededAsync(
            tools["move_channel_and_sync_permissions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = voiceChannelId,
                ["reason"] = "Disposable live channel move and sync",
                ["input"] = new
                {
                    expectedStateDigest = voiceBeforeSyncMove.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    expectedChannelsStateDigest = snapshotBeforeSyncMove.RootElement.GetProperty("data").GetProperty("channelsStateDigest").GetString(),
                    destinationParentId = activeCategoryId,
                    expectedDestinationParentStateDigest = activeForSyncMove.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    position = 1
                }
            },
            timeout.Token);
        using var snapshotBeforeReorder = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        using var reordered = await CallMutationSucceededAsync(
            tools["reorder_channels"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live sibling reorder",
                ["input"] = new
                {
                    expectedChannelsStateDigest = snapshotBeforeReorder.RootElement.GetProperty("data").GetProperty("channelsStateDigest").GetString(),
                    parentId = activeCategoryId,
                    positions = new[]
                    {
                        new { channelId = textChannelId, position = 0 },
                        new { channelId = voiceChannelId, position = 1 }
                    }
                }
            },
            timeout.Token);

        using var invalidFollowSource = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = textChannelId },
            timeout.Token);
        using var invalidFollow = await CallMutationErrorAsync(
            tools["follow_announcement_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Live non-announcement follow refusal",
                ["input"] = new
                {
                    sourceChannelId = textChannelId,
                    expectedSourceStateDigest = invalidFollowSource.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    destinationChannelId = textChannelId,
                    expectedDestinationStateDigest = invalidFollowSource.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()
                }
            },
            "invalid_argument",
            timeout.Token);

        var communityChannelIds = new List<string>();
        foreach (var (toolName, channelName) in new[]
        {
            ("create_announcement_channel", $"ds-live-announcement-{suffix}"),
            ("create_stage_channel", $"ds-live-stage-{suffix}"),
            ("create_forum_channel", $"ds-live-forum-{suffix}"),
            ("create_media_channel", $"ds-live-media-{suffix}")
        })
        {
            if (communityEnabled)
            {
                communityChannelIds.Add(await CreateLiveChannelAsync(tools, toolName, channelName, activeCategoryId, timeout.Token));
            }
            else
            {
                var arguments = await CreateLiveChannelArgumentsAsync(tools, toolName, channelName, activeCategoryId, timeout.Token);
                using var refused = await CallMutationErrorAsync(tools[toolName], arguments, "invalid_argument", timeout.Token);
            }
        }

        foreach (var communityChannelId in communityChannelIds)
        {
            await DeleteChannelFixtureAsync(tools, communityChannelId, timeout.Token);
        }

        await DeleteChannelFixtureAsync(tools, textChannelId, timeout.Token);
        await DeleteChannelFixtureAsync(tools, voiceChannelId, timeout.Token);
        await DeleteChannelFixtureAsync(tools, activeCategoryId, timeout.Token);
        await DeleteChannelFixtureAsync(tools, archiveCategoryId, timeout.Token);
        output.WriteLine(
            "Channel variants passed and cleaned text {0}, voice {1}, categories {2}/{3}; Community enabled={4}.",
            textChannelId,
            voiceChannelId,
            activeCategoryId,
            archiveCategoryId,
            communityEnabled);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedExpandedChannelFields_UpdateVoiceAndForumAndCleanFixtures()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_channel",
            "list_voice_regions",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_text_channel",
            "create_voice_channel",
            "create_forum_channel",
            "update_channel",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        string? voiceId = null;
        string? forumId = null;
        string? convertibleId = null;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        try
        {
            using var regions = await CallOkAsync(
                tools["list_voice_regions"],
                new Dictionary<string, object?> { ["scope"] = "guild" },
                timeout.Token);
            var region = regions.RootElement.GetProperty("data").EnumerateArray()
                .First(item => !item.GetProperty("deprecated").GetBoolean())
                .GetProperty("id").GetString()!;

            voiceId = await CreateLiveChannelAsync(
                tools,
                "create_voice_channel",
                $"ds-live-expanded-voice-{suffix}",
                parentId: null,
                timeout.Token);
            using var voice = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = voiceId },
                timeout.Token);
            using var voiceUpdated = await CallMutationSucceededAsync(
                tools["update_channel"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = voiceId,
                    ["reason"] = "Disposable live expanded voice update",
                    ["expected_state_digest"] = voice.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    ["set"] = new
                    {
                        bitrate = 72_000,
                        userLimit = 7,
                        rtcRegion = region,
                        videoQualityMode = 2
                    },
                    ["clear"] = Array.Empty<string>()
                },
                timeout.Token);
            using var updatedVoice = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = voiceId },
                timeout.Token);
            var voiceSettings = updatedVoice.RootElement.GetProperty("data").GetProperty("mutationSettings");
            Assert.Equal(72_000, voiceSettings.GetProperty("bitrate").GetInt32());
            Assert.Equal(7, voiceSettings.GetProperty("userLimit").GetInt32());
            Assert.Equal(region, voiceSettings.GetProperty("rtcRegion").GetString());
            Assert.Equal(2, voiceSettings.GetProperty("videoQualityMode").GetInt32());

            convertibleId = await CreateLiveChannelAsync(
                tools,
                "create_text_channel",
                $"ds-live-convertible-{suffix}",
                parentId: null,
                timeout.Token);
            using var convertible = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = convertibleId },
                timeout.Token);
            using var converted = await CallMutationSucceededAsync(
                tools["update_channel"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = convertibleId,
                    ["reason"] = "Disposable live text to announcement conversion",
                    ["expected_state_digest"] = convertible.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    ["set"] = new { channelType = "announcement", isSpoiler = true },
                    ["clear"] = Array.Empty<string>()
                },
                timeout.Token);
            using var announcement = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = convertibleId },
                timeout.Token);
            Assert.Equal("announcement", announcement.RootElement.GetProperty("data").GetProperty("kind").GetString());
            Assert.True(announcement.RootElement.GetProperty("data").GetProperty("mutationSettings").GetProperty("isSpoiler").GetBoolean());
            using var convertedBack = await CallMutationSucceededAsync(
                tools["update_channel"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = convertibleId,
                    ["reason"] = "Disposable live announcement to text conversion",
                    ["expected_state_digest"] = announcement.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    ["set"] = new { channelType = "text", isSpoiler = false },
                    ["clear"] = Array.Empty<string>()
                },
                timeout.Token);

            var forumCreateArguments = await CreateLiveChannelArgumentsAsync(
                tools,
                "create_forum_channel",
                $"ds-live-expanded-forum-{suffix}",
                parentId: null,
                timeout.Token);
            var forumCreateInput = Assert.IsType<Dictionary<string, object?>>(forumCreateArguments["input"]);
            forumCreateInput["availableTags"] = new[]
            {
                new
                {
                    id = (string?)null,
                    name = "Initial",
                    moderated = false,
                    emojiId = (string?)null,
                    emojiName = (string?)null
                }
            };
            forumCreateInput["defaultReaction"] = new { emojiId = (string?)null, emojiName = "\u2705" };
            forumCreateInput["defaultSortOrder"] = 1;
            forumCreateInput["defaultForumLayout"] = 2;
            using var forumCreated = await CallMutationSucceededAsync(
                tools["create_forum_channel"],
                forumCreateArguments,
                timeout.Token);
            forumId = forumCreated.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
            using var forum = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = forumId },
                timeout.Token);
            var initialForumSettings = forum.RootElement.GetProperty("data").GetProperty("mutationSettings");
            Assert.Equal("Initial", Assert.Single(initialForumSettings.GetProperty("availableTags").EnumerateArray()).GetProperty("name").GetString());
            Assert.Equal("\u2705", initialForumSettings.GetProperty("defaultReactionEmojiName").GetString());
            Assert.False(initialForumSettings.GetProperty("requireTag").GetBoolean());
            using var forumTagsUpdated = await CallMutationSucceededAsync(
                tools["update_channel"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = forumId,
                    ["reason"] = "Disposable live forum tag update",
                    ["expected_state_digest"] = forum.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    ["set"] = new
                    {
                        availableTags = new[]
                        {
                            new
                            {
                                id = (string?)null,
                                name = "Validated",
                                moderated = false,
                                emojiId = (string?)null,
                                emojiName = (string?)null
                            }
                        }
                    },
                    ["clear"] = Array.Empty<string>()
                },
                timeout.Token);
            using var forumWithTags = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = forumId },
                timeout.Token);
            var tagSettings = forumWithTags.RootElement.GetProperty("data").GetProperty("mutationSettings");
            var tag = Assert.Single(tagSettings.GetProperty("availableTags").EnumerateArray());
            Assert.Equal("Validated", tag.GetProperty("name").GetString());
            Assert.False(string.IsNullOrWhiteSpace(tag.GetProperty("id").GetString()));

            using var forumDefaultsUpdated = await CallMutationSucceededAsync(
                tools["update_channel"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = forumId,
                    ["reason"] = "Disposable live forum defaults update",
                    ["expected_state_digest"] = forumWithTags.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    ["set"] = new
                    {
                        defaultReaction = new { emojiId = (string?)null, emojiName = "\U0001F44D" },
                        defaultSortOrder = 0,
                        defaultForumLayout = 1
                    },
                    ["clear"] = Array.Empty<string>()
                },
                timeout.Token);
            using var forumWithDefaults = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = forumId },
                timeout.Token);
            var defaultSettings = forumWithDefaults.RootElement.GetProperty("data").GetProperty("mutationSettings");
            Assert.Equal("\U0001F44D", defaultSettings.GetProperty("defaultReactionEmojiName").GetString());
            Assert.Equal(0, defaultSettings.GetProperty("defaultSortOrder").GetInt32());
            Assert.Equal(1, defaultSettings.GetProperty("defaultForumLayout").GetInt32());

            using var forumFlagsUpdated = await CallMutationSucceededAsync(
                tools["update_channel"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = forumId,
                    ["reason"] = "Disposable live forum flags update",
                    ["expected_state_digest"] = forumWithDefaults.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    ["set"] = new { requireTag = true },
                    ["clear"] = Array.Empty<string>()
                },
                timeout.Token);
            using var updatedForum = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = forumId },
                timeout.Token);
            Assert.True(updatedForum.RootElement.GetProperty("data").GetProperty("mutationSettings").GetProperty("requireTag").GetBoolean());
        }
        finally
        {
            if (forumId is not null)
            {
                _ = await TryDeleteChannelFixtureAsync(tools, forumId, timeout.Token);
            }

            if (voiceId is not null)
            {
                _ = await TryDeleteChannelFixtureAsync(tools, voiceId, timeout.Token);
            }

            if (convertibleId is not null)
            {
                _ = await TryDeleteChannelFixtureAsync(tools, convertibleId, timeout.Token);
            }
        }

        output.WriteLine("Expanded voice and forum fields passed with all fixtures deleted.");
    }

    private static async Task<string> CreateLiveChannelAsync(
        Dictionary<string, McpClientTool> tools,
        string toolName,
        string name,
        string? parentId,
        CancellationToken cancellationToken)
    {
        var arguments = await CreateLiveChannelArgumentsAsync(tools, toolName, name, parentId, cancellationToken);
        using var created = await CallMutationSucceededAsync(tools[toolName], arguments, cancellationToken);
        return created.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
    }

    private static async Task<Dictionary<string, object?>> CreateLiveChannelArgumentsAsync(
        Dictionary<string, McpClientTool> tools,
        string toolName,
        string name,
        string? parentId,
        CancellationToken cancellationToken)
    {
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, cancellationToken);
        string? parentDigest = null;
        if (parentId is not null)
        {
            using var parent = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = parentId },
                cancellationToken);
            parentDigest = parent.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString();
        }

        var isVoice = toolName is "create_voice_channel" or "create_stage_channel";
        var isTextLike = toolName is "create_text_channel" or "create_announcement_channel" or "create_forum_channel" or "create_media_channel";
        var isForumLike = toolName is "create_forum_channel" or "create_media_channel";
        var input = new Dictionary<string, object?>
        {
            ["expectedChannelsStateDigest"] = snapshot.RootElement.GetProperty("data").GetProperty("channelsStateDigest").GetString(),
            ["expectedParentStateDigest"] = parentDigest,
            ["name"] = name,
            ["position"] = null,
            ["parentId"] = parentId,
            ["topic"] = isTextLike ? "Disposable Discord Steward channel variant" : null,
            ["isNsfw"] = isTextLike ? false : null,
            ["slowModeSeconds"] = isTextLike ? 0 : null,
            ["bitrate"] = isVoice ? 64_000 : null,
            ["userLimit"] = isVoice ? 0 : null,
            ["rtcRegion"] = null,
            ["videoQualityMode"] = isVoice ? 1 : null,
            ["defaultAutoArchiveMinutes"] = isTextLike ? 1_440 : null,
            ["defaultThreadSlowModeSeconds"] = isForumLike ? 0 : null,
            ["defaultSortOrder"] = null,
            ["defaultForumLayout"] = null,
            ["roleOverwrites"] = Array.Empty<object>()
        };
        return new Dictionary<string, object?>
        {
            ["request_id"] = Guid.NewGuid().ToString("D"),
            ["reason"] = $"Disposable live {toolName} fixture",
            ["input"] = input
        };
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedChannelVariantsContinuation_ReordersRefusesAndCleansExactFixtures()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        var activeCategoryId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_ACTIVE_CATEGORY_ID");
        var archiveCategoryId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_ARCHIVE_CATEGORY_ID");
        var textChannelId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_TEXT_CHANNEL_ID");
        var voiceChannelId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_VOICE_CHANNEL_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(activeCategoryId) ||
            string.IsNullOrWhiteSpace(archiveCategoryId) ||
            string.IsNullOrWhiteSpace(textChannelId) ||
            string.IsNullOrWhiteSpace(voiceChannelId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_channel",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "reorder_channels",
            "follow_announcement_channel",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var snapshotData = snapshot.RootElement.GetProperty("data");
        var siblingIds = snapshotData.GetProperty("channels").EnumerateArray()
            .Where(channel => channel.TryGetProperty("categoryId", out var categoryId) && categoryId.GetString() == activeCategoryId)
            .Select(channel => channel.GetProperty("id").GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { textChannelId, voiceChannelId }.Order(StringComparer.Ordinal), siblingIds);

        using var reordered = await CallMutationSucceededAsync(
            tools["reorder_channels"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live sibling reorder continuation",
                ["input"] = new
                {
                    expectedChannelsStateDigest = snapshotData.GetProperty("channelsStateDigest").GetString(),
                    parentId = activeCategoryId,
                    positions = new[]
                    {
                        new { channelId = textChannelId, position = 0 },
                        new { channelId = voiceChannelId, position = 1 }
                    }
                }
            },
            timeout.Token);

        using var text = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = textChannelId },
            timeout.Token);
        var textDigest = text.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString();
        using var invalidFollow = await CallMutationErrorAsync(
            tools["follow_announcement_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Live non-announcement follow refusal",
                ["input"] = new
                {
                    sourceChannelId = textChannelId,
                    expectedSourceStateDigest = textDigest,
                    destinationChannelId = textChannelId,
                    expectedDestinationStateDigest = textDigest
                }
            },
            "invalid_argument",
            timeout.Token);

        await DeleteChannelFixtureAsync(tools, textChannelId, timeout.Token);
        await DeleteChannelFixtureAsync(tools, voiceChannelId, timeout.Token);
        await DeleteChannelFixtureAsync(tools, activeCategoryId, timeout.Token);
        await DeleteChannelFixtureAsync(tools, archiveCategoryId, timeout.Token);
        output.WriteLine("Channel continuation reordered and cleaned exact fixtures {0}, {1}, {2}, {3}.", textChannelId, voiceChannelId, activeCategoryId, archiveCategoryId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedThreadLifecycle_UpdatesMembershipArchivesRestoresAndDeletesFixture()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_bot_effective_permissions",
            "get_channel",
            "get_thread",
            "list_active_threads",
            "list_archived_threads",
            "list_thread_members",
            "get_thread_member",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_text_channel",
            "create_thread",
            "create_forum_post",
            "update_thread",
            "set_thread_member",
            "delete_thread",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parentId = await CreateLiveChannelAsync(
            tools,
            "create_text_channel",
            $"ds-live-thread-parent-{suffix}",
            parentId: null,
            timeout.Token);
        using var parent = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = parentId },
            timeout.Token);
        var parentData = parent.RootElement.GetProperty("data");
        using var created = await CallMutationSucceededAsync(
            tools["create_thread"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live public thread create",
                ["input"] = new
                {
                    parentChannelId = parentId,
                    expectedParentStateDigest = parentData.GetProperty("mutationStateDigest").GetString(),
                    name = $"ds-live-thread-{suffix}",
                    threadType = "public",
                    sourceMessageId = (string?)null,
                    autoArchiveMinutes = 60,
                    slowModeSeconds = 0,
                    isInvitable = (bool?)null
                }
            },
            timeout.Token);
        var threadId = created.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;

        using var activeThreads = await CallOkAsync(tools["list_active_threads"], null, timeout.Token);
        Assert.Contains(activeThreads.RootElement.GetProperty("data").EnumerateArray(), thread => thread.GetProperty("id").GetString() == threadId);
        using var thread = await CallOkAsync(
            tools["get_thread"],
            new Dictionary<string, object?> { ["thread_id"] = threadId },
            timeout.Token);
        var threadDigest = thread.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var updated = await CallMutationSucceededAsync(
            tools["update_thread"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["thread_id"] = threadId,
                ["reason"] = "Disposable live thread update",
                ["input"] = new
                {
                    expectedStateDigest = threadDigest,
                    set = new
                    {
                        name = $"ds-live-thread-updated-{suffix}",
                        isArchived = (bool?)null,
                        isLocked = (bool?)null,
                        autoArchiveMinutes = 1440,
                        slowModeSeconds = 2,
                        appliedTagIds = (string[]?)null,
                        isInvitable = (bool?)null,
                        isPinned = (bool?)null
                    }
                }
            },
            timeout.Token);

        using var botPermissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
        var botUserId = botPermissions.RootElement.GetProperty("data").GetProperty("observed").GetProperty("botUserId").GetString()!;
        using var updatedThread = await CallOkAsync(
            tools["get_thread"],
            new Dictionary<string, object?> { ["thread_id"] = threadId },
            timeout.Token);
        threadDigest = updatedThread.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var removedSelf = await CallMutationSucceededAsync(
            tools["set_thread_member"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live thread self membership remove",
                ["input"] = new
                {
                    threadId,
                    expectedThreadStateDigest = threadDigest,
                    userId = botUserId,
                    present = false
                }
            },
            timeout.Token);
        using var threadAfterRemoval = await CallOkAsync(
            tools["get_thread"],
            new Dictionary<string, object?> { ["thread_id"] = threadId },
            timeout.Token);
        threadDigest = threadAfterRemoval.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var addedSelf = await CallMutationSucceededAsync(
            tools["set_thread_member"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live thread self membership add",
                ["input"] = new
                {
                    threadId,
                    expectedThreadStateDigest = threadDigest,
                    userId = botUserId,
                    present = true
                }
            },
            timeout.Token);
        using var members = await CallOkAsync(
            tools["list_thread_members"],
            new Dictionary<string, object?> { ["thread_id"] = threadId, ["limit"] = 10 },
            timeout.Token);
        Assert.Contains(members.RootElement.GetProperty("data").GetProperty("items").EnumerateArray(), member => member.GetProperty("userId").GetString() == botUserId);
        using var exactMember = await CallOkAsync(
            tools["get_thread_member"],
            new Dictionary<string, object?> { ["thread_id"] = threadId, ["user_id"] = botUserId },
            timeout.Token);
        Assert.Equal(botUserId, exactMember.RootElement.GetProperty("data").GetProperty("userId").GetString());

        using var threadBeforeArchive = await CallOkAsync(
            tools["get_thread"],
            new Dictionary<string, object?> { ["thread_id"] = threadId },
            timeout.Token);
        using var archived = await CallMutationSucceededAsync(
            tools["update_thread"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["thread_id"] = threadId,
                ["reason"] = "Disposable live thread archive",
                ["input"] = new
                {
                    expectedStateDigest = threadBeforeArchive.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                    set = new { isArchived = true }
                }
            },
            timeout.Token);
        using var archivedThreads = await CallOkAsync(
            tools["list_archived_threads"],
            new Dictionary<string, object?>
            {
                ["parent_channel_id"] = parentId,
                ["archive_type"] = "public",
                ["limit"] = 10
            },
            timeout.Token);
        Assert.Contains(archivedThreads.RootElement.GetProperty("data").EnumerateArray(), item => item.GetProperty("id").GetString() == threadId);
        using var archivedThread = await CallOkAsync(
            tools["get_thread"],
            new Dictionary<string, object?> { ["thread_id"] = threadId },
            timeout.Token);
        using var restored = await CallMutationSucceededAsync(
            tools["update_thread"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["thread_id"] = threadId,
                ["reason"] = "Disposable live thread restore",
                ["input"] = new
                {
                    expectedStateDigest = archivedThread.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                    set = new { isArchived = false, isLocked = false }
                }
            },
            timeout.Token);

        using var parentForForumRefusal = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = parentId },
            timeout.Token);
        var parentForForumData = parentForForumRefusal.RootElement.GetProperty("data");
        using var forumRefusal = await CallMutationErrorAsync(
            tools["create_forum_post"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Live forum-post parent type refusal",
                ["input"] = new
                {
                    parentChannelId = parentId,
                    expectedParentStateDigest = parentForForumData.GetProperty("mutationStateDigest").GetString(),
                    expectedAvailableTagsDigest = parentForForumData.GetProperty("availableTagsDigest").GetString(),
                    name = $"ds-live-forum-refusal-{suffix}",
                    content = "This must not be written to a text channel.",
                    appliedTagIds = Array.Empty<string>(),
                    autoArchiveMinutes = 60,
                    slowModeSeconds = 0
                }
            },
            "invalid_argument",
            timeout.Token);

        using var finalThread = await CallOkAsync(
            tools["get_thread"],
            new Dictionary<string, object?> { ["thread_id"] = threadId },
            timeout.Token);
        using var deleted = await CallMutationSucceededAsync(
            tools["delete_thread"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["thread_id"] = threadId,
                ["reason"] = "Disposable live thread cleanup",
                ["input"] = new
                {
                    expectedStateDigest = finalThread.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                }
            },
            timeout.Token);
        await DeleteChannelFixtureAsync(tools, parentId, timeout.Token);
        output.WriteLine("Thread lifecycle passed and deleted thread {0} plus parent {1}.", threadId, parentId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedGuildSettingsLifecycle_MutatesAndRestoresCommunitySettings()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild",
            "get_guild_configuration_snapshot",
            "get_channel",
            "get_guild_widget",
            "get_guild_widget_image",
            "get_welcome_screen",
            "get_onboarding",
            "get_incident_status",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_text_channel",
            "update_guild_metadata",
            "update_guild_system_routing",
            "update_guild_safety",
            "update_guild_access_controls",
            "update_guild_widget",
            "update_welcome_screen",
            "update_onboarding",
            "set_incident_actions",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var originalGuild = await ReadLiveGuildStateAsync(tools, timeout.Token);
        var originalAccess = await ReadLiveGuildAccessStateAsync(tools, timeout.Token);
        var originalWidget = await ReadLiveWidgetStateAsync(tools, timeout.Token);
        var originalWelcome = await ReadLiveWelcomeStateAsync(tools, timeout.Token);
        var originalOnboarding = await ReadLiveOnboardingStateAsync(tools, timeout.Token);
        Assert.Equal(0, originalWelcome.ChannelCount);
        Assert.Equal(0, originalOnboarding.PromptCount);
        Assert.Equal(0, originalOnboarding.DefaultChannelCount);
        var originalIncident = await ReadLiveIncidentStateAsync(tools, timeout.Token);
        string? widgetChannelId = null;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        try
        {
            widgetChannelId = await CreateLiveChannelAsync(
                tools,
                "create_text_channel",
                $"ds-live-widget-{suffix}",
                parentId: null,
                timeout.Token);

            await SetGuildNameAsync(tools, originalGuild.Digest, $"DiscSteward Test Server {suffix}", timeout.Token);
            var changedGuild = await ReadLiveGuildStateAsync(tools, timeout.Token);
            Assert.Equal($"DiscSteward Test Server {suffix}", changedGuild.Name);
            await SetGuildNameAsync(tools, changedGuild.Digest, originalGuild.Name, timeout.Token);

            changedGuild = await ReadLiveGuildStateAsync(tools, timeout.Token);
            var temporaryAfkTimeout = originalGuild.AfkTimeoutSeconds == 60 ? 300 : 60;
            await SetGuildRoutingAsync(tools, changedGuild.Digest, originalGuild with { AfkTimeoutSeconds = temporaryAfkTimeout }, timeout.Token);
            changedGuild = await ReadLiveGuildStateAsync(tools, timeout.Token);
            Assert.Equal(temporaryAfkTimeout, changedGuild.AfkTimeoutSeconds);
            await SetGuildRoutingAsync(tools, changedGuild.Digest, originalGuild, timeout.Token);

            changedGuild = await ReadLiveGuildStateAsync(tools, timeout.Token);
            var temporaryNotifications = originalGuild.DefaultMessageNotifications == 0 ? 1 : 0;
            await SetGuildSafetyAsync(
                tools,
                changedGuild.Digest,
                originalGuild.VerificationLevel,
                temporaryNotifications,
                originalGuild.ExplicitContentFilter,
                timeout.Token);
            changedGuild = await ReadLiveGuildStateAsync(tools, timeout.Token);
            Assert.Equal(temporaryNotifications, changedGuild.DefaultMessageNotifications);
            await SetGuildSafetyAsync(
                tools,
                changedGuild.Digest,
                originalGuild.VerificationLevel,
                originalGuild.DefaultMessageNotifications,
                originalGuild.ExplicitContentFilter,
                timeout.Token);

            var accessBeforeUpdate = await ReadLiveGuildAccessStateAsync(tools, timeout.Token);
            Assert.Equal(originalAccess.InvitesDisabled, accessBeforeUpdate.InvitesDisabled);
            Assert.Equal(originalAccess.RaidAlertsDisabled, accessBeforeUpdate.RaidAlertsDisabled);
            await SetGuildAccessControlsAsync(
                tools,
                accessBeforeUpdate.Digest,
                !originalAccess.InvitesDisabled,
                !originalAccess.RaidAlertsDisabled,
                timeout.Token);
            var changedAccess = await ReadLiveGuildAccessStateAsync(tools, timeout.Token);
            Assert.Equal(!originalAccess.InvitesDisabled, changedAccess.InvitesDisabled);
            Assert.Equal(!originalAccess.RaidAlertsDisabled, changedAccess.RaidAlertsDisabled);
            await SetGuildAccessControlsAsync(
                tools,
                changedAccess.Digest,
                originalAccess.InvitesDisabled,
                originalAccess.RaidAlertsDisabled,
                timeout.Token);

            var temporaryWidgetEnabled = !originalWidget.Enabled;
            if (originalWidget.Enabled)
            {
                using var originalImage = await CallOkAsync(tools["get_guild_widget_image"], null, timeout.Token);
                Assert.True(originalImage.RootElement.GetProperty("data").GetProperty("byteLength").GetInt32() > 0);
            }

            await SetGuildWidgetAsync(
                tools,
                originalWidget.Digest,
                temporaryWidgetEnabled,
                temporaryWidgetEnabled ? widgetChannelId : null,
                timeout.Token);
            var changedWidget = await ReadLiveWidgetStateAsync(tools, timeout.Token);
            Assert.Equal(temporaryWidgetEnabled, changedWidget.Enabled);
            if (changedWidget.Enabled)
            {
                using var enabledImage = await CallOkAsync(tools["get_guild_widget_image"], null, timeout.Token);
                Assert.True(enabledImage.RootElement.GetProperty("data").GetProperty("byteLength").GetInt32() > 0);
            }

            await SetGuildWidgetAsync(
                tools,
                changedWidget.Digest,
                originalWidget.Enabled,
                originalWidget.ChannelId,
                timeout.Token);

            await SetWelcomeScreenAsync(
                tools,
                originalWelcome.Digest,
                enabled: true,
                description: "Disposable Discord Steward welcome screen",
                channels:
                [
                    new
                    {
                        channelId = widgetChannelId,
                        description = "Disposable live validation channel",
                        emojiId = (string?)null,
                        emojiName = "✅"
                    }
                ],
                timeout.Token);
            var changedWelcome = await ReadLiveWelcomeStateAsync(tools, timeout.Token);
            Assert.True(changedWelcome.Enabled);
            Assert.Equal(1, changedWelcome.ChannelCount);
            await SetWelcomeScreenAsync(
                tools,
                changedWelcome.Digest,
                originalWelcome.Enabled,
                originalWelcome.Description,
                Array.Empty<object>(),
                timeout.Token);

            await SetOnboardingAsync(
                tools,
                originalOnboarding.Digest,
                prompts: Array.Empty<object>(),
                defaultChannelIds: Array.Empty<string>(),
                enabled: false,
                mode: originalOnboarding.Mode.Contains("Advanced", StringComparison.OrdinalIgnoreCase) ? "default" : "advanced",
                timeout.Token);
            var changedOnboarding = await ReadLiveOnboardingStateAsync(tools, timeout.Token);
            Assert.False(changedOnboarding.Enabled);
            Assert.Equal(0, changedOnboarding.PromptCount);
            Assert.Equal(0, changedOnboarding.DefaultChannelCount);
            Assert.NotEqual(originalOnboarding.Mode, changedOnboarding.Mode);
            await SetOnboardingAsync(
                tools,
                changedOnboarding.Digest,
                Array.Empty<object>(),
                Array.Empty<string>(),
                originalOnboarding.Enabled,
                originalOnboarding.Mode,
                timeout.Token);

            var temporaryInvitesDisabledUntil = originalIncident.InvitesDisabledUntil.HasValue
                ? (DateTimeOffset?)null
                : DateTimeOffset.UtcNow.AddMinutes(2);
            await SetIncidentActionsAsync(
                tools,
                originalIncident.Digest,
                temporaryInvitesDisabledUntil,
                originalIncident.DirectMessagesDisabledUntil,
                timeout.Token);
            var changedIncident = await ReadLiveIncidentStateAsync(tools, timeout.Token);
            Assert.Equal(temporaryInvitesDisabledUntil.HasValue, changedIncident.InvitesDisabledUntil.HasValue);
            await SetIncidentActionsAsync(
                tools,
                changedIncident.Digest,
                originalIncident.InvitesDisabledUntil,
                originalIncident.DirectMessagesDisabledUntil,
                timeout.Token);
        }
        finally
        {
            await RestoreGuildSettingsAsync(
                tools,
                originalGuild,
                originalAccess,
                originalWidget,
                originalWelcome,
                originalOnboarding,
                originalIncident,
                timeout.Token);
            if (widgetChannelId is not null)
            {
                await DeleteChannelFixtureAsync(tools, widgetChannelId, timeout.Token);
            }
        }

        output.WriteLine("Guild metadata, routing, safety, widget, welcome, onboarding, and incident actions passed with original state restored.");
    }

    [Fact]
    [Trait("Category", "LiveDiscord")]
    public async Task AuthorizedCommunityReads_ReturnExactWelcomeAndOnboardingState()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names = ["get_guild", "get_welcome_screen", "get_onboarding"];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(guildId, timeout.Token, names, sensitiveReads: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var guild = await CallOkAsync(tools["get_guild"], null, timeout.Token);
        Assert.Contains(
            guild.RootElement.GetProperty("data").GetProperty("features").EnumerateArray(),
            feature => string.Equals(feature.GetString(), "COMMUNITY", StringComparison.OrdinalIgnoreCase));
        using var welcome = await CallOkAsync(tools["get_welcome_screen"], null, timeout.Token);
        using var onboarding = await CallOkAsync(tools["get_onboarding"], null, timeout.Token);
        output.WriteLine(
            "Community reads passed: welcome enabled={0}, channels={1}; onboarding enabled={2}, prompts={3}, defaults={4}.",
            welcome.RootElement.GetProperty("data").GetProperty("isEnabled").GetBoolean(),
            welcome.RootElement.GetProperty("data").GetProperty("channels").GetArrayLength(),
            onboarding.RootElement.GetProperty("data").GetProperty("isEnabled").GetBoolean(),
            onboarding.RootElement.GetProperty("data").GetProperty("prompts").GetArrayLength(),
            onboarding.RootElement.GetProperty("data").GetProperty("defaultChannelIds").GetArrayLength());
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedIncidentActionsLifecycle_NormalizesAndRestoresTimestamps()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names = ["get_incident_status", "get_operation", "reconcile_operation", "set_incident_actions"];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var original = await ReadLiveIncidentStateAsync(tools, timeout.Token);
        try
        {
            var requested = DateTimeOffset.UtcNow.AddMinutes(2);
            await SetIncidentActionsAsync(
                tools,
                original.Digest,
                requested,
                original.DirectMessagesDisabledUntil,
                timeout.Token);
            var changed = await ReadLiveIncidentStateAsync(tools, timeout.Token);
            Assert.Equal(requested.ToUniversalTime().Ticks - requested.ToUniversalTime().Ticks % 10, changed.InvitesDisabledUntil?.Ticks);
        }
        finally
        {
            var current = await ReadLiveIncidentStateAsync(tools, timeout.Token);
            if (current.InvitesDisabledUntil != original.InvitesDisabledUntil ||
                current.DirectMessagesDisabledUntil != original.DirectMessagesDisabledUntil)
            {
                await SetIncidentActionsAsync(
                    tools,
                    current.Digest,
                    original.InvitesDisabledUntil,
                    original.DirectMessagesDisabledUntil,
                    timeout.Token);
            }
        }

        output.WriteLine("Incident action timestamp normalized to Discord precision and original state was restored.");
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedInviteAndWebhookLifecycle_CreatesMessagesUpdatesAndCleansAllFixtures()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_channel",
            "get_message",
            "get_invite",
            "list_invites",
            "get_webhook",
            "list_webhooks",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_text_channel",
            "create_invite",
            "delete_invite",
            "create_webhook",
            "update_webhook",
            "send_webhook_message",
            "edit_webhook_message",
            "delete_webhook_message",
            "delete_webhook",
            "delete_channel"
        ];
        var localRoot = Path.Combine(Path.GetTempPath(), $"discord-steward-live-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(localRoot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        string? channelId = null;
        string? inviteCode = null;
        string? webhookId = null;
        string? storedWebhookId = null;
        try
        {
            await using var client = await CreateClientAsync(
                guildId,
                timeout.Token,
                names,
                sensitiveReads: true,
                allWriteGates: true,
                localDataRoot: localRoot);
            var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
            var suffix = Guid.NewGuid().ToString("N")[..8];
            channelId = await CreateLiveChannelAsync(
                tools,
                "create_text_channel",
                $"ds-live-integrations-{suffix}",
                parentId: null,
                timeout.Token);
            using var channel = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = channelId },
                timeout.Token);
            var channelDigest = channel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!;
            using var invite = await CallMutationSucceededAsync(
                tools["create_invite"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable live invite create",
                    ["input"] = new
                    {
                        expectedChannelStateDigest = channelDigest,
                        channelId,
                        maxAgeSeconds = 600,
                        maxUses = 1,
                        temporary = false,
                        unique = true,
                        targetType = (string?)null,
                        targetUserId = (string?)null,
                        targetApplicationId = (string?)null,
                        roleIds = Array.Empty<string>()
                    }
                },
                timeout.Token);
            inviteCode = invite.RootElement.GetProperty("data").GetProperty("observed").GetProperty("code").GetString()!;
            using var exactInvite = await CallOkAsync(
                tools["get_invite"],
                new Dictionary<string, object?> { ["code"] = inviteCode },
                timeout.Token);
            using var listedInvites = await CallOkAsync(tools["list_invites"], null, timeout.Token);
            Assert.Contains(listedInvites.RootElement.GetProperty("data").EnumerateArray(), item => item.GetProperty("code").GetString() == inviteCode);
            using var deletedInvite = await CallMutationSucceededAsync(
                tools["delete_invite"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["code"] = inviteCode,
                    ["reason"] = "Disposable live invite cleanup",
                    ["input"] = new
                    {
                        expectedStateDigest = exactInvite.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                    }
                },
                timeout.Token);
            inviteCode = null;

            using var createdWebhook = await CallMutationSucceededAsync(
                tools["create_webhook"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable live webhook create",
                    ["input"] = new
                    {
                        expectedChannelStateDigest = channelDigest,
                        channelId,
                        name = $"Steward Hook {suffix}",
                        avatarAssetId = (string?)null
                    }
                },
                timeout.Token);
            var storedObservation = createdWebhook.RootElement.GetProperty("data").GetProperty("observed");
            storedWebhookId = storedObservation.GetProperty("storedWebhookId").GetString()!;
            webhookId = storedObservation.GetProperty("webhook").GetProperty("id").GetString()!;
            using var webhook = await CallOkAsync(
                tools["get_webhook"],
                new Dictionary<string, object?> { ["webhook_id"] = webhookId },
                timeout.Token);
            using var listedWebhooks = await CallOkAsync(tools["list_webhooks"], null, timeout.Token);
            Assert.Contains(listedWebhooks.RootElement.GetProperty("data").EnumerateArray(), item => item.GetProperty("id").GetString() == webhookId);
            using var updatedWebhook = await CallMutationSucceededAsync(
                tools["update_webhook"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["webhook_id"] = webhookId,
                    ["reason"] = "Disposable live webhook update",
                    ["input"] = new
                    {
                        expectedStateDigest = webhook.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                        storedWebhookId,
                        name = $"Steward Hook Updated {suffix}",
                        avatarAssetId = (string?)null,
                        channelId = (string?)null,
                        clear = Array.Empty<string>()
                    }
                },
                timeout.Token);
            using var currentWebhook = await CallOkAsync(
                tools["get_webhook"],
                new Dictionary<string, object?> { ["webhook_id"] = webhookId },
                timeout.Token);
            var webhookDigest = currentWebhook.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
            var allowedMentions = new
            {
                userIds = Array.Empty<string>(),
                roleIds = Array.Empty<string>(),
                everyone = false,
                repliedUser = false
            };
            using var sent = await CallMutationSucceededAsync(
                tools["send_webhook_message"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable live webhook message send",
                    ["input"] = new
                    {
                        storedWebhookId,
                        expectedWebhookStateDigest = webhookDigest,
                        threadId = (string?)null,
                        content = $"Disposable webhook message {suffix}",
                        allowedMentions,
                        assetIds = Array.Empty<string>(),
                        usernameOverride = $"Steward Live {suffix}",
                        avatarUrlOverride = (string?)null,
                        tts = false,
                        embeds = new[]
                        {
                            new { title = "Webhook live validation", description = $"Webhook embed {suffix}" }
                        },
                        poll = new
                        {
                            question = "Did the webhook rich payload work?",
                            answers = new[]
                            {
                                new { text = "Yes", emojiId = (string?)null, emojiName = (string?)null },
                                new { text = "Also yes", emojiId = (string?)null, emojiName = (string?)null }
                            },
                            durationHours = 1,
                            allowMultiselect = false
                        },
                        suppressNotifications = true,
                        suppressEmbeds = false
                    }
                },
                timeout.Token);
            var sentData = sent.RootElement.GetProperty("data").GetProperty("observed");
            var messageId = sentData.GetProperty("messageId").GetString()!;
            using var sentMessage = await CallOkAsync(
                tools["get_message"],
                new Dictionary<string, object?> { ["channel_id"] = channelId, ["message_id"] = messageId },
                timeout.Token);
            var sentMessageData = sentMessage.RootElement.GetProperty("data");
            Assert.Equal($"Steward Live {suffix}", sentMessageData.GetProperty("authorName").GetString());
            Assert.NotEqual("[]", sentMessageData.GetProperty("embedsJson").GetString());
            Assert.Equal("Did the webhook rich payload work?", sentMessageData.GetProperty("poll").GetProperty("question").GetString());
            using var edited = await CallMutationSucceededAsync(
                tools["edit_webhook_message"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable live webhook message edit",
                    ["input"] = new
                    {
                        storedWebhookId,
                        expectedWebhookStateDigest = webhookDigest,
                        messageId,
                        expectedMessageStateDigest = sentMessage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                        threadId = (string?)null,
                        content = $"Edited disposable webhook message {suffix}",
                        allowedMentions,
                        retainedAttachmentIds = Array.Empty<string>(),
                        newAssetIds = Array.Empty<string>(),
                        embeds = new[]
                        {
                            new { title = "Edited webhook rich payload", description = $"Webhook edit {suffix}" }
                        },
                        suppressEmbeds = false
                    }
                },
                timeout.Token);
            using var editedMessage = await CallOkAsync(
                tools["get_message"],
                new Dictionary<string, object?> { ["channel_id"] = channelId, ["message_id"] = messageId },
                timeout.Token);
            Assert.NotEqual("[]", editedMessage.RootElement.GetProperty("data").GetProperty("embedsJson").GetString());
            using var deletedMessage = await CallMutationSucceededAsync(
                tools["delete_webhook_message"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable live webhook message cleanup",
                    ["input"] = new
                    {
                        storedWebhookId,
                        expectedWebhookStateDigest = webhookDigest,
                        messageId,
                        expectedMessageStateDigest = editedMessage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                        threadId = (string?)null
                    }
                },
                timeout.Token);
            using var webhookBeforeDelete = await CallOkAsync(
                tools["get_webhook"],
                new Dictionary<string, object?> { ["webhook_id"] = webhookId },
                timeout.Token);
            using var deletedWebhook = await CallMutationSucceededAsync(
                tools["delete_webhook"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["webhook_id"] = webhookId,
                    ["reason"] = "Disposable live webhook cleanup",
                    ["input"] = new
                    {
                        expectedStateDigest = webhookBeforeDelete.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                        storedWebhookId
                    }
                },
                timeout.Token);
            webhookId = null;
            storedWebhookId = null;
            await DeleteChannelFixtureAsync(tools, channelId, timeout.Token);
            channelId = null;
            output.WriteLine("Invite and webhook lifecycles passed with all Discord and vault fixtures cleaned.");
        }
        finally
        {
            if (webhookId is not null || inviteCode is not null || channelId is not null)
            {
                await using var cleanupClient = await CreateClientAsync(
                    guildId,
                    timeout.Token,
                    names,
                    sensitiveReads: true,
                    allWriteGates: true,
                    localDataRoot: localRoot);
                var cleanupTools = (await cleanupClient.ListToolsAsync(cancellationToken: timeout.Token))
                    .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
                if (webhookId is not null)
                {
                    await DeleteWebhookFixtureAsync(cleanupTools, webhookId, storedWebhookId, timeout.Token);
                }

                if (inviteCode is not null)
                {
                    await DeleteInviteFixtureAsync(cleanupTools, inviteCode, timeout.Token);
                }

                if (channelId is not null)
                {
                    await DeleteChannelFixtureAsync(cleanupTools, channelId, timeout.Token);
                }
            }

            if (Directory.Exists(localRoot))
            {
                Directory.Delete(localRoot, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedGuildTemplateEndpoints_RejectBotCredentialWithoutUnknownOutcomes()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "list_guild_templates",
            "get_guild_template",
            "get_operation",
            "reconcile_operation",
            "create_guild_template",
            "sync_guild_template",
            "update_guild_template",
            "delete_guild_template"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var missingCode = $"missing{suffix}";
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        using var listed = await CallAsync(tools["list_guild_templates"], null, timeout.Token);
        Assert.Equal("discord_permission_denied", listed.RootElement.GetProperty("error").GetProperty("code").GetString());
        using var missing = await CallAsync(
            tools["get_guild_template"],
            new Dictionary<string, object?> { ["code"] = missingCode },
            timeout.Token);
        Assert.Equal("resource_not_found", missing.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var createRejected = await CallMutationErrorAsync(
            tools["create_guild_template"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Live bot-credential template rejection",
                ["input"] = new
                {
                    expectedConfigurationStateDigest = snapshot.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                    name = $"Steward Template {suffix}",
                    description = "This bot-token request must be rejected"
                }
            },
            "discord_permission_denied",
            timeout.Token);
        using var syncRejected = await CallMutationErrorAsync(
            tools["sync_guild_template"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["code"] = missingCode,
                ["reason"] = "Live missing template sync refusal",
                ["input"] = new
                {
                    expectedConfigurationStateDigest = snapshot.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                    expectedTemplateStateDigest = new string('a', 64)
                }
            },
            "resource_not_found",
            timeout.Token);
        using var updateRejected = await CallMutationErrorAsync(
            tools["update_guild_template"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["code"] = missingCode,
                ["reason"] = "Live missing template update refusal",
                ["input"] = new
                {
                    expectedTemplateStateDigest = new string('a', 64),
                    name = "Missing template",
                    description = (string?)null,
                    clear = Array.Empty<string>()
                }
            },
            "resource_not_found",
            timeout.Token);
        using var deleteRejected = await CallMutationErrorAsync(
            tools["delete_guild_template"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["code"] = missingCode,
                ["reason"] = "Live missing template delete refusal",
                ["input"] = new { expectedTemplateStateDigest = new string('a', 64) }
            },
            "resource_not_found",
            timeout.Token);
        output.WriteLine("Guild template endpoints returned deterministic bot-credential and missing-resource failures without unknown outcomes.");
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedTargetedInviteLifecycle_UsesOnlyBotUserAndCleansFixture()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_bot_effective_permissions",
            "get_channel",
            "get_invite",
            "list_invite_target_users",
            "get_invite_target_user_job_status",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_text_channel",
            "create_targeted_invite",
            "update_invite_target_users",
            "delete_invite",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string? channelId = null;
        string? inviteCode = null;
        try
        {
            channelId = await CreateLiveChannelAsync(
                tools,
                "create_text_channel",
                $"ds-live-targeted-invite-{suffix}",
                parentId: null,
                timeout.Token);
            using var channel = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = channelId },
                timeout.Token);
            using var botPermissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
            var botUserId = botPermissions.RootElement.GetProperty("data").GetProperty("observed").GetProperty("botUserId").GetString()!;
            var createRequestId = Guid.NewGuid().ToString("D");
            using var created = await CallMutationSucceededAsync(
                tools["create_targeted_invite"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = createRequestId,
                    ["reason"] = "Disposable bot-only targeted invite create",
                    ["input"] = new
                    {
                        invite = new
                        {
                            expectedChannelStateDigest = channel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                            channelId,
                            maxAgeSeconds = 600,
                            maxUses = 1,
                            temporary = false,
                            unique = true,
                            targetType = (string?)null,
                            targetUserId = (string?)null,
                            targetApplicationId = (string?)null,
                            roleIds = Array.Empty<string>()
                        },
                        userIds = new[] { botUserId }
                    }
                },
                timeout.Token);
            inviteCode = created.RootElement.GetProperty("data").GetProperty("observed").GetProperty("code").GetString()!;
            using var job = await CallOkAsync(
                tools["get_invite_target_user_job_status"],
                new Dictionary<string, object?> { ["code"] = inviteCode, ["request_id"] = createRequestId },
                timeout.Token);
            Assert.Contains(job.RootElement.GetProperty("data").GetProperty("status").GetString(), AcceptedInviteJobStatuses);

            using var targets = await CallAsync(
                tools["list_invite_target_users"],
                new Dictionary<string, object?> { ["code"] = inviteCode },
                timeout.Token);
            if (targets.RootElement.GetProperty("outcome").GetString() == "ok")
            {
                var targetData = targets.RootElement.GetProperty("data");
                Assert.Contains(botUserId, targetData.GetProperty("userIds").EnumerateArray().Select(item => item.GetString()));
                var updateRequestId = Guid.NewGuid().ToString("D");
                using var updated = await CallMutationSucceededAsync(
                    tools["update_invite_target_users"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = updateRequestId,
                        ["code"] = inviteCode,
                        ["reason"] = "Disposable bot-only targeted invite replacement",
                        ["input"] = new
                        {
                            expectedTargetUsersDigest = targetData.GetProperty("stateDigest").GetString(),
                            userIds = new[] { botUserId }
                        }
                    },
                    timeout.Token);
                using var updateJob = await CallOkAsync(
                    tools["get_invite_target_user_job_status"],
                    new Dictionary<string, object?> { ["code"] = inviteCode, ["request_id"] = updateRequestId },
                    timeout.Token);
                Assert.Contains(updateJob.RootElement.GetProperty("data").GetProperty("status").GetString(), AcceptedInviteJobStatuses);
            }

            await DeleteInviteFixtureAsync(tools, inviteCode, timeout.Token);
            inviteCode = null;
            await DeleteChannelFixtureAsync(tools, channelId, timeout.Token);
            channelId = null;
            output.WriteLine("Bot-only targeted invite lifecycle passed and cleaned its invite and channel.");
        }
        finally
        {
            if (inviteCode is not null)
            {
                await DeleteInviteFixtureAsync(tools, inviteCode, timeout.Token);
            }

            if (channelId is not null)
            {
                await DeleteChannelFixtureAsync(tools, channelId, timeout.Token);
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedCommunityChannelLifecycle_ExercisesAnnouncementForumMediaAndStageTools()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_bot_effective_permissions",
            "get_channel",
            "get_thread",
            "get_stage_instance",
            "get_member",
            "get_message",
            "list_webhooks",
            "get_webhook",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_text_channel",
            "create_announcement_channel",
            "create_stage_channel",
            "create_forum_channel",
            "create_media_channel",
            "follow_announcement_channel",
            "create_forum_post",
            "delete_thread",
            "start_stage_instance",
            "update_stage_instance",
            "end_stage_instance",
            "set_stage_speaker",
            "request_stage_speaking",
            "send_message",
            "crosspost_announcement_message",
            "delete_message",
            "delete_webhook",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string? destinationId = null;
        string? announcementId = null;
        string? stageChannelId = null;
        string? forumId = null;
        string? mediaId = null;
        string? forumThreadId = null;
        string? followerWebhookId = null;
        try
        {
            destinationId = await CreateLiveChannelAsync(
                tools,
                "create_text_channel",
                $"ds-live-follow-destination-{suffix}",
                parentId: null,
                timeout.Token);
            announcementId = await CreateLiveChannelAsync(
                tools,
                "create_announcement_channel",
                $"ds-live-announcement-{suffix}",
                parentId: null,
                timeout.Token);
            stageChannelId = await CreateLiveChannelAsync(
                tools,
                "create_stage_channel",
                $"ds-live-stage-{suffix}",
                parentId: null,
                timeout.Token);
            forumId = await CreateLiveChannelAsync(
                tools,
                "create_forum_channel",
                $"ds-live-forum-{suffix}",
                parentId: null,
                timeout.Token);

            var mediaArguments = await CreateLiveChannelArgumentsAsync(
                tools,
                "create_media_channel",
                $"ds-live-media-{suffix}",
                parentId: null,
                timeout.Token);
            using (var media = await CallAsync(tools["create_media_channel"], mediaArguments, timeout.Token))
            {
                if (media.RootElement.GetProperty("outcome").GetString() == "succeeded")
                {
                    mediaId = media.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString();
                }
                else
                {
                    Assert.Contains(
                        media.RootElement.GetProperty("error").GetProperty("code").GetString(),
                        AcceptedCapabilityErrors);
                }
            }

            using var announcement = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = announcementId },
                timeout.Token);
            using var destination = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = destinationId },
                timeout.Token);
            using var followed = await CallMutationSucceededAsync(
                tools["follow_announcement_channel"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable live announcement follow",
                    ["input"] = new
                    {
                        sourceChannelId = announcementId,
                        expectedSourceStateDigest = announcement.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                        destinationChannelId = destinationId,
                        expectedDestinationStateDigest = destination.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()
                    }
                },
                timeout.Token);
            followerWebhookId = followed.RootElement.GetProperty("data").GetProperty("observed").GetProperty("webhookId").GetString();
            using var webhooks = await CallOkAsync(tools["list_webhooks"], null, timeout.Token);
            Assert.Contains(webhooks.RootElement.GetProperty("data").EnumerateArray(), webhook => webhook.GetProperty("id").GetString() == followerWebhookId);

            var announcementDigest = announcement.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!;
            var allowedMentions = new
            {
                userIds = Array.Empty<string>(),
                roleIds = Array.Empty<string>(),
                everyone = false,
                repliedUser = false
            };
            using var sent = await SendLiveMessageAsync(
                tools,
                announcementId,
                announcementDigest,
                $"Disposable announcement {suffix}",
                $"announce-{suffix}",
                allowedMentions,
                timeout.Token);
            var sentData = sent.RootElement.GetProperty("data").GetProperty("observed");
            var announcementMessageId = sentData.GetProperty("messageId").GetString()!;
            using var crossposted = await CallMutationSucceededAsync(
                tools["crosspost_announcement_message"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = announcementId,
                    ["message_id"] = announcementMessageId,
                    ["reason"] = "Disposable live announcement crosspost",
                    ["input"] = new
                    {
                        expectedMessageStateDigest = sentData.GetProperty("stateDigest").GetString()
                    }
                },
                timeout.Token);
            using var crosspostedMessage = await CallOkAsync(
                tools["get_message"],
                new Dictionary<string, object?> { ["channel_id"] = announcementId, ["message_id"] = announcementMessageId },
                timeout.Token);
            using var deletedAnnouncement = await CallMutationSucceededAsync(
                tools["delete_message"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = announcementId,
                    ["message_id"] = announcementMessageId,
                    ["reason"] = "Disposable live announcement message cleanup",
                    ["input"] = new
                    {
                        expectedMessageStateDigest = crosspostedMessage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                    }
                },
                timeout.Token);

            using var forum = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = forumId },
                timeout.Token);
            var forumData = forum.RootElement.GetProperty("data");
            using var forumPost = await CallMutationSucceededAsync(
                tools["create_forum_post"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable live forum post create",
                    ["input"] = new
                    {
                        parentChannelId = forumId,
                        expectedParentStateDigest = forumData.GetProperty("mutationStateDigest").GetString(),
                        expectedAvailableTagsDigest = forumData.GetProperty("availableTagsDigest").GetString(),
                        name = $"ds-live-forum-post-{suffix}",
                        content = "Disposable Discord Steward forum post",
                        appliedTagIds = Array.Empty<string>(),
                        autoArchiveMinutes = 60,
                        slowModeSeconds = 0
                    }
                },
                timeout.Token);
            forumThreadId = forumPost.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
            using var forumThread = await CallOkAsync(
                tools["get_thread"],
                new Dictionary<string, object?> { ["thread_id"] = forumThreadId },
                timeout.Token);
            using var deletedForumThread = await CallMutationSucceededAsync(
                tools["delete_thread"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["thread_id"] = forumThreadId,
                    ["reason"] = "Disposable live forum post cleanup",
                    ["input"] = new
                    {
                        expectedStateDigest = forumThread.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                    }
                },
                timeout.Token);
            forumThreadId = null;

            using var stageChannel = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = stageChannelId },
                timeout.Token);
            using var stageStart = await CallAsync(
                tools["start_stage_instance"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = stageChannelId,
                    ["reason"] = "Disposable live stage start",
                    ["input"] = new
                    {
                        expectedChannelStateDigest = stageChannel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                        topic = $"Disposable Stage {suffix}",
                        scheduledEventId = (string?)null,
                        sendStartNotification = false
                    }
                },
                timeout.Token);
            if (stageStart.RootElement.GetProperty("outcome").GetString() == "succeeded")
            {
                using var stage = await CallOkAsync(
                    tools["get_stage_instance"],
                    new Dictionary<string, object?> { ["channel_id"] = stageChannelId },
                    timeout.Token);
                var stageDigest = stage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
                using var updateStage = await CallAsync(
                    tools["update_stage_instance"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["channel_id"] = stageChannelId,
                        ["reason"] = "Live stage public-privacy attempt",
                        ["input"] = new { expectedStateDigest = stageDigest, privacyLevel = "public" }
                    },
                    timeout.Token);
                Assert.Contains(updateStage.RootElement.GetProperty("outcome").GetString(), AppliedOrVerifiedNoChangeOutcomes);

                using var botPermissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
                var botUserId = botPermissions.RootElement.GetProperty("data").GetProperty("observed").GetProperty("botUserId").GetString()!;
                using var botMember = await CallOkAsync(
                    tools["get_member"],
                    new Dictionary<string, object?> { ["user_id"] = botUserId },
                    timeout.Token);
                using var currentStage = await CallOkAsync(
                    tools["get_stage_instance"],
                    new Dictionary<string, object?> { ["channel_id"] = stageChannelId },
                    timeout.Token);
                stageDigest = currentStage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
                using var speakerRefusal = await CallMutationErrorAsync(
                    tools["set_stage_speaker"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["channel_id"] = stageChannelId,
                        ["reason"] = "Live disconnected stage speaker refusal",
                        ["input"] = new
                        {
                            expectedStageStateDigest = stageDigest,
                            userId = botUserId,
                            expectedMemberStateDigest = botMember.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                            isSpeaker = true
                        }
                    },
                    "state_conflict",
                    timeout.Token);
                using var requestRefusal = await CallMutationErrorAsync(
                    tools["request_stage_speaking"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["channel_id"] = stageChannelId,
                        ["reason"] = "Live disconnected request-to-speak refusal",
                        ["input"] = new { expectedStageStateDigest = stageDigest, requesting = true }
                    },
                    "state_conflict",
                    timeout.Token);
                using var finalStage = await CallOkAsync(
                    tools["get_stage_instance"],
                    new Dictionary<string, object?> { ["channel_id"] = stageChannelId },
                    timeout.Token);
                using var ended = await CallMutationSucceededAsync(
                    tools["end_stage_instance"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["channel_id"] = stageChannelId,
                        ["reason"] = "Disposable live stage cleanup",
                        ["input"] = new
                        {
                            expectedStateDigest = finalStage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                        }
                    },
                    timeout.Token);
            }
            else
            {
                Assert.Contains(
                    stageStart.RootElement.GetProperty("error").GetProperty("code").GetString(),
                    AcceptedCapabilityErrors);
                foreach (var (toolName, input) in new (string, object)[]
                {
                    ("update_stage_instance", new { expectedStateDigest = new string('a', 64), privacyLevel = "guild_only" }),
                    ("end_stage_instance", new { expectedStateDigest = new string('a', 64) }),
                    ("set_stage_speaker", new { expectedStageStateDigest = new string('a', 64), userId = guildId, expectedMemberStateDigest = new string('a', 64), isSpeaker = true }),
                    ("request_stage_speaking", new { expectedStageStateDigest = new string('a', 64), requesting = true })
                })
                {
                    using var refusal = await CallMutationErrorAsync(
                        tools[toolName],
                        new Dictionary<string, object?>
                        {
                            ["request_id"] = Guid.NewGuid().ToString("D"),
                            ["channel_id"] = stageChannelId,
                            ["reason"] = "Live missing stage instance refusal",
                            ["input"] = input
                        },
                        "resource_not_found",
                        timeout.Token);
                }
            }

            if (followerWebhookId is not null)
            {
                await DeleteWebhookFixtureAsync(tools, followerWebhookId, storedWebhookId: null, timeout.Token);
                followerWebhookId = null;
            }

            foreach (var channelId in new[] { mediaId, forumId, stageChannelId, announcementId, destinationId })
            {
                if (channelId is not null)
                {
                    await DeleteChannelFixtureAsync(tools, channelId, timeout.Token);
                }
            }

            mediaId = forumId = stageChannelId = announcementId = destinationId = null;
            output.WriteLine("Community channel lifecycle passed for announcement, forum, media capability, and stage operations.");
        }
        finally
        {
            if (forumThreadId is not null)
            {
                using var thread = await CallAsync(
                    tools["get_thread"],
                    new Dictionary<string, object?> { ["thread_id"] = forumThreadId },
                    timeout.Token);
                if (thread.RootElement.GetProperty("outcome").GetString() == "ok")
                {
                    using var deleted = await CallMutationSucceededAsync(
                        tools["delete_thread"],
                        new Dictionary<string, object?>
                        {
                            ["request_id"] = Guid.NewGuid().ToString("D"),
                            ["thread_id"] = forumThreadId,
                            ["reason"] = "Disposable live forum thread fixture cleanup",
                            ["input"] = new
                            {
                                expectedStateDigest = thread.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                            }
                        },
                        timeout.Token);
                }
            }

            if (followerWebhookId is not null)
            {
                await DeleteWebhookFixtureAsync(tools, followerWebhookId, storedWebhookId: null, timeout.Token);
            }

            foreach (var channelId in new[] { mediaId, forumId, stageChannelId, announcementId, destinationId })
            {
                if (channelId is not null)
                {
                    _ = await TryDeleteChannelFixtureAsync(tools, channelId, timeout.Token);
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedAnnouncementContinuation_FollowsCrosspostsAndCleansExactFixtures()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        var destinationId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_ANNOUNCEMENT_DESTINATION_ID");
        var announcementId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_ANNOUNCEMENT_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(destinationId) ||
            string.IsNullOrWhiteSpace(announcementId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_channel",
            "get_message",
            "list_webhooks",
            "get_webhook",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "follow_announcement_channel",
            "send_message",
            "crosspost_announcement_message",
            "delete_message",
            "delete_webhook",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        string? followerWebhookId = null;
        try
        {
            using var source = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = announcementId },
                timeout.Token);
            using var destination = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = destinationId },
                timeout.Token);
            using var followed = await CallMutationSucceededAsync(
                tools["follow_announcement_channel"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable exact announcement follow",
                    ["input"] = new
                    {
                        sourceChannelId = announcementId,
                        expectedSourceStateDigest = source.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                        destinationChannelId = destinationId,
                        expectedDestinationStateDigest = destination.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()
                    }
                },
                timeout.Token);
            followerWebhookId = followed.RootElement.GetProperty("data").GetProperty("observed").GetProperty("webhookId").GetString();
            using var webhooks = await CallOkAsync(tools["list_webhooks"], null, timeout.Token);
            Assert.Contains(webhooks.RootElement.GetProperty("data").EnumerateArray(), item => item.GetProperty("id").GetString() == followerWebhookId);

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var allowedMentions = new
            {
                userIds = Array.Empty<string>(),
                roleIds = Array.Empty<string>(),
                everyone = false,
                repliedUser = false
            };
            using var sent = await SendLiveMessageAsync(
                tools,
                announcementId,
                source.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!,
                $"Disposable announcement {suffix}",
                $"announce-{suffix}",
                allowedMentions,
                timeout.Token);
            var sentData = sent.RootElement.GetProperty("data").GetProperty("observed");
            var messageId = sentData.GetProperty("messageId").GetString()!;
            using var crossposted = await CallMutationSucceededAsync(
                tools["crosspost_announcement_message"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = announcementId,
                    ["message_id"] = messageId,
                    ["reason"] = "Disposable exact announcement crosspost",
                    ["input"] = new { expectedMessageStateDigest = sentData.GetProperty("stateDigest").GetString() }
                },
                timeout.Token);
            using var message = await CallOkAsync(
                tools["get_message"],
                new Dictionary<string, object?> { ["channel_id"] = announcementId, ["message_id"] = messageId },
                timeout.Token);
            using var deletedMessage = await CallMutationSucceededAsync(
                tools["delete_message"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = announcementId,
                    ["message_id"] = messageId,
                    ["reason"] = "Disposable exact announcement message cleanup",
                    ["input"] = new
                    {
                        expectedMessageStateDigest = message.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                    }
                },
                timeout.Token);
            await DeleteWebhookFixtureAsync(tools, followerWebhookId!, storedWebhookId: null, timeout.Token);
            followerWebhookId = null;
            await DeleteChannelFixtureAsync(tools, announcementId, timeout.Token);
            await DeleteChannelFixtureAsync(tools, destinationId, timeout.Token);
            output.WriteLine("Announcement follow/crosspost continuation passed and cleaned both channels plus follower webhook.");
        }
        finally
        {
            if (followerWebhookId is not null)
            {
                await DeleteWebhookFixtureAsync(tools, followerWebhookId, storedWebhookId: null, timeout.Token);
            }

            _ = await TryDeleteChannelFixtureAsync(tools, announcementId, timeout.Token);
            _ = await TryDeleteChannelFixtureAsync(tools, destinationId, timeout.Token);
        }
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedStageForumMediaLifecycle_ExercisesRemainingCommunitySurfaces()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_bot_effective_permissions",
            "get_channel",
            "get_thread",
            "get_stage_instance",
            "get_member",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_stage_channel",
            "create_forum_channel",
            "create_media_channel",
            "create_forum_post",
            "delete_thread",
            "start_stage_instance",
            "update_stage_instance",
            "end_stage_instance",
            "set_stage_speaker",
            "request_stage_speaking",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(7));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        string? stageChannelId = null;
        string? forumId = null;
        string? mediaId = null;
        string? forumThreadId = null;
        try
        {
            stageChannelId = await CreateLiveChannelAsync(
                tools,
                "create_stage_channel",
                $"ds-live-stage-{suffix}",
                parentId: null,
                timeout.Token);
            forumId = await CreateLiveChannelAsync(
                tools,
                "create_forum_channel",
                $"ds-live-forum-{suffix}",
                parentId: null,
                timeout.Token);
            var mediaArguments = await CreateLiveChannelArgumentsAsync(
                tools,
                "create_media_channel",
                $"ds-live-media-{suffix}",
                parentId: null,
                timeout.Token);
            using (var media = await CallAsync(tools["create_media_channel"], mediaArguments, timeout.Token))
            {
                if (media.RootElement.GetProperty("outcome").GetString() == "succeeded")
                {
                    mediaId = media.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString();
                }
                else
                {
                    Assert.Contains(media.RootElement.GetProperty("error").GetProperty("code").GetString(), AcceptedCapabilityErrors);
                }
            }

            using var forum = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = forumId },
                timeout.Token);
            var forumData = forum.RootElement.GetProperty("data");
            using var post = await CallMutationSucceededAsync(
                tools["create_forum_post"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["reason"] = "Disposable focused forum post create",
                    ["input"] = new
                    {
                        parentChannelId = forumId,
                        expectedParentStateDigest = forumData.GetProperty("mutationStateDigest").GetString(),
                        expectedAvailableTagsDigest = forumData.GetProperty("availableTagsDigest").GetString(),
                        name = $"ds-live-forum-post-{suffix}",
                        content = "Disposable focused forum post",
                        appliedTagIds = Array.Empty<string>(),
                        autoArchiveMinutes = 60,
                        slowModeSeconds = 0
                    }
                },
                timeout.Token);
            forumThreadId = post.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
            using var thread = await CallOkAsync(
                tools["get_thread"],
                new Dictionary<string, object?> { ["thread_id"] = forumThreadId },
                timeout.Token);
            using var deletedPost = await CallMutationSucceededAsync(
                tools["delete_thread"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["thread_id"] = forumThreadId,
                    ["reason"] = "Disposable focused forum post cleanup",
                    ["input"] = new
                    {
                        expectedStateDigest = thread.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                    }
                },
                timeout.Token);
            forumThreadId = null;

            using var stageChannel = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = stageChannelId },
                timeout.Token);
            using var stageStart = await CallAsync(
                tools["start_stage_instance"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = stageChannelId,
                    ["reason"] = "Disposable focused stage start",
                    ["input"] = new
                    {
                        expectedChannelStateDigest = stageChannel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                        topic = $"Disposable Stage {suffix}",
                        scheduledEventId = (string?)null,
                        sendStartNotification = false
                    }
                },
                timeout.Token);
            if (stageStart.RootElement.GetProperty("outcome").GetString() == "succeeded")
            {
                using var stage = await CallOkAsync(
                    tools["get_stage_instance"],
                    new Dictionary<string, object?> { ["channel_id"] = stageChannelId },
                    timeout.Token);
                var stageDigest = stage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
                using var privacy = await CallAsync(
                    tools["update_stage_instance"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["channel_id"] = stageChannelId,
                        ["reason"] = "Live focused stage privacy attempt",
                        ["input"] = new { expectedStateDigest = stageDigest, privacyLevel = "public" }
                    },
                    timeout.Token);
                Assert.Contains(privacy.RootElement.GetProperty("outcome").GetString(), AppliedOrVerifiedNoChangeOutcomes);

                using var botPermissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
                var botUserId = botPermissions.RootElement.GetProperty("data").GetProperty("observed").GetProperty("botUserId").GetString()!;
                using var member = await CallOkAsync(
                    tools["get_member"],
                    new Dictionary<string, object?> { ["user_id"] = botUserId },
                    timeout.Token);
                using var currentStage = await CallOkAsync(
                    tools["get_stage_instance"],
                    new Dictionary<string, object?> { ["channel_id"] = stageChannelId },
                    timeout.Token);
                stageDigest = currentStage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
                using var speaker = await CallMutationErrorAsync(
                    tools["set_stage_speaker"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["channel_id"] = stageChannelId,
                        ["reason"] = "Live disconnected focused stage speaker refusal",
                        ["input"] = new
                        {
                            expectedStageStateDigest = stageDigest,
                            userId = botUserId,
                            expectedMemberStateDigest = member.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                            isSpeaker = true
                        }
                    },
                    "state_conflict",
                    timeout.Token);
                using var requesting = await CallMutationErrorAsync(
                    tools["request_stage_speaking"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["channel_id"] = stageChannelId,
                        ["reason"] = "Live disconnected focused request-to-speak refusal",
                        ["input"] = new { expectedStageStateDigest = stageDigest, requesting = true }
                    },
                    "state_conflict",
                    timeout.Token);
                using var finalStage = await CallOkAsync(
                    tools["get_stage_instance"],
                    new Dictionary<string, object?> { ["channel_id"] = stageChannelId },
                    timeout.Token);
                using var ended = await CallMutationSucceededAsync(
                    tools["end_stage_instance"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["channel_id"] = stageChannelId,
                        ["reason"] = "Disposable focused stage cleanup",
                        ["input"] = new
                        {
                            expectedStateDigest = finalStage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                        }
                    },
                    timeout.Token);
            }
            else
            {
                Assert.Contains(stageStart.RootElement.GetProperty("error").GetProperty("code").GetString(), AcceptedCapabilityErrors);
                foreach (var (toolName, input) in new (string, object)[]
                {
                    ("update_stage_instance", new { expectedStateDigest = new string('a', 64), privacyLevel = "guild_only" }),
                    ("end_stage_instance", new { expectedStateDigest = new string('a', 64) }),
                    ("set_stage_speaker", new { expectedStageStateDigest = new string('a', 64), userId = guildId, expectedMemberStateDigest = new string('a', 64), isSpeaker = true }),
                    ("request_stage_speaking", new { expectedStageStateDigest = new string('a', 64), requesting = true })
                })
                {
                    using var refusal = await CallMutationErrorAsync(
                        tools[toolName],
                        new Dictionary<string, object?>
                        {
                            ["request_id"] = Guid.NewGuid().ToString("D"),
                            ["channel_id"] = stageChannelId,
                            ["reason"] = "Live missing focused stage refusal",
                            ["input"] = input
                        },
                        "resource_not_found",
                        timeout.Token);
                }
            }

            if (mediaId is not null && await TryDeleteChannelFixtureAsync(tools, mediaId, timeout.Token))
            {
                mediaId = null;
            }

            if (forumId is not null && await TryDeleteChannelFixtureAsync(tools, forumId, timeout.Token))
            {
                forumId = null;
            }

            if (stageChannelId is not null && await TryDeleteChannelFixtureAsync(tools, stageChannelId, timeout.Token))
            {
                stageChannelId = null;
            }

            output.WriteLine("Focused stage/forum/media lifecycle passed and cleaned all fixtures.");
        }
        finally
        {
            if (forumThreadId is not null)
            {
                using var thread = await CallAsync(
                    tools["get_thread"],
                    new Dictionary<string, object?> { ["thread_id"] = forumThreadId },
                    timeout.Token);
                if (thread.RootElement.GetProperty("outcome").GetString() == "ok")
                {
                    using var deleted = await CallMutationSucceededAsync(
                        tools["delete_thread"],
                        new Dictionary<string, object?>
                        {
                            ["request_id"] = Guid.NewGuid().ToString("D"),
                            ["thread_id"] = forumThreadId,
                            ["reason"] = "Disposable focused forum thread fallback cleanup",
                            ["input"] = new
                            {
                                expectedStateDigest = thread.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                            }
                        },
                        timeout.Token);
                }
            }

            foreach (var channelId in new[] { mediaId, forumId, stageChannelId })
            {
                if (channelId is not null)
                {
                    _ = await TryDeleteChannelFixtureAsync(tools, channelId, timeout.Token);
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedAnalysisAndMemberSafety_CallsEveryRemainingProtectedSurfaceWithoutWrites()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_bot_effective_permissions",
            "get_member",
            "get_member_voice_state",
            "list_members_by_role",
            "get_ban",
            "analyze_channel_access",
            "analyze_privilege_escalation_paths",
            "analyze_moderation_configuration",
            "analyze_onboarding_configuration",
            "explain_member_permissions",
            "simulate_automod_rule",
            "get_operation",
            "reconcile_operation",
            "reorder_roles",
            "set_member_nickname",
            "set_member_timeout",
            "set_member_voice_channel",
            "update_member_voice_state",
            "set_member_verification_bypass",
            "kick_member",
            "ban_member",
            "bulk_ban_members",
            "unban_member"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var snapshotData = snapshot.RootElement.GetProperty("data");
        var ownerId = snapshotData.GetProperty("ownerId").GetString()!;
        var channelId = snapshotData.GetProperty("channels")[0].GetProperty("id").GetString()!;
        using var botPermissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
        var observedBot = botPermissions.RootElement.GetProperty("data").GetProperty("observed");
        var botUserId = observedBot.GetProperty("botUserId").GetString()!;
        var botRoleId = observedBot.GetProperty("highestRoleId").GetString()!;

        using var membersByRole = await CallOkAsync(
            tools["list_members_by_role"],
            new Dictionary<string, object?> { ["role_id"] = botRoleId, ["limit"] = 10 },
            timeout.Token);
        Assert.Contains(membersByRole.RootElement.GetProperty("data").GetProperty("items").EnumerateArray(), member => member.GetProperty("userId").GetString() == botUserId);
        using var voice = await CallOkAsync(
            tools["get_member_voice_state"],
            new Dictionary<string, object?> { ["user_id"] = botUserId },
            timeout.Token);
        using var channelAccess = await CallOkAsync(
            tools["analyze_channel_access"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            timeout.Token);
        using var escalation = await CallOkAsync(tools["analyze_privilege_escalation_paths"], null, timeout.Token);
        using var moderation = await CallOkAsync(tools["analyze_moderation_configuration"], null, timeout.Token);
        using var onboarding = await CallOkAsync(tools["analyze_onboarding_configuration"], null, timeout.Token);
        using var memberPermissions = await CallOkAsync(
            tools["explain_member_permissions"],
            new Dictionary<string, object?> { ["user_id"] = ownerId, ["channel_id"] = channelId },
            timeout.Token);
        using var missingBan = await CallAsync(
            tools["get_ban"],
            new Dictionary<string, object?> { ["user_id"] = ownerId },
            timeout.Token);
        Assert.Equal("resource_not_found", missingBan.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var simulated = await CallOkAsync(
            tools["simulate_automod_rule"],
            new Dictionary<string, object?>
            {
                ["rule"] = new
                {
                    id = "1",
                    guildId,
                    name = "Disposable local simulation",
                    eventType = "MessageSend",
                    triggerType = "Keyword",
                    keywordFilter = AutoModSimulationKeywords,
                    regexPatterns = Array.Empty<string>(),
                    allowList = Array.Empty<string>(),
                    presets = Array.Empty<string>(),
                    mentionTotalLimit = (int?)null,
                    mentionRaidProtectionEnabled = (bool?)null,
                    actions = new[]
                    {
                        new
                        {
                            type = "BlockMessage",
                            channelId = (string?)null,
                            customMessage = (string?)null,
                            timeoutSeconds = (int?)null
                        }
                    },
                    enabled = true,
                    exemptRoleIds = Array.Empty<string>(),
                    exemptChannelIds = Array.Empty<string>(),
                    stateDigest = new string('a', 64)
                },
                ["sample_type"] = "message",
                ["sample_text"] = "This contains steward-live-keyword for a local-only simulation."
            },
            timeout.Token);

        var blockedDigest = new string('a', 64);
        var protectedCalls = new (string ToolName, object Input)[]
        {
            ("set_member_nickname", new { expectedMemberStateDigest = blockedDigest, nickname = "must-not-apply" }),
            ("set_member_timeout", new { expectedMemberStateDigest = blockedDigest, timedOutUntil = (DateTimeOffset?)null }),
            ("set_member_voice_channel", new { expectedMemberStateDigest = blockedDigest, channelId = (string?)null }),
            ("update_member_voice_state", new { expectedMemberStateDigest = blockedDigest, muted = (bool?)true, deafened = (bool?)null }),
            ("set_member_verification_bypass", new { expectedMemberStateDigest = blockedDigest, bypassesVerification = true }),
            ("kick_member", new { expectedMemberStateDigest = blockedDigest }),
            ("ban_member", new { expectedMemberStateDigest = blockedDigest })
        };
        foreach (var (toolName, input) in protectedCalls)
        {
            using var refused = await CallMutationErrorAsync(
                tools[toolName],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["user_id"] = ownerId,
                    ["reason"] = $"Live protected owner refusal for {toolName}",
                    ["input"] = input
                },
                "policy_denied",
                timeout.Token);
        }

        using var bulkBanRefusal = await CallMutationErrorAsync(
            tools["bulk_ban_members"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Live protected owner and bot bulk-ban refusal",
                ["input"] = new
                {
                    targets = new[]
                    {
                        new { userId = ownerId, expectedMemberStateDigest = blockedDigest },
                        new { userId = botUserId, expectedMemberStateDigest = blockedDigest }
                    }
                }
            },
            "policy_denied",
            timeout.Token);

        using var unbanRefusal = await CallMutationErrorAsync(
            tools["unban_member"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["user_id"] = ownerId,
                ["reason"] = "Live non-banned owner unban refusal",
                ["input"] = new { expectedBanStateDigest = blockedDigest }
            },
            "state_conflict",
            timeout.Token);

        var blockedRole = snapshotData.GetProperty("roles").EnumerateArray()
            .First(role => role.GetProperty("id").GetString() == botRoleId);
        using var reorderRefusal = await CallMutationErrorAsync(
            tools["reorder_roles"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Live managed-role reorder refusal",
                ["expected_roles_state_digest"] = snapshotData.GetProperty("rolesStateDigest").GetString(),
                ["positions"] = new[]
                {
                    new
                    {
                        roleId = blockedRole.GetProperty("id").GetString(),
                        position = blockedRole.GetProperty("position").GetInt32()
                    }
                }
            },
            "policy_denied",
            timeout.Token);
        output.WriteLine("Remaining analyses and member reads passed; every person-impacting owner operation and blocked role reorder refused before write.");
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedMemberSafetyContinuation_RefusesRemainingProtectedOperationsBeforeWrite()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_operation",
            "reconcile_operation",
            "reorder_roles",
            "set_member_verification_bypass",
            "kick_member",
            "ban_member",
            "unban_member"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var data = snapshot.RootElement.GetProperty("data");
        var ownerId = data.GetProperty("ownerId").GetString()!;
        var blockedDigest = new string('a', 64);
        foreach (var (toolName, input) in new (string, object)[]
        {
            ("set_member_verification_bypass", new { expectedMemberStateDigest = blockedDigest, bypassesVerification = true }),
            ("kick_member", new { expectedMemberStateDigest = blockedDigest }),
            ("ban_member", new { expectedMemberStateDigest = blockedDigest })
        })
        {
            using var refused = await CallMutationErrorAsync(
                tools[toolName],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["user_id"] = ownerId,
                    ["reason"] = $"Live protected owner continuation refusal for {toolName}",
                    ["input"] = input
                },
                "policy_denied",
                timeout.Token);
        }

        using var unbanRefusal = await CallMutationErrorAsync(
            tools["unban_member"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["user_id"] = ownerId,
                ["reason"] = "Live non-banned owner continuation refusal",
                ["input"] = new { expectedBanStateDigest = blockedDigest }
            },
            "state_conflict",
            timeout.Token);

        var blockedRole = data.GetProperty("roles").EnumerateArray()
            .First(role =>
                role.GetProperty("isManaged").GetBoolean() &&
                string.Equals(role.GetProperty("name").GetString(), "DiscSteward", StringComparison.OrdinalIgnoreCase));
        using var reorderRefusal = await CallMutationErrorAsync(
            tools["reorder_roles"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Live managed-role reorder continuation refusal",
                ["expected_roles_state_digest"] = data.GetProperty("rolesStateDigest").GetString(),
                ["positions"] = new[]
                {
                    new
                    {
                        roleId = blockedRole.GetProperty("id").GetString(),
                        position = blockedRole.GetProperty("position").GetInt32()
                    }
                }
            },
            "policy_denied",
            timeout.Token);
        output.WriteLine("Verification bypass, kick, ban, unban, and role reorder all refused before write on protected/current state.");
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedRemoveUserReactionLifecycle_RemovesOnlyStewardReactionAndCleansFixture()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_guild_configuration_snapshot",
            "get_bot_effective_permissions",
            "get_channel",
            "get_message",
            "list_message_reactions",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_text_channel",
            "send_message",
            "set_own_reaction",
            "remove_user_reaction",
            "delete_message",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var channelId = await CreateLiveChannelAsync(
            tools,
            "create_text_channel",
            $"ds-live-remove-reaction-{suffix}",
            parentId: null,
            timeout.Token);
        try
        {
            using var channel = await CallOkAsync(
                tools["get_channel"],
                new Dictionary<string, object?> { ["channel_id"] = channelId },
                timeout.Token);
            var allowedMentions = new
            {
                userIds = Array.Empty<string>(),
                roleIds = Array.Empty<string>(),
                everyone = false,
                repliedUser = false
            };
            using var sent = await SendLiveMessageAsync(
                tools,
                channelId,
                channel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!,
                "Disposable bot-only reaction fixture",
                $"remove-reaction-{suffix}",
                allowedMentions,
                timeout.Token);
            var messageData = sent.RootElement.GetProperty("data").GetProperty("observed");
            var messageId = messageData.GetProperty("messageId").GetString()!;
            using var reacted = await CallMutationSucceededAsync(
                tools["set_own_reaction"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = channelId,
                    ["message_id"] = messageId,
                    ["reason"] = "Disposable own reaction before exact removal",
                    ["input"] = new
                    {
                        expectedMessageStateDigest = messageData.GetProperty("stateDigest").GetString(),
                        expectedReactionStateDigest = messageData.GetProperty("reactionStateDigest").GetString(),
                        emoji = "✅",
                        present = true
                    }
                },
                timeout.Token);
            using var botPermissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
            var botUserId = botPermissions.RootElement.GetProperty("data").GetProperty("observed").GetProperty("botUserId").GetString()!;
            var reactedData = reacted.RootElement.GetProperty("data").GetProperty("observed");
            using var removed = await CallMutationSucceededAsync(
                tools["remove_user_reaction"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = channelId,
                    ["message_id"] = messageId,
                    ["reason"] = "Disposable exact Steward reaction removal",
                    ["input"] = new
                    {
                        expectedMessageStateDigest = reactedData.GetProperty("stateDigest").GetString(),
                        expectedReactionStateDigest = reactedData.GetProperty("reactionStateDigest").GetString(),
                        emoji = "✅",
                        userId = botUserId
                    }
                },
                timeout.Token);
            using var reactions = await CallOkAsync(
                tools["list_message_reactions"],
                new Dictionary<string, object?>
                {
                    ["channel_id"] = channelId,
                    ["message_id"] = messageId,
                    ["emoji"] = "✅",
                    ["limit"] = 10
                },
                timeout.Token);
            Assert.Empty(reactions.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            var removedData = removed.RootElement.GetProperty("data").GetProperty("observed");
            using var deleted = await CallMutationSucceededAsync(
                tools["delete_message"],
                new Dictionary<string, object?>
                {
                    ["request_id"] = Guid.NewGuid().ToString("D"),
                    ["channel_id"] = channelId,
                    ["message_id"] = messageId,
                    ["reason"] = "Disposable reaction message cleanup",
                    ["input"] = new
                    {
                        expectedMessageStateDigest = removedData.GetProperty("stateDigest").GetString()
                    }
                },
                timeout.Token);
            await DeleteChannelFixtureAsync(tools, channelId, timeout.Token);
            output.WriteLine("remove_user_reaction removed only Steward's reaction and cleaned the message/channel.");
        }
        finally
        {
            _ = await TryDeleteChannelFixtureAsync(tools, channelId, timeout.Token);
        }
    }

    private static async Task DeleteInviteFixtureAsync(
        Dictionary<string, McpClientTool> tools,
        string code,
        CancellationToken cancellationToken)
    {
        using var invite = await CallAsync(
            tools["get_invite"],
            new Dictionary<string, object?> { ["code"] = code },
            cancellationToken);
        if (invite.RootElement.GetProperty("outcome").GetString() != "ok")
        {
            return;
        }

        using var deleted = await CallMutationSucceededAsync(
            tools["delete_invite"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["code"] = code,
                ["reason"] = "Disposable live invite fixture cleanup",
                ["input"] = new
                {
                    expectedStateDigest = invite.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                }
            },
            cancellationToken);
    }

    private static async Task DeleteWebhookFixtureAsync(
        Dictionary<string, McpClientTool> tools,
        string webhookId,
        string? storedWebhookId,
        CancellationToken cancellationToken)
    {
        using var webhook = await CallAsync(
            tools["get_webhook"],
            new Dictionary<string, object?> { ["webhook_id"] = webhookId },
            cancellationToken);
        if (webhook.RootElement.GetProperty("outcome").GetString() != "ok")
        {
            return;
        }

        using var deleted = await CallMutationSucceededAsync(
            tools["delete_webhook"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["webhook_id"] = webhookId,
                ["reason"] = "Disposable live webhook fixture cleanup",
                ["input"] = new
                {
                    expectedStateDigest = webhook.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                    storedWebhookId
                }
            },
            cancellationToken);
    }

    private static async Task DeleteGuildTemplateFixtureAsync(
        Dictionary<string, McpClientTool> tools,
        string code,
        CancellationToken cancellationToken)
    {
        using var template = await CallAsync(
            tools["get_guild_template"],
            new Dictionary<string, object?> { ["code"] = code },
            cancellationToken);
        if (template.RootElement.GetProperty("outcome").GetString() != "ok")
        {
            return;
        }

        using var deleted = await CallMutationSucceededAsync(
            tools["delete_guild_template"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["code"] = code,
                ["reason"] = "Disposable live guild template fixture cleanup",
                ["input"] = new
                {
                    expectedTemplateStateDigest = template.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                }
            },
            cancellationToken);
    }

    private static async Task<LiveGuildState> ReadLiveGuildStateAsync(
        Dictionary<string, McpClientTool> tools,
        CancellationToken cancellationToken)
    {
        using var guild = await CallOkAsync(tools["get_guild"], null, cancellationToken);
        var data = guild.RootElement.GetProperty("data");
        var settings = data.GetProperty("mutationSettings");
        return new LiveGuildState(
            data.GetProperty("name").GetString()!,
            data.GetProperty("mutationStateDigest").GetString()!,
            OptionalString(settings, "afkChannelId"),
            settings.GetProperty("afkTimeoutSeconds").GetInt32(),
            OptionalString(settings, "systemChannelId"),
            settings.GetProperty("systemChannelFlags").GetInt32(),
            OptionalString(settings, "rulesChannelId"),
            OptionalString(settings, "publicUpdatesChannelId"),
            OptionalString(settings, "safetyAlertsChannelId"),
            settings.GetProperty("verificationLevel").GetInt32(),
            settings.GetProperty("defaultMessageNotifications").GetInt32(),
            settings.GetProperty("explicitContentFilter").GetInt32());
    }

    private static async Task<LiveWidgetState> ReadLiveWidgetStateAsync(
        Dictionary<string, McpClientTool> tools,
        CancellationToken cancellationToken)
    {
        using var widget = await CallOkAsync(tools["get_guild_widget"], null, cancellationToken);
        var data = widget.RootElement.GetProperty("data");
        return new LiveWidgetState(
            data.GetProperty("enabled").GetBoolean(),
            OptionalString(data, "channelId"),
            data.GetProperty("stateDigest").GetString()!);
    }

    private static async Task<LiveGuildAccessState> ReadLiveGuildAccessStateAsync(
        Dictionary<string, McpClientTool> tools,
        CancellationToken cancellationToken)
    {
        using var guild = await CallOkAsync(tools["get_guild"], null, cancellationToken);
        var data = guild.RootElement.GetProperty("data");
        var features = data.GetProperty("features").EnumerateArray()
            .Select(feature => feature.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new LiveGuildAccessState(
            features.Contains("INVITES_DISABLED"),
            features.Contains("RAID_ALERTS_DISABLED"),
            data.GetProperty("featuresStateDigest").GetString()!);
    }

    private static async Task<LiveIncidentState> ReadLiveIncidentStateAsync(
        Dictionary<string, McpClientTool> tools,
        CancellationToken cancellationToken)
    {
        using var incident = await CallOkAsync(tools["get_incident_status"], null, cancellationToken);
        var data = incident.RootElement.GetProperty("data");
        return new LiveIncidentState(
            OptionalTimestamp(data, "invitesDisabledUntil"),
            OptionalTimestamp(data, "directMessagesDisabledUntil"),
            data.GetProperty("stateDigest").GetString()!);
    }

    private static async Task<LiveWelcomeState> ReadLiveWelcomeStateAsync(
        Dictionary<string, McpClientTool> tools,
        CancellationToken cancellationToken)
    {
        using var welcome = await CallOkAsync(tools["get_welcome_screen"], null, cancellationToken);
        var data = welcome.RootElement.GetProperty("data");
        return new LiveWelcomeState(
            data.GetProperty("isEnabled").GetBoolean(),
            OptionalString(data, "description"),
            data.GetProperty("channels").GetArrayLength(),
            data.GetProperty("stateDigest").GetString()!);
    }

    private static async Task<LiveOnboardingState> ReadLiveOnboardingStateAsync(
        Dictionary<string, McpClientTool> tools,
        CancellationToken cancellationToken)
    {
        using var onboarding = await CallOkAsync(tools["get_onboarding"], null, cancellationToken);
        var data = onboarding.RootElement.GetProperty("data");
        return new LiveOnboardingState(
            data.GetProperty("isEnabled").GetBoolean(),
            data.GetProperty("mode").GetString()!,
            data.GetProperty("prompts").GetArrayLength(),
            data.GetProperty("defaultChannelIds").GetArrayLength(),
            data.GetProperty("stateDigest").GetString()!);
    }

    private static async Task SetGuildNameAsync(
        Dictionary<string, McpClientTool> tools,
        string digest,
        string name,
        CancellationToken cancellationToken)
    {
        using var result = await CallMutationSucceededAsync(
            tools["update_guild_metadata"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live guild name transition",
                ["input"] = new
                {
                    expectedStateDigest = digest,
                    set = new
                    {
                        name,
                        description = (string?)null,
                        preferredLocale = (string?)null,
                        premiumProgressBarEnabled = (bool?)null,
                        iconAssetId = (string?)null,
                        bannerAssetId = (string?)null,
                        splashAssetId = (string?)null,
                        discoverySplashAssetId = (string?)null
                    },
                    clear = Array.Empty<string>()
                }
            },
            cancellationToken);
    }

    private static async Task SetGuildRoutingAsync(
        Dictionary<string, McpClientTool> tools,
        string digest,
        LiveGuildState state,
        CancellationToken cancellationToken)
    {
        using var result = await CallMutationSucceededAsync(
            tools["update_guild_system_routing"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live guild routing transition",
                ["input"] = new
                {
                    expectedStateDigest = digest,
                    afkChannelId = state.AfkChannelId,
                    afkTimeoutSeconds = state.AfkTimeoutSeconds,
                    systemChannelId = state.SystemChannelId,
                    systemChannelFlags = state.SystemChannelFlags,
                    rulesChannelId = state.RulesChannelId,
                    publicUpdatesChannelId = state.PublicUpdatesChannelId,
                    safetyAlertsChannelId = state.SafetyAlertsChannelId
                }
            },
            cancellationToken);
    }

    private static async Task SetGuildSafetyAsync(
        Dictionary<string, McpClientTool> tools,
        string digest,
        int verificationLevel,
        int defaultMessageNotifications,
        int explicitContentFilter,
        CancellationToken cancellationToken)
    {
        using var result = await CallMutationSucceededAsync(
            tools["update_guild_safety"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live guild safety transition",
                ["input"] = new
                {
                    expectedStateDigest = digest,
                    verificationLevel,
                    defaultMessageNotifications,
                    explicitContentFilter
                }
            },
            cancellationToken);
    }

    private static async Task SetGuildWidgetAsync(
        Dictionary<string, McpClientTool> tools,
        string digest,
        bool enabled,
        string? channelId,
        CancellationToken cancellationToken)
    {
        using var result = await CallMutationSucceededAsync(
            tools["update_guild_widget"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live guild widget transition",
                ["input"] = new { expectedStateDigest = digest, enabled, channelId }
            },
            cancellationToken);
    }

    private static async Task SetGuildAccessControlsAsync(
        Dictionary<string, McpClientTool> tools,
        string digest,
        bool invitesDisabled,
        bool raidAlertsDisabled,
        CancellationToken cancellationToken)
    {
        using var result = await CallMutationSucceededAsync(
            tools["update_guild_access_controls"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live guild access-control transition",
                ["input"] = new { expectedFeaturesStateDigest = digest, invitesDisabled, raidAlertsDisabled }
            },
            cancellationToken);
    }

    private static async Task SetIncidentActionsAsync(
        Dictionary<string, McpClientTool> tools,
        string digest,
        DateTimeOffset? invitesDisabledUntil,
        DateTimeOffset? directMessagesDisabledUntil,
        CancellationToken cancellationToken)
    {
        using var result = await CallMutationSucceededAsync(
            tools["set_incident_actions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live incident action transition",
                ["input"] = new { expectedStateDigest = digest, invitesDisabledUntil, directMessagesDisabledUntil }
            },
            cancellationToken);
    }

    private static async Task SetWelcomeScreenAsync(
        Dictionary<string, McpClientTool> tools,
        string digest,
        bool enabled,
        string? description,
        object[] channels,
        CancellationToken cancellationToken)
    {
        using var result = await CallMutationSucceededAsync(
            tools["update_welcome_screen"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live welcome-screen transition",
                ["input"] = new { expectedStateDigest = digest, enabled, description, channels }
            },
            cancellationToken);
    }

    private static async Task SetOnboardingAsync(
        Dictionary<string, McpClientTool> tools,
        string digest,
        object[] prompts,
        string[] defaultChannelIds,
        bool enabled,
        string mode,
        CancellationToken cancellationToken)
    {
        using var result = await CallMutationSucceededAsync(
            tools["update_onboarding"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live onboarding transition",
                ["input"] = new { expectedStateDigest = digest, prompts, defaultChannelIds, enabled, mode }
            },
            cancellationToken);
    }

    private static async Task RestoreGuildSettingsAsync(
        Dictionary<string, McpClientTool> tools,
        LiveGuildState originalGuild,
        LiveGuildAccessState originalAccess,
        LiveWidgetState originalWidget,
        LiveWelcomeState originalWelcome,
        LiveOnboardingState originalOnboarding,
        LiveIncidentState originalIncident,
        CancellationToken cancellationToken)
    {
        var access = await ReadLiveGuildAccessStateAsync(tools, cancellationToken);
        if (access.InvitesDisabled != originalAccess.InvitesDisabled ||
            access.RaidAlertsDisabled != originalAccess.RaidAlertsDisabled)
        {
            await SetGuildAccessControlsAsync(
                tools,
                access.Digest,
                originalAccess.InvitesDisabled,
                originalAccess.RaidAlertsDisabled,
                cancellationToken);
        }

        var incident = await ReadLiveIncidentStateAsync(tools, cancellationToken);
        if (incident.InvitesDisabledUntil != originalIncident.InvitesDisabledUntil ||
            incident.DirectMessagesDisabledUntil != originalIncident.DirectMessagesDisabledUntil)
        {
            await SetIncidentActionsAsync(
                tools,
                incident.Digest,
                originalIncident.InvitesDisabledUntil,
                originalIncident.DirectMessagesDisabledUntil,
                cancellationToken);
        }

        var widget = await ReadLiveWidgetStateAsync(tools, cancellationToken);
        if (widget.Enabled != originalWidget.Enabled || widget.ChannelId != originalWidget.ChannelId)
        {
            await SetGuildWidgetAsync(tools, widget.Digest, originalWidget.Enabled, originalWidget.ChannelId, cancellationToken);
        }

        var onboarding = await ReadLiveOnboardingStateAsync(tools, cancellationToken);
        if (onboarding.Enabled != originalOnboarding.Enabled || onboarding.Mode != originalOnboarding.Mode ||
            onboarding.PromptCount != originalOnboarding.PromptCount ||
            onboarding.DefaultChannelCount != originalOnboarding.DefaultChannelCount)
        {
            await SetOnboardingAsync(
                tools,
                onboarding.Digest,
                Array.Empty<object>(),
                Array.Empty<string>(),
                originalOnboarding.Enabled,
                originalOnboarding.Mode,
                cancellationToken);
        }

        var welcome = await ReadLiveWelcomeStateAsync(tools, cancellationToken);
        if (welcome.Enabled != originalWelcome.Enabled || welcome.Description != originalWelcome.Description ||
            welcome.ChannelCount != originalWelcome.ChannelCount)
        {
            await SetWelcomeScreenAsync(
                tools,
                welcome.Digest,
                originalWelcome.Enabled,
                originalWelcome.Description,
                Array.Empty<object>(),
                cancellationToken);
        }

        var guild = await ReadLiveGuildStateAsync(tools, cancellationToken);
        if (guild.VerificationLevel != originalGuild.VerificationLevel ||
            guild.DefaultMessageNotifications != originalGuild.DefaultMessageNotifications ||
            guild.ExplicitContentFilter != originalGuild.ExplicitContentFilter)
        {
            await SetGuildSafetyAsync(
                tools,
                guild.Digest,
                originalGuild.VerificationLevel,
                originalGuild.DefaultMessageNotifications,
                originalGuild.ExplicitContentFilter,
                cancellationToken);
            guild = await ReadLiveGuildStateAsync(tools, cancellationToken);
        }

        if (guild.AfkChannelId != originalGuild.AfkChannelId ||
            guild.AfkTimeoutSeconds != originalGuild.AfkTimeoutSeconds ||
            guild.SystemChannelId != originalGuild.SystemChannelId ||
            guild.SystemChannelFlags != originalGuild.SystemChannelFlags ||
            guild.RulesChannelId != originalGuild.RulesChannelId ||
            guild.PublicUpdatesChannelId != originalGuild.PublicUpdatesChannelId ||
            guild.SafetyAlertsChannelId != originalGuild.SafetyAlertsChannelId)
        {
            await SetGuildRoutingAsync(tools, guild.Digest, originalGuild, cancellationToken);
            guild = await ReadLiveGuildStateAsync(tools, cancellationToken);
        }

        if (guild.Name != originalGuild.Name)
        {
            await SetGuildNameAsync(tools, guild.Digest, originalGuild.Name, cancellationToken);
        }
    }

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? OptionalTimestamp(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var timestamp)
            ? timestamp
            : null;

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedAutoModLifecycle_CreatesUpdatesTogglesAndDeletesRule()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var names = new[]
        {
            "list_automod_rules",
            "get_automod_rule",
            "get_operation",
            "reconcile_operation",
            "create_automod_rule",
            "update_automod_rule",
            "set_automod_rule_enabled",
            "delete_automod_rule"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var existing = await CallOkAsync(tools["list_automod_rules"], null, timeout.Token);
        var rulesDigest = existing.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var baseConfiguration = new
        {
            name = $"ds-live-automod-{suffix}",
            eventType = "message_send",
            keywordFilter = new[] { $"ds-live-block-{suffix}" },
            regexPatterns = Array.Empty<string>(),
            allowList = Array.Empty<string>(),
            presets = Array.Empty<string>(),
            mentionTotalLimit = (int?)null,
            mentionRaidProtectionEnabled = (bool?)null,
            actions = new[]
            {
                new
                {
                    type = "block_message",
                    channelId = (string?)null,
                    customMessage = "Disposable Discord Steward AutoMod test",
                    timeoutSeconds = (int?)null
                }
            },
            enabled = false,
            exemptRoleIds = Array.Empty<string>(),
            exemptChannelIds = Array.Empty<string>()
        };
        using var created = await CallMutationSucceededAsync(
            tools["create_automod_rule"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live AutoMod create",
                ["input"] = new
                {
                    expectedRulesStateDigest = rulesDigest,
                    triggerType = "keyword",
                    configuration = baseConfiguration
                }
            },
            timeout.Token);
        var ruleId = created.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
        using var exact = await CallOkAsync(
            tools["get_automod_rule"],
            new Dictionary<string, object?> { ["rule_id"] = ruleId },
            timeout.Token);
        var ruleDigest = exact.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var updated = await CallMutationSucceededAsync(
            tools["update_automod_rule"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["rule_id"] = ruleId,
                ["reason"] = "Disposable live AutoMod update",
                ["input"] = new
                {
                    expectedStateDigest = ruleDigest,
                    configuration = new
                    {
                        name = $"ds-live-automod-updated-{suffix}",
                        eventType = "message_send",
                        keywordFilter = new[] { $"ds-live-updated-{suffix}" },
                        regexPatterns = Array.Empty<string>(),
                        allowList = new[] { $"ds-live-allowed-{suffix}" },
                        presets = Array.Empty<string>(),
                        mentionTotalLimit = (int?)null,
                        mentionRaidProtectionEnabled = (bool?)null,
                        baseConfiguration.actions,
                        enabled = false,
                        exemptRoleIds = Array.Empty<string>(),
                        exemptChannelIds = Array.Empty<string>()
                    }
                }
            },
            timeout.Token);

        using var updatedRule = await CallOkAsync(
            tools["get_automod_rule"],
            new Dictionary<string, object?> { ["rule_id"] = ruleId },
            timeout.Token);
        ruleDigest = updatedRule.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var enabled = await CallMutationSucceededAsync(
            tools["set_automod_rule_enabled"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["rule_id"] = ruleId,
                ["reason"] = "Disposable live AutoMod enable",
                ["input"] = new { expectedStateDigest = ruleDigest, enabled = true }
            },
            timeout.Token);
        using var enabledRule = await CallOkAsync(
            tools["get_automod_rule"],
            new Dictionary<string, object?> { ["rule_id"] = ruleId },
            timeout.Token);
        Assert.True(enabledRule.RootElement.GetProperty("data").GetProperty("enabled").GetBoolean());
        ruleDigest = enabledRule.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var disabled = await CallMutationSucceededAsync(
            tools["set_automod_rule_enabled"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["rule_id"] = ruleId,
                ["reason"] = "Disposable live AutoMod disable",
                ["input"] = new { expectedStateDigest = ruleDigest, enabled = false }
            },
            timeout.Token);
        using var disabledRule = await CallOkAsync(
            tools["get_automod_rule"],
            new Dictionary<string, object?> { ["rule_id"] = ruleId },
            timeout.Token);
        ruleDigest = disabledRule.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var deleted = await CallMutationSucceededAsync(
            tools["delete_automod_rule"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["rule_id"] = ruleId,
                ["reason"] = "Disposable live AutoMod cleanup",
                ["input"] = new { expectedStateDigest = ruleDigest }
            },
            timeout.Token);
        output.WriteLine("Disposable AutoMod lifecycle passed and deleted rule {0}.", ruleId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedScheduledEventLifecycles_CompleteCancelAndDeleteFixtures()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var names = new[]
        {
            "list_scheduled_events",
            "get_scheduled_event",
            "get_operation",
            "reconcile_operation",
            "create_scheduled_event",
            "update_scheduled_event",
            "start_scheduled_event",
            "complete_scheduled_event",
            "cancel_scheduled_event",
            "delete_scheduled_event"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var start = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3_600);
        var end = start.AddHours(1);

        using var listed = await CallOkAsync(tools["list_scheduled_events"], null, timeout.Token);
        var eventsDigest = listed.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var created = await CallMutationSucceededAsync(
            tools["create_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live scheduled event create",
                ["input"] = new
                {
                    expectedEventsStateDigest = eventsDigest,
                    channelId = (string?)null,
                    name = $"ds-live-event-{suffix}",
                    description = "Disposable Discord Steward event",
                    startTime = start,
                    endTime = end,
                    entityType = "external",
                    location = "Disposable test location",
                    coverAssetId = (string?)null,
                    recurrence = (object?)null
                }
            },
            timeout.Token);
        var eventId = created.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
        using var exact = await CallOkAsync(
            tools["get_scheduled_event"],
            new Dictionary<string, object?> { ["event_id"] = eventId },
            timeout.Token);
        var eventDigest = exact.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var updated = await CallMutationSucceededAsync(
            tools["update_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["event_id"] = eventId,
                ["reason"] = "Disposable live scheduled event update",
                ["input"] = new
                {
                    expectedStateDigest = eventDigest,
                    set = new
                    {
                        channelId = (string?)null,
                        name = $"ds-live-event-updated-{suffix}",
                        description = "Updated disposable Discord Steward event",
                        startTime = start.AddMinutes(5),
                        endTime = end.AddMinutes(5),
                        entityType = "external",
                        location = "Updated disposable location",
                        coverAssetId = (string?)null,
                        recurrence = (object?)null
                    },
                    clear = Array.Empty<string>()
                }
            },
            timeout.Token);
        using var updatedEvent = await CallOkAsync(
            tools["get_scheduled_event"],
            new Dictionary<string, object?> { ["event_id"] = eventId },
            timeout.Token);
        eventDigest = updatedEvent.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var started = await CallMutationSucceededAsync(
            tools["start_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["event_id"] = eventId,
                ["reason"] = "Disposable live scheduled event start",
                ["input"] = new { expectedStateDigest = eventDigest }
            },
            timeout.Token);
        using var activeEvent = await CallOkAsync(
            tools["get_scheduled_event"],
            new Dictionary<string, object?> { ["event_id"] = eventId },
            timeout.Token);
        Assert.Equal("Active", activeEvent.RootElement.GetProperty("data").GetProperty("status").GetString());
        eventDigest = activeEvent.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var completed = await CallMutationSucceededAsync(
            tools["complete_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["event_id"] = eventId,
                ["reason"] = "Disposable live scheduled event complete",
                ["input"] = new { expectedStateDigest = eventDigest }
            },
            timeout.Token);
        using var completedEvent = await CallOkAsync(
            tools["get_scheduled_event"],
            new Dictionary<string, object?> { ["event_id"] = eventId },
            timeout.Token);
        eventDigest = completedEvent.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var deletedCompleted = await CallMutationSucceededAsync(
            tools["delete_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["event_id"] = eventId,
                ["reason"] = "Disposable completed event cleanup",
                ["input"] = new { expectedStateDigest = eventDigest }
            },
            timeout.Token);

        using var relisted = await CallOkAsync(tools["list_scheduled_events"], null, timeout.Token);
        eventsDigest = relisted.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var cancelFixture = await CallMutationSucceededAsync(
            tools["create_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable cancel event create",
                ["input"] = new
                {
                    expectedEventsStateDigest = eventsDigest,
                    channelId = (string?)null,
                    name = $"ds-live-cancel-{suffix}",
                    description = "Disposable cancel transition",
                    startTime = start.AddHours(2),
                    endTime = end.AddHours(2),
                    entityType = "external",
                    location = "Disposable test location",
                    coverAssetId = (string?)null,
                    recurrence = (object?)null
                }
            },
            timeout.Token);
        var canceledEventId = cancelFixture.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
        using var scheduledCancel = await CallOkAsync(
            tools["get_scheduled_event"],
            new Dictionary<string, object?> { ["event_id"] = canceledEventId },
            timeout.Token);
        eventDigest = scheduledCancel.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var canceled = await CallMutationSucceededAsync(
            tools["cancel_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["event_id"] = canceledEventId,
                ["reason"] = "Disposable live scheduled event cancel",
                ["input"] = new { expectedStateDigest = eventDigest }
            },
            timeout.Token);
        using var canceledEvent = await CallOkAsync(
            tools["get_scheduled_event"],
            new Dictionary<string, object?> { ["event_id"] = canceledEventId },
            timeout.Token);
        eventDigest = canceledEvent.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
        using var deletedCanceled = await CallMutationSucceededAsync(
            tools["delete_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["event_id"] = canceledEventId,
                ["reason"] = "Disposable canceled event cleanup",
                ["input"] = new { expectedStateDigest = eventDigest }
            },
            timeout.Token);
        output.WriteLine("Scheduled-event complete/cancel lifecycles passed and deleted events {0}, {1}.", eventId, canceledEventId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedEmojiLifecycle_RegistersAssetCreatesUpdatesAndDeletesEmoji()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var localRoot = Path.Combine(Path.GetTempPath(), $"discord-steward-live-expression-{Guid.NewGuid():N}");
        var inbox = Path.Combine(localRoot, "inbox");
        Directory.CreateDirectory(inbox);
        await File.WriteAllBytesAsync(
            Path.Combine(inbox, "emoji.png"),
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
            TestContext.Current.CancellationToken);
        try
        {
            var names = new[]
            {
                "list_assets",
                "register_asset",
                "get_asset",
                "delete_asset",
                "list_guild_expressions",
                "get_guild_expression",
                "get_operation",
                "reconcile_operation",
                "create_guild_expression",
                "update_guild_expression",
                "delete_guild_expression"
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await using (var client = await CreateClientAsync(
                guildId,
                timeout.Token,
                names,
                sensitiveReads: true,
                allWriteGates: true,
                localDataRoot: localRoot))
            {
                var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
                using var assets = await CallOkAsync(tools["list_assets"], null, timeout.Token);
                var candidate = assets.RootElement.GetProperty("data").GetProperty("candidates")[0];
                using var registered = await CallOkAsync(
                    tools["register_asset"],
                    new Dictionary<string, object?>
                    {
                        ["candidate_id"] = candidate.GetProperty("candidateId").GetString(),
                        ["expected_digest"] = candidate.GetProperty("sha256Digest").GetString(),
                        ["media_type"] = "image/png",
                        ["byte_length"] = candidate.GetProperty("byteLength").GetInt64()
                    },
                    timeout.Token);
                var assetId = registered.RootElement.GetProperty("data").GetProperty("assetId").GetString()!;
                using var expressions = await CallOkAsync(
                    tools["list_guild_expressions"],
                    new Dictionary<string, object?> { ["expression_type"] = "emoji" },
                    timeout.Token);
                var expressionsDigest = expressions.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
                var suffix = Guid.NewGuid().ToString("N")[..8];
                using var created = await CallMutationSucceededAsync(
                    tools["create_guild_expression"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["reason"] = "Disposable live emoji create",
                        ["input"] = new
                        {
                            expectedExpressionsStateDigest = expressionsDigest,
                            expressionType = "emoji",
                            assetId,
                            metadata = new
                            {
                                name = $"dslive_{suffix}",
                                description = (string?)null,
                                tags = Array.Empty<string>(),
                                roleIds = Array.Empty<string>(),
                                volume = (double?)null,
                                emojiId = (string?)null,
                                emojiName = (string?)null
                            }
                        }
                    },
                    timeout.Token);
                var emojiId = created.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
                using var exact = await CallOkAsync(
                    tools["get_guild_expression"],
                    new Dictionary<string, object?> { ["expression_type"] = "emoji", ["expression_id"] = emojiId },
                    timeout.Token);
                var emojiDigest = exact.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
                using var updated = await CallMutationSucceededAsync(
                    tools["update_guild_expression"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["expression_id"] = emojiId,
                        ["reason"] = "Disposable live emoji update",
                        ["input"] = new
                        {
                            expectedStateDigest = emojiDigest,
                            expressionType = "emoji",
                            metadata = new
                            {
                                name = $"dslive_updated_{suffix}",
                                description = (string?)null,
                                tags = Array.Empty<string>(),
                                roleIds = Array.Empty<string>(),
                                volume = (double?)null,
                                emojiId = (string?)null,
                                emojiName = (string?)null
                            }
                        }
                    },
                    timeout.Token);
                using var updatedEmoji = await CallOkAsync(
                    tools["get_guild_expression"],
                    new Dictionary<string, object?> { ["expression_type"] = "emoji", ["expression_id"] = emojiId },
                    timeout.Token);
                emojiDigest = updatedEmoji.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()!;
                using var deleted = await CallMutationSucceededAsync(
                    tools["delete_guild_expression"],
                    new Dictionary<string, object?>
                    {
                        ["request_id"] = Guid.NewGuid().ToString("D"),
                        ["expression_id"] = emojiId,
                        ["reason"] = "Disposable live emoji cleanup",
                        ["input"] = new { expectedStateDigest = emojiDigest, expressionType = "emoji" }
                    },
                    timeout.Token);
                using var deletedAsset = await CallOkAsync(
                    tools["delete_asset"],
                    new Dictionary<string, object?> { ["asset_id"] = assetId },
                    timeout.Token);
                output.WriteLine("Asset-backed emoji lifecycle passed and deleted emoji {0} plus asset {1}.", emojiId, assetId);
            }
        }
        finally
        {
            if (Directory.Exists(localRoot))
            {
                Directory.Delete(localRoot, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedMessageLifecycle_ExercisesMessagesReactionsPinsPollAndDeletes()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var names = new[]
        {
            "get_guild_configuration_snapshot",
            "get_channel",
            "get_message",
            "list_message_reactions",
            "list_poll_voters",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "create_text_channel",
            "trigger_typing_indicator",
            "send_message",
            "edit_own_message",
            "set_message_pinned",
            "set_own_reaction",
            "clear_message_reactions",
            "create_poll",
            "end_poll",
            "delete_message",
            "bulk_delete_messages",
            "delete_channel"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var snapshot = await CallOkAsync(tools["get_guild_configuration_snapshot"], null, timeout.Token);
        var channelsDigest = snapshot.RootElement.GetProperty("data").GetProperty("channelsStateDigest").GetString()!;
        using var channelCreate = await CallMutationSucceededAsync(
            tools["create_text_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["reason"] = "Disposable live message channel create",
                ["input"] = new
                {
                    expectedChannelsStateDigest = channelsDigest,
                    name = $"ds-live-messages-{suffix}",
                    topic = "Disposable message tool fixture",
                    isNsfw = false,
                    slowModeSeconds = 0,
                    defaultAutoArchiveMinutes = 1440,
                    roleOverwrites = Array.Empty<object>()
                }
            },
            timeout.Token);
        var channelId = channelCreate.RootElement.GetProperty("data").GetProperty("observed").GetProperty("id").GetString()!;
        using var channel = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            timeout.Token);
        var channelDigest = channel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!;
        var allowedMentions = new
        {
            userIds = Array.Empty<string>(),
            roleIds = Array.Empty<string>(),
            everyone = false,
            repliedUser = false
        };

        using var typing = await CallMutationSucceededAsync(
            tools["trigger_typing_indicator"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live typing indicator",
                ["input"] = new { expectedChannelStateDigest = channelDigest }
            },
            timeout.Token);

        using var rich = await CallMutationSucceededAsync(
            tools["send_message"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live rich message",
                ["input"] = new
                {
                    expectedChannelStateDigest = channelDigest,
                    mode = "new",
                    content = $"Rich disposable message {suffix}",
                    sourceChannelId = (string?)null,
                    sourceMessageId = (string?)null,
                    expectedSourceMessageStateDigest = (string?)null,
                    allowedMentions,
                    assetIds = Array.Empty<string>(),
                    nonce = $"rich-{suffix}",
                    suppressNotifications = true,
                    embeds = new[]
                    {
                        new
                        {
                            title = "Discord Steward live validation",
                            description = $"Rich embed {suffix}",
                            colorHex = "#2ECC71"
                        }
                    }
                }
            },
            timeout.Token);
        var richMessageId = rich.RootElement.GetProperty("data").GetProperty("observed").GetProperty("messageId").GetString()!;
        using var richMessage = await CallOkAsync(
            tools["get_message"],
            new Dictionary<string, object?> { ["channel_id"] = channelId, ["message_id"] = richMessageId },
            timeout.Token);
        Assert.NotEqual("[]", richMessage.RootElement.GetProperty("data").GetProperty("embedsJson").GetString());

        using var components = await CallMutationSucceededAsync(
            tools["send_message"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live static components message",
                ["input"] = new
                {
                    expectedChannelStateDigest = channelDigest,
                    mode = "new",
                    content = (string?)null,
                    sourceChannelId = (string?)null,
                    sourceMessageId = (string?)null,
                    expectedSourceMessageStateDigest = (string?)null,
                    allowedMentions,
                    assetIds = Array.Empty<string>(),
                    nonce = $"components-{suffix}",
                    suppressNotifications = true,
                    components = new[]
                    {
                        new { type = "text_display", content = $"Static Components v2 {suffix}" }
                    }
                }
            },
            timeout.Token);
        var componentsMessageId = components.RootElement.GetProperty("data").GetProperty("observed").GetProperty("messageId").GetString()!;
        using var componentsMessage = await CallOkAsync(
            tools["get_message"],
            new Dictionary<string, object?> { ["channel_id"] = channelId, ["message_id"] = componentsMessageId },
            timeout.Token);
        Assert.NotEqual("[]", componentsMessage.RootElement.GetProperty("data").GetProperty("componentsJson").GetString());
        Assert.NotEqual(0, componentsMessage.RootElement.GetProperty("data").GetProperty("flags").GetInt64() & (1L << 15));

        using var sent = await SendLiveMessageAsync(
            tools,
            channelId,
            channelDigest,
            $"Disposable message {suffix}",
            $"msg-{suffix}-1",
            allowedMentions,
            timeout.Token);
        var messageData = sent.RootElement.GetProperty("data").GetProperty("observed");
        var messageId = messageData.GetProperty("messageId").GetString()!;
        var messageDigest = messageData.GetProperty("stateDigest").GetString()!;
        using var edited = await CallMutationSucceededAsync(
            tools["edit_own_message"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live message edit",
                ["input"] = new
                {
                    expectedMessageStateDigest = messageDigest,
                    content = $"Edited disposable message {suffix}",
                    retainedAttachmentIds = Array.Empty<string>(),
                    newAssetIds = Array.Empty<string>(),
                    allowedMentions,
                    embeds = new[]
                    {
                        new { title = "Edited rich payload", description = $"Edit {suffix}" }
                    },
                    suppressEmbeds = false
                }
            },
            timeout.Token);
        using var editedMessage = await CallOkAsync(
            tools["get_message"],
            new Dictionary<string, object?> { ["channel_id"] = channelId, ["message_id"] = messageId },
            timeout.Token);
        Assert.NotEqual("[]", editedMessage.RootElement.GetProperty("data").GetProperty("embedsJson").GetString());
        Assert.Equal(0, editedMessage.RootElement.GetProperty("data").GetProperty("flags").GetInt64() & 4L);
        using var suppressed = await CallMutationSucceededAsync(
            tools["edit_own_message"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live message clear embeds and suppress previews",
                ["input"] = new
                {
                    expectedMessageStateDigest = editedMessage.RootElement.GetProperty("data").GetProperty("stateDigest").GetString(),
                    content = (string?)null,
                    retainedAttachmentIds = Array.Empty<string>(),
                    newAssetIds = Array.Empty<string>(),
                    allowedMentions,
                    embeds = Array.Empty<object>(),
                    suppressEmbeds = true
                }
            },
            timeout.Token);
        var suppressedData = suppressed.RootElement.GetProperty("data").GetProperty("observed");
        Assert.Equal("[]", suppressedData.GetProperty("richPayload").GetProperty("embedsJson").GetString());
        Assert.NotEqual(0, suppressedData.GetProperty("flags").GetInt64() & 4L);
        messageDigest = suppressedData.GetProperty("stateDigest").GetString()!;
        using var pinned = await CallMutationSucceededAsync(
            tools["set_message_pinned"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live message pin",
                ["input"] = new { expectedMessageStateDigest = messageDigest, pinned = true }
            },
            timeout.Token);
        messageData = pinned.RootElement.GetProperty("data").GetProperty("observed");
        Assert.True(messageData.GetProperty("isPinned").GetBoolean());
        messageDigest = messageData.GetProperty("stateDigest").GetString()!;
        using var unpinned = await CallMutationSucceededAsync(
            tools["set_message_pinned"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live message unpin",
                ["input"] = new { expectedMessageStateDigest = messageDigest, pinned = false }
            },
            timeout.Token);
        messageData = unpinned.RootElement.GetProperty("data").GetProperty("observed");
        messageDigest = messageData.GetProperty("stateDigest").GetString()!;
        var reactionDigest = messageData.GetProperty("reactionStateDigest").GetString()!;
        using var reacted = await CallMutationSucceededAsync(
            tools["set_own_reaction"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live reaction add",
                ["input"] = new
                {
                    expectedMessageStateDigest = messageDigest,
                    expectedReactionStateDigest = reactionDigest,
                    emoji = "👍",
                    present = true
                }
            },
            timeout.Token);
        using var reactionUsers = await CallOkAsync(
            tools["list_message_reactions"],
            new Dictionary<string, object?>
            {
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["emoji"] = "👍",
                ["limit"] = 10
            },
            timeout.Token);
        Assert.True(reactionUsers.RootElement.GetProperty("data").GetProperty("items").GetArrayLength() > 0);
        messageData = reacted.RootElement.GetProperty("data").GetProperty("observed");
        messageDigest = messageData.GetProperty("stateDigest").GetString()!;
        reactionDigest = messageData.GetProperty("reactionStateDigest").GetString()!;
        using var unreacted = await CallMutationSucceededAsync(
            tools["set_own_reaction"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live reaction remove",
                ["input"] = new
                {
                    expectedMessageStateDigest = messageDigest,
                    expectedReactionStateDigest = reactionDigest,
                    emoji = "👍",
                    present = false
                }
            },
            timeout.Token);
        messageData = unreacted.RootElement.GetProperty("data").GetProperty("observed");
        messageDigest = messageData.GetProperty("stateDigest").GetString()!;
        reactionDigest = messageData.GetProperty("reactionStateDigest").GetString()!;
        using var reactedAgain = await CallMutationSucceededAsync(
            tools["set_own_reaction"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live reaction add before clear",
                ["input"] = new
                {
                    expectedMessageStateDigest = messageDigest,
                    expectedReactionStateDigest = reactionDigest,
                    emoji = "✅",
                    present = true
                }
            },
            timeout.Token);
        messageData = reactedAgain.RootElement.GetProperty("data").GetProperty("observed");
        using var cleared = await CallMutationSucceededAsync(
            tools["clear_message_reactions"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live reaction clear",
                ["input"] = new
                {
                    expectedMessageStateDigest = messageData.GetProperty("stateDigest").GetString(),
                    expectedReactionStateDigest = messageData.GetProperty("reactionStateDigest").GetString(),
                    emoji = (string?)null
                }
            },
            timeout.Token);
        messageData = cleared.RootElement.GetProperty("data").GetProperty("observed");

        using var poll = await CallMutationSucceededAsync(
            tools["create_poll"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live poll create",
                ["input"] = new
                {
                    expectedChannelStateDigest = channelDigest,
                    content = "Disposable poll fixture",
                    question = "Did the Discord Steward poll tool work?",
                    answers = new[]
                    {
                        new { text = "Yes", emojiId = (string?)null, emojiName = "✅" },
                        new { text = "Also yes", emojiId = (string?)null, emojiName = "👍" }
                    },
                    durationHours = 1,
                    allowMultiselect = false,
                    allowedMentions,
                    nonce = $"poll-{suffix}",
                    suppressNotifications = true
                }
            },
            timeout.Token);
        var pollMessageId = poll.RootElement.GetProperty("data").GetProperty("observed").GetProperty("messageId").GetString()!;
        using var pollMessage = await CallOkAsync(
            tools["get_message"],
            new Dictionary<string, object?> { ["channel_id"] = channelId, ["message_id"] = pollMessageId },
            timeout.Token);
        var pollData = pollMessage.RootElement.GetProperty("data");
        var answerId = pollData.GetProperty("poll").GetProperty("answers")[0].GetProperty("answerId").GetInt32();
        using var voters = await CallOkAsync(
            tools["list_poll_voters"],
            new Dictionary<string, object?>
            {
                ["channel_id"] = channelId,
                ["message_id"] = pollMessageId,
                ["answer_id"] = answerId,
                ["limit"] = 10
            },
            timeout.Token);
        using var ended = await CallMutationSucceededAsync(
            tools["end_poll"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = pollMessageId,
                ["reason"] = "Disposable live poll end",
                ["input"] = new
                {
                    expectedMessageStateDigest = pollData.GetProperty("stateDigest").GetString(),
                    expectedPollStateDigest = pollData.GetProperty("poll").GetProperty("stateDigest").GetString()
                }
            },
            timeout.Token);
        using var deletedMain = await CallMutationSucceededAsync(
            tools["delete_message"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = messageId,
                ["reason"] = "Disposable live exact message delete",
                ["input"] = new { expectedMessageStateDigest = messageData.GetProperty("stateDigest").GetString() }
            },
            timeout.Token);
        using var deletedPoll = await CallMutationSucceededAsync(
            tools["delete_message"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["message_id"] = pollMessageId,
                ["reason"] = "Disposable live poll message cleanup",
                ["input"] = new { expectedMessageStateDigest = ended.RootElement.GetProperty("data").GetProperty("observed").GetProperty("stateDigest").GetString() }
            },
            timeout.Token);

        using var bulkOne = await SendLiveMessageAsync(tools, channelId, channelDigest, "Bulk delete fixture one", $"msg-{suffix}-2", allowedMentions, timeout.Token);
        using var bulkTwo = await SendLiveMessageAsync(tools, channelId, channelDigest, "Bulk delete fixture two", $"msg-{suffix}-3", allowedMentions, timeout.Token);
        var bulkOneData = bulkOne.RootElement.GetProperty("data").GetProperty("observed");
        var bulkTwoData = bulkTwo.RootElement.GetProperty("data").GetProperty("observed");
        var bulkOneId = bulkOneData.GetProperty("messageId").GetString()!;
        var bulkTwoId = bulkTwoData.GetProperty("messageId").GetString()!;
        using var bulkDeleted = await CallMutationSucceededAsync(
            tools["bulk_delete_messages"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live bulk message cleanup",
                ["input"] = new
                {
                    messages = new[]
                    {
                        new { messageId = bulkOneId, stateDigest = bulkOneData.GetProperty("stateDigest").GetString() },
                        new { messageId = bulkTwoId, stateDigest = bulkTwoData.GetProperty("stateDigest").GetString() }
                    }
                }
            },
            timeout.Token);
        await DeleteChannelFixtureAsync(tools, channelId, timeout.Token);
        output.WriteLine("Message lifecycle passed and cleaned channel {0} plus all disposable messages.", channelId);
    }

    private static Task<JsonDocument> SendLiveMessageAsync(
        Dictionary<string, McpClientTool> tools,
        string channelId,
        string channelDigest,
        string content,
        string nonce,
        object allowedMentions,
        CancellationToken cancellationToken) => CallMutationSucceededAsync(
            tools["send_message"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live message send",
                ["input"] = new
                {
                    expectedChannelStateDigest = channelDigest,
                    mode = "new",
                    content,
                    sourceChannelId = (string?)null,
                    sourceMessageId = (string?)null,
                    expectedSourceMessageStateDigest = (string?)null,
                    allowedMentions,
                    assetIds = Array.Empty<string>(),
                    nonce,
                    suppressNotifications = true
                }
            },
            cancellationToken);

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedChannelCleanup_RemovesExactDisposableFixture()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        var channelId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_CLEANUP_CHANNEL_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(channelId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names =
        [
            "get_channel",
            "analyze_deletion_impact",
            "get_operation",
            "reconcile_operation",
            "delete_channel"
        ];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        await DeleteChannelFixtureAsync(tools, channelId, timeout.Token);
        output.WriteLine("Cleaned exact disposable channel {0}.", channelId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedScheduledEventCleanup_RemovesExactDisposableFixture()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        var eventId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_CLEANUP_EVENT_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(eventId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names = ["get_scheduled_event", "get_operation", "reconcile_operation", "delete_scheduled_event"];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var scheduledEvent = await CallOkAsync(
            tools["get_scheduled_event"],
            new Dictionary<string, object?> { ["event_id"] = eventId },
            timeout.Token);
        using var deleted = await CallMutationSucceededAsync(
            tools["delete_scheduled_event"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["event_id"] = eventId,
                ["reason"] = "Disposable live scheduled event fixture cleanup",
                ["input"] = new
                {
                    expectedStateDigest = scheduledEvent.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()
                }
            },
            timeout.Token);
        output.WriteLine("Cleaned exact disposable scheduled event {0}.", eventId);
    }

    [Fact]
    [Trait("Category", "LiveDiscordMutation")]
    public async Task AuthorizedOperationReconciliation_ResolvesExactUnknownOperation()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_GUILD_ID");
        var requestId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_RECONCILE_REQUEST_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(requestId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_MUTATION_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        string[] names = ["get_operation", "reconcile_operation"];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var before = await CallOkAsync(
            tools["get_operation"],
            new Dictionary<string, object?> { ["request_id"] = requestId },
            timeout.Token);
        Assert.Equal("unknown", before.RootElement.GetProperty("data").GetProperty("status").GetString());
        using var reconciled = await CallMutationSucceededAsync(
            tools["reconcile_operation"],
            new Dictionary<string, object?> { ["request_id"] = requestId },
            timeout.Token);
        Assert.True(reconciled.RootElement.GetProperty("data").GetProperty("verified").GetBoolean());
        output.WriteLine("Reconciled unknown operation {0} to succeeded.", requestId);
    }

    private static async Task DeleteChannelFixtureAsync(
        Dictionary<string, McpClientTool> tools,
        string channelId,
        CancellationToken cancellationToken)
    {
        using var channel = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            cancellationToken);
        using var impact = await CallOkAsync(
            tools["analyze_deletion_impact"],
            new Dictionary<string, object?>
            {
                ["resource_type"] = channel.RootElement.GetProperty("data").GetProperty("kind").GetString() == "category" ? "category" : "channel",
                ["resource_id"] = channelId
            },
            cancellationToken);
        var channelData = channel.RootElement.GetProperty("data");
        var impactData = impact.RootElement.GetProperty("data");
        var blockers = impactData.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var cascade = impactData.GetProperty("cascade").EnumerateArray().Select(item => new
        {
            resourceType = item.GetProperty("resourceType").GetString(),
            resourceId = item.GetProperty("resourceId").GetString(),
            relationship = item.GetProperty("relationship").GetString()
        }).ToArray();
        using var deleted = await CallMutationSucceededAsync(
            tools["delete_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live channel fixture cleanup",
                ["input"] = new
                {
                    expectedStateDigest = channelData.GetProperty("mutationStateDigest").GetString(),
                    expectedCascadeDigest = impactData.GetProperty("cascadeDigest").GetString(),
                    expectedBlockers = blockers,
                    expectedCascade = cascade
                }
            },
            cancellationToken);
    }

    private static async Task<bool> TryDeleteChannelFixtureAsync(
        Dictionary<string, McpClientTool> tools,
        string channelId,
        CancellationToken cancellationToken)
    {
        using var channel = await CallAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            cancellationToken);
        if (channel.RootElement.GetProperty("outcome").GetString() != "ok")
        {
            return false;
        }

        using var impact = await CallAsync(
            tools["analyze_deletion_impact"],
            new Dictionary<string, object?> { ["resource_type"] = "channel", ["resource_id"] = channelId },
            cancellationToken);
        if (impact.RootElement.GetProperty("outcome").GetString() != "ok")
        {
            return false;
        }

        var impactData = impact.RootElement.GetProperty("data");
        if (impactData.GetProperty("blockers").GetArrayLength() != 0 ||
            impactData.GetProperty("cascade").GetArrayLength() != 0)
        {
            return false;
        }

        using var deleted = await CallAsync(
            tools["delete_channel"],
            new Dictionary<string, object?>
            {
                ["request_id"] = Guid.NewGuid().ToString("D"),
                ["channel_id"] = channelId,
                ["reason"] = "Disposable live best-effort channel cleanup",
                ["input"] = new
                {
                    expectedStateDigest = channel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString(),
                    expectedCascadeDigest = impactData.GetProperty("cascadeDigest").GetString(),
                    expectedBlockers = Array.Empty<string>(),
                    expectedCascade = Array.Empty<object>()
                }
            },
            cancellationToken);
        return deleted.RootElement.GetProperty("outcome").GetString() == "succeeded";
    }

    [Fact]
    [Trait("Category", "LiveDiscord")]
    public async Task AuthorizedFullPermissionUnion_IsPresentWithoutAdministrator()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var names = new[]
        {
            "get_bot_effective_permissions",
            "list_audit_events",
            "create_role",
            "create_text_channel",
            "set_voice_channel_status",
            "create_thread",
            "set_member_nickname",
            "set_member_timeout",
            "set_member_voice_channel",
            "update_member_voice_state",
            "kick_member",
            "ban_member",
            "send_message",
            "delete_message",
            "set_message_pinned",
            "set_own_reaction",
            "create_automod_rule",
            "create_scheduled_event",
            "update_scheduled_event",
            "start_stage_instance",
            "request_stage_speaking",
            "create_guild_expression",
            "update_guild_expression",
            "create_invite",
            "create_webhook"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        using var permissions = await CallOkAsync(tools["get_bot_effective_permissions"], null, timeout.Token);
        var observed = permissions.RootElement.GetProperty("data").GetProperty("observed");
        Assert.False(observed.GetProperty("hasAdministrator").GetBoolean());
        var actual = observed.GetProperty("effectivePermissions").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var expected = new[]
        {
            "AddReactions", "AttachFiles", "BanMembers", "CreateEvents", "CreateGuildExpressions",
            "CreateInstantInvite", "CreatePrivateThreads", "CreatePublicThreads", "DeafenMembers",
            "KickMembers", "ManageChannels", "ManageEmojisAndStickers", "ManageEvents", "ManageGuild",
            "ManageMessages", "ManageNicknames", "ManageRoles", "ManageThreads", "ManageWebhooks",
            "ModerateMembers", "MoveMembers", "MuteMembers", "PinMessages", "ReadMessageHistory",
            "RequestToSpeak", "SendMessages", "SetVoiceChannelStatus", "ViewAuditLog", "ViewChannel"
        };
        Assert.DoesNotContain(expected, permission => !actual.Contains(permission));
        output.WriteLine("Full 29-permission catalog union is present; Administrator=False.");
    }

    [Fact]
    [Trait("Category", "LiveDiscord")]
    public async Task AuthorizedAdministratorRefusal_BlocksEveryMutationFamilyBeforeWrite()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        const string channelId = "100000000000000003";
        var names = new[]
        {
            "get_operation",
            "create_role",
            "create_text_channel",
            "set_member_nickname",
            "send_message",
            "create_automod_rule",
            "update_guild_widget",
            "create_scheduled_event",
            "create_guild_expression",
            "create_webhook"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(
            guildId,
            timeout.Token,
            names,
            sensitiveReads: true,
            allWriteGates: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var digest = new string('a', 64);
        var calls = new (string Tool, Dictionary<string, object?> Arguments)[]
        {
            ("create_role", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["reason"] = "live safety refusal: role",
                ["expected_roles_state_digest"] = digest, ["name"] = "blocked-role", ["permissions"] = Array.Empty<string>()
            }),
            ("create_text_channel", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["reason"] = "live safety refusal: channel",
                ["input"] = new { expectedChannelsStateDigest = digest, name = "blocked-channel", roleOverwrites = Array.Empty<object>() }
            }),
            ("set_member_nickname", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["user_id"] = guildId,
                ["reason"] = "live safety refusal: member", ["input"] = new { expectedMemberStateDigest = digest, nickname = "blocked" }
            }),
            ("send_message", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["channel_id"] = channelId,
                ["reason"] = "live safety refusal: message", ["input"] = new
                {
                    expectedChannelStateDigest = digest, mode = "new", content = "blocked", nonce = "blocked-live",
                    allowedMentions = new { userIds = Array.Empty<string>(), roleIds = Array.Empty<string>(), everyone = false, repliedUser = false },
                    assetIds = Array.Empty<string>(), suppressNotifications = true
                }
            }),
            ("create_automod_rule", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["reason"] = "live safety refusal: automod",
                ["input"] = new
                {
                    expectedRulesStateDigest = digest,
                    triggerType = "spam",
                    configuration = new
                    {
                        name = "blocked spam rule",
                        eventType = "message_send",
                        keywordFilter = Array.Empty<string>(),
                        regexPatterns = Array.Empty<string>(),
                        allowList = Array.Empty<string>(),
                        presets = Array.Empty<string>(),
                        mentionTotalLimit = (int?)null,
                        mentionRaidProtectionEnabled = (bool?)null,
                        actions = new[] { new { type = "block_message", channelId = (string?)null, customMessage = (string?)null, timeoutSeconds = (int?)null } },
                        enabled = false,
                        exemptRoleIds = Array.Empty<string>(),
                        exemptChannelIds = Array.Empty<string>()
                    }
                }
            }),
            ("update_guild_widget", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["reason"] = "live safety refusal: community",
                ["input"] = new { expectedStateDigest = digest, enabled = false, channelId = (string?)null }
            }),
            ("create_scheduled_event", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["reason"] = "live safety refusal: event",
                ["input"] = new
                {
                    expectedEventsStateDigest = digest, name = "blocked event",
                    startTime = DateTimeOffset.UtcNow.AddHours(1), entityType = "external", location = "blocked"
                }
            }),
            ("create_guild_expression", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["reason"] = "live safety refusal: expression",
                ["input"] = new
                {
                    expectedExpressionsStateDigest = digest,
                    expressionType = "emoji",
                    assetId = $"asset_{new string('b', 64)}",
                    metadata = new
                    {
                        name = "blocked",
                        description = (string?)null,
                        tags = Array.Empty<string>(),
                        roleIds = Array.Empty<string>(),
                        volume = (double?)null,
                        emojiId = (string?)null,
                        emojiName = (string?)null
                    }
                }
            }),
            ("create_webhook", new()
            {
                ["request_id"] = Guid.NewGuid().ToString("D"), ["reason"] = "live safety refusal: integration",
                ["input"] = new { expectedChannelStateDigest = digest, channelId, name = "blocked-hook" }
            })
        };

        foreach (var call in calls)
        {
            using var result = await CallAsync(tools[call.Tool], call.Arguments, timeout.Token);
            Assert.Equal("failed", result.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("policy_denied", result.RootElement.GetProperty("error").GetProperty("code").GetString());
            var requestId = call.Arguments["request_id"]!.ToString()!;
            using var evidence = await CallOkAsync(
                tools["get_operation"],
                new Dictionary<string, object?> { ["request_id"] = requestId },
                timeout.Token);
            Assert.Equal("failed", evidence.RootElement.GetProperty("data").GetProperty("status").GetString());
            Assert.Equal("policy_denied", evidence.RootElement.GetProperty("data").GetProperty("errorCode").GetString());
        }

        output.WriteLine("Administrator refusal blocked {0} representative mutation families before write.", calls.Length);
    }

    [Fact]
    [Trait("Category", "LiveDiscord")]
    public async Task AuthorizedCatalogReadsSmoke_UsesReadOnlyFocusedManifest()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var names = new[]
        {
            "get_steward_capabilities",
            "get_guild_widget",
            "get_incident_status",
            "get_guild_vanity_url",
            "list_guild_templates",
            "get_guild_template",
            "list_channels",
            "list_active_threads",
            "list_thread_members",
            "list_scheduled_events",
            "list_scheduled_event_subscribers",
            "get_stage_instance",
            "list_invites",
            "list_invite_target_users"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(guildId, timeout.Token, names, sensitiveReads: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Keys.Order(StringComparer.Ordinal));

        using var widget = await CallOkAsync(tools["get_guild_widget"], null, timeout.Token);
        using var incidents = await CallOkAsync(tools["get_incident_status"], null, timeout.Token);
        using var vanity = await CallAsync(tools["get_guild_vanity_url"], null, timeout.Token);
        Assert.True(vanity.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        using var templates = await CallOkAsync(tools["list_guild_templates"], null, timeout.Token);
        var templateItems = templates.RootElement.GetProperty("data");
        if (templateItems.GetArrayLength() > 0)
        {
            var code = templateItems[0].GetProperty("code").GetString()!;
            using var exactTemplate = await CallOkAsync(
                tools["get_guild_template"],
                new Dictionary<string, object?> { ["code"] = code },
                timeout.Token);
            Assert.Equal(code, exactTemplate.RootElement.GetProperty("data").GetProperty("code").GetString());
        }

        using var threads = await CallOkAsync(tools["list_active_threads"], null, timeout.Token);
        var threadItems = threads.RootElement.GetProperty("data");
        if (threadItems.GetArrayLength() > 0)
        {
            var threadId = threadItems[0].GetProperty("id").GetString()!;
            using var members = await CallAsync(
                tools["list_thread_members"],
                new Dictionary<string, object?> { ["thread_id"] = threadId, ["limit"] = 10 },
                timeout.Token);
            Assert.True(members.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        }

        using var events = await CallOkAsync(tools["list_scheduled_events"], null, timeout.Token);
        var eventItems = events.RootElement.GetProperty("data").GetProperty("items");
        if (eventItems.GetArrayLength() > 0)
        {
            var eventId = eventItems[0].GetProperty("id").GetString()!;
            using var subscribers = await CallAsync(
                tools["list_scheduled_event_subscribers"],
                new Dictionary<string, object?> { ["event_id"] = eventId, ["limit"] = 10 },
                timeout.Token);
            Assert.True(subscribers.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        }

        using var channels = await CallOkAsync(tools["list_channels"], null, timeout.Token);
        var stage = channels.RootElement.GetProperty("data").EnumerateArray()
            .FirstOrDefault(channel => channel.GetProperty("kind").GetString() == "stage");
        if (stage.ValueKind != JsonValueKind.Undefined)
        {
            using var instance = await CallAsync(
                tools["get_stage_instance"],
                new Dictionary<string, object?> { ["channel_id"] = stage.GetProperty("id").GetString() },
                timeout.Token);
            Assert.True(instance.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        }

        using var invites = await CallOkAsync(tools["list_invites"], null, timeout.Token);
        foreach (var invite in invites.RootElement.GetProperty("data").EnumerateArray().Take(3))
        {
            using var targets = await CallAsync(
                tools["list_invite_target_users"],
                new Dictionary<string, object?> { ["code"] = invite.GetProperty("code").GetString() },
                timeout.Token);
            Assert.True(targets.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        }

        output.WriteLine(
            "Focused catalog read smoke passed: templates={0}, threads={1}, events={2}, invites={3}.",
            templateItems.GetArrayLength(),
            threadItems.GetArrayLength(),
            eventItems.GetArrayLength(),
            invites.RootElement.GetProperty("data").GetArrayLength());
    }

    [Fact]
    [Trait("Category", "LiveDiscord")]
    public async Task AuthorizedMemberMessageReadsSmoke_IsBoundedAndReadOnly()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_GUILD_ID");
        if (string.IsNullOrWhiteSpace(guildId) ||
            !string.Equals(Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_AUTHORIZED"), "yes", StringComparison.Ordinal))
        {
            return;
        }

        var names = new[]
        {
            "list_channels",
            "list_members",
            "search_members",
            "get_member",
            "get_role_member_counts",
            "preview_member_prune",
            "list_messages",
            "get_message",
            "search_messages",
            "list_channel_pins",
            "list_message_reactions",
            "list_poll_voters"
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(guildId, timeout.Token, names, sensitiveReads: true);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Keys.Order(StringComparer.Ordinal));

        using var members = await CallAsync(
            tools["list_members"],
            new Dictionary<string, object?> { ["limit"] = 10 },
            timeout.Token);
        if (members.RootElement.GetProperty("outcome").GetString() == "ok")
        {
            var memberItems = members.RootElement.GetProperty("data").GetProperty("items");
            if (memberItems.GetArrayLength() > 0)
            {
                var userId = memberItems[0].GetProperty("userId").GetString()!;
                var query = memberItems[0].GetProperty("username").GetString()!;
                using var exact = await CallOkAsync(
                    tools["get_member"],
                    new Dictionary<string, object?> { ["user_id"] = userId },
                    timeout.Token);
                using var searched = await CallAsync(
                    tools["search_members"],
                    new Dictionary<string, object?> { ["query"] = query[..1], ["limit"] = 10 },
                    timeout.Token);
                Assert.True(searched.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
            }
        }

        using var roleCounts = await CallAsync(tools["get_role_member_counts"], null, timeout.Token);
        using var prune = await CallAsync(
            tools["preview_member_prune"],
            new Dictionary<string, object?> { ["inactivity_days"] = 30 },
            timeout.Token);
        Assert.True(roleCounts.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        Assert.True(prune.RootElement.GetProperty("outcome").GetString() is "ok" or "error");

        using var channels = await CallOkAsync(tools["list_channels"], null, timeout.Token);
        var textChannel = channels.RootElement.GetProperty("data").EnumerateArray()
            .First(channel => channel.GetProperty("kind").GetString() is "text" or "announcement");
        var channelId = textChannel.GetProperty("id").GetString()!;
        using var messages = await CallAsync(
            tools["list_messages"],
            new Dictionary<string, object?> { ["channel_id"] = channelId, ["limit"] = 5 },
            timeout.Token);
        if (messages.RootElement.GetProperty("outcome").GetString() == "ok")
        {
            var messageItems = messages.RootElement.GetProperty("data").GetProperty("items");
            if (messageItems.GetArrayLength() > 0)
            {
                var messageId = messageItems[0].GetProperty("id").GetString()!;
                using var exact = await CallOkAsync(
                    tools["get_message"],
                    new Dictionary<string, object?> { ["channel_id"] = channelId, ["message_id"] = messageId },
                    timeout.Token);
                using var reactions = await CallAsync(
                    tools["list_message_reactions"],
                    new Dictionary<string, object?>
                    {
                        ["channel_id"] = channelId,
                        ["message_id"] = messageId,
                        ["emoji"] = "👍",
                        ["limit"] = 10
                    },
                    timeout.Token);
                Assert.True(reactions.RootElement.GetProperty("outcome").GetString() is "ok" or "error");

                if (messageItems[0].TryGetProperty("poll", out var poll) && poll.ValueKind == JsonValueKind.Object)
                {
                    var answerId = poll.GetProperty("answers")[0].GetProperty("answerId").GetInt32();
                    using var voters = await CallAsync(
                        tools["list_poll_voters"],
                        new Dictionary<string, object?>
                        {
                            ["channel_id"] = channelId,
                            ["message_id"] = messageId,
                            ["answer_id"] = answerId,
                            ["limit"] = 10
                        },
                        timeout.Token);
                    Assert.True(voters.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
                }
            }
        }

        using var search = await CallAsync(
            tools["search_messages"],
            new Dictionary<string, object?> { ["channel_ids"] = new[] { channelId }, ["limit"] = 5 },
            timeout.Token);
        using var pins = await CallAsync(
            tools["list_channel_pins"],
            new Dictionary<string, object?> { ["channel_id"] = channelId, ["limit"] = 10 },
            timeout.Token);
        Assert.True(search.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        Assert.True(pins.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        output.WriteLine("Bounded member/message read smoke completed without writes.");
    }

    [Fact]
    [Trait("Category", "LiveDiscord")]
    public async Task AuthorizedReadOnlySmoke_UsesExactTargetManifest()
    {
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_GUILD_ID");
        var authorized = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_SMOKE_AUTHORIZED");
        if (string.IsNullOrWhiteSpace(guildId) || !string.Equals(authorized, "yes", StringComparison.Ordinal))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await CreateClientAsync(guildId, timeout.Token);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        Assert.Equal(
            [
                "analyze_role_hierarchy",
                "capture_guild_configuration_snapshot",
                "compare_guild_configuration",
                "explain_role_permissions",
                "get_automod_rule",
                "get_bot_effective_permissions",
                "get_channel",
                "get_guild",
                "get_guild_configuration_snapshot",
                "get_onboarding",
                "get_operation",
                "get_role",
                "get_scheduled_event",
                "get_steward_capabilities",
                "get_thread",
                "get_welcome_screen",
                "lint_guild_configuration",
                "list_active_threads",
                "list_audit_events",
                "list_automod_rules",
                "list_bans",
                "list_channel_member_overwrites",
                "list_channels",
                "list_guild_expressions",
                "list_integrations",
                "list_invites",
                "list_operations",
                "list_roles",
                "list_scheduled_events",
                "list_webhooks",
                "reconcile_operation",
                "update_channel"
            ],
            tools.Keys.Order(StringComparer.Ordinal));

        using var capabilities = await CallAsync(
            tools["get_steward_capabilities"],
            arguments: null,
            timeout.Token);
        var capabilityRoot = capabilities.RootElement;
        Assert.Equal(guildId, capabilityRoot.GetProperty("guildId").GetString());
        Assert.Equal("operator", capabilityRoot.GetProperty("mode").GetString());
        Assert.True(capabilityRoot.GetProperty("discordTokenConfigured").GetBoolean());
        Assert.Equal(64, capabilityRoot.GetProperty("manifestDigest").GetString()?.Length);
        Assert.Contains(
            capabilityRoot.GetProperty("registeredTools").EnumerateArray().Select(value => value.GetString()),
            name => name == "update_channel");

        using var permissions = await CallOkAsync(
            tools["get_bot_effective_permissions"],
            arguments: null,
            timeout.Token);
        var observedPermissions = permissions.RootElement.GetProperty("data").GetProperty("observed");
        Assert.Equal(guildId, observedPermissions.GetProperty("guildId").GetString());
        var hasAdministrator = observedPermissions.GetProperty("hasAdministrator").GetBoolean();

        using var snapshot = await CallOkAsync(
            tools["get_guild_configuration_snapshot"],
            arguments: null,
            timeout.Token);
        var data = snapshot.RootElement.GetProperty("data");
        Assert.Equal(guildId, data.GetProperty("id").GetString());
        var roles = data.GetProperty("roles");
        var channels = data.GetProperty("channels");
        Assert.True(roles.GetArrayLength() > 0);
        Assert.True(channels.GetArrayLength() > 0);
        var roleId = roles[0].GetProperty("id").GetString()!;
        var channelId = channels[0].GetProperty("id").GetString()!;

        using var role = await CallOkAsync(
            tools["get_role"],
            new Dictionary<string, object?> { ["role_id"] = roleId },
            timeout.Token);
        Assert.Equal(roleId, role.RootElement.GetProperty("data").GetProperty("id").GetString());

        using var channel = await CallOkAsync(
            tools["get_channel"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            timeout.Token);
        Assert.Equal(channelId, channel.RootElement.GetProperty("data").GetProperty("id").GetString());
        Assert.Equal(64, channel.RootElement.GetProperty("data").GetProperty("stateDigest").GetString()?.Length);

        using var lint = await CallOkAsync(
            tools["lint_guild_configuration"],
            arguments: null,
            timeout.Token);
        var findingCount = lint.RootElement.GetProperty("data").GetProperty("findingCount").GetInt32();

        using var explanation = await CallOkAsync(
            tools["explain_role_permissions"],
            new Dictionary<string, object?>
            {
                ["role_id"] = roleId,
                ["channel_id"] = channelId
            },
            timeout.Token);
        var effectivePermissionCount = explanation.RootElement
            .GetProperty("data")
            .GetProperty("effectivePermissions")
            .GetArrayLength();

        using var hierarchy = await CallOkAsync(
            tools["analyze_role_hierarchy"],
            arguments: null,
            timeout.Token);
        Assert.Equal(guildId, hierarchy.RootElement.GetProperty("data").GetProperty("guildId").GetString());

        using var memberOverwrites = await CallOkAsync(
            tools["list_channel_member_overwrites"],
            new Dictionary<string, object?> { ["channel_id"] = channelId },
            timeout.Token);
        Assert.Equal(channelId, memberOverwrites.RootElement.GetProperty("data").GetProperty("channelId").GetString());

        using var captured = await CallOkAsync(
            tools["capture_guild_configuration_snapshot"],
            arguments: null,
            timeout.Token);
        var snapshotId = captured.RootElement.GetProperty("data").GetProperty("snapshotId").GetString()!;
        using var compared = await CallOkAsync(
            tools["compare_guild_configuration"],
            new Dictionary<string, object?> { ["snapshot_id"] = snapshotId },
            timeout.Token);
        Assert.False(compared.RootElement.GetProperty("data").GetProperty("changed").GetBoolean());

        using var operations = await CallOkAsync(
            tools["list_operations"],
            new Dictionary<string, object?> { ["limit"] = 20 },
            timeout.Token);

        using var autoModRules = await CallOkAsync(tools["list_automod_rules"], null, timeout.Token);
        var autoModItems = autoModRules.RootElement.GetProperty("data").GetProperty("items");
        if (autoModItems.GetArrayLength() > 0)
        {
            var ruleId = autoModItems[0].GetProperty("id").GetString()!;
            using var exactRule = await CallOkAsync(
                tools["get_automod_rule"],
                new Dictionary<string, object?> { ["rule_id"] = ruleId },
                timeout.Token);
            Assert.Equal(ruleId, exactRule.RootElement.GetProperty("data").GetProperty("id").GetString());
        }

        using var scheduledEvents = await CallOkAsync(tools["list_scheduled_events"], null, timeout.Token);
        var eventItems = scheduledEvents.RootElement.GetProperty("data").GetProperty("items");
        if (eventItems.GetArrayLength() > 0)
        {
            var eventId = eventItems[0].GetProperty("id").GetString()!;
            using var exactEvent = await CallOkAsync(
                tools["get_scheduled_event"],
                new Dictionary<string, object?> { ["event_id"] = eventId },
                timeout.Token);
            Assert.Equal(eventId, exactEvent.RootElement.GetProperty("data").GetProperty("id").GetString());
        }

        using var activeThreads = await CallOkAsync(tools["list_active_threads"], null, timeout.Token);
        var threadItems = activeThreads.RootElement.GetProperty("data");
        if (threadItems.GetArrayLength() > 0)
        {
            var threadId = threadItems[0].GetProperty("id").GetString()!;
            using var exactThread = await CallOkAsync(
                tools["get_thread"],
                new Dictionary<string, object?> { ["thread_id"] = threadId },
                timeout.Token);
            Assert.Equal(threadId, exactThread.RootElement.GetProperty("data").GetProperty("id").GetString());
        }

        using var bans = await CallOkAsync(tools["list_bans"], new Dictionary<string, object?> { ["limit"] = 100 }, timeout.Token);
        using var invites = await CallOkAsync(tools["list_invites"], null, timeout.Token);
        using var integrations = await CallOkAsync(tools["list_integrations"], null, timeout.Token);
        using var webhooks = await CallOkAsync(tools["list_webhooks"], null, timeout.Token);
        using var audit = await CallOkAsync(tools["list_audit_events"], new Dictionary<string, object?> { ["limit"] = 20 }, timeout.Token);
        using var emojis = await CallOkAsync(tools["list_guild_expressions"], new Dictionary<string, object?> { ["expression_type"] = "emoji" }, timeout.Token);
        using var stickers = await CallOkAsync(tools["list_guild_expressions"], new Dictionary<string, object?> { ["expression_type"] = "sticker" }, timeout.Token);
        using var soundboard = await CallOkAsync(tools["list_guild_expressions"], new Dictionary<string, object?> { ["expression_type"] = "soundboard" }, timeout.Token);
        using var onboarding = await CallAsync(tools["get_onboarding"], null, timeout.Token);
        using var welcome = await CallAsync(tools["get_welcome_screen"], null, timeout.Token);
        Assert.True(onboarding.RootElement.GetProperty("outcome").GetString() is "ok" or "error");
        Assert.True(welcome.RootElement.GetProperty("outcome").GetString() is "ok" or "error");

        output.WriteLine(
            "Extended read smoke passed for guild {0}: {1} roles, {2} channels, {3} lint findings, {4} sampled effective permissions; Administrator={5}; AutoMod={6}; events={7}; threads={8}; bans={9}; invites={10}; integrations={11}; webhooks={12}; audit={13}; emojis={14}; stickers={15}; soundboard={16}; operations={17}.",
            guildId,
            roles.GetArrayLength(),
            channels.GetArrayLength(),
            findingCount,
            effectivePermissionCount,
            hasAdministrator,
            autoModItems.GetArrayLength(),
            eventItems.GetArrayLength(),
            threadItems.GetArrayLength(),
            bans.RootElement.GetProperty("data").GetArrayLength(),
            invites.RootElement.GetProperty("data").GetArrayLength(),
            integrations.RootElement.GetProperty("data").GetArrayLength(),
            webhooks.RootElement.GetProperty("data").GetArrayLength(),
            audit.RootElement.GetProperty("data").GetArrayLength(),
            emojis.RootElement.GetProperty("data").GetProperty("items").GetArrayLength(),
            stickers.RootElement.GetProperty("data").GetProperty("items").GetArrayLength(),
            soundboard.RootElement.GetProperty("data").GetProperty("items").GetArrayLength(),
            operations.RootElement.GetProperty("data").GetArrayLength());
    }


    [Fact]
    [Trait("Category", "LiveDiscordUpdate")]
    public async Task AuthorizedDirectUpdate_WritesOnceVerifiesAndReplaysFromJournal()
    {
        var authorized = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_UPDATE_AUTHORIZED");
        var guildId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_UPDATE_GUILD_ID");
        var channelId = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_UPDATE_CHANNEL_ID");
        var topic = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_UPDATE_TOPIC");
        var clearTopic = string.Equals(
            Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_UPDATE_CLEAR_TOPIC"),
            "yes",
            StringComparison.Ordinal);
        var slowModeText = Environment.GetEnvironmentVariable("DISCORD_STEWARD_LIVE_UPDATE_SLOW_MODE_SECONDS");
        if (!string.Equals(authorized, "yes", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(guildId) ||
            string.IsNullOrWhiteSpace(channelId) ||
            (topic is null && !clearTopic && slowModeText is null) ||
            (topic is not null && clearTopic) ||
            (slowModeText is not null && !int.TryParse(
                slowModeText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _)))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await CreateClientAsync(guildId, timeout.Token);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        string stateDigest;
        using (var channel = await CallOkAsync(
                   tools["get_channel"],
                   new Dictionary<string, object?> { ["channel_id"] = channelId },
                   timeout.Token))
        {
            stateDigest = channel.RootElement.GetProperty("data").GetProperty("mutationStateDigest").GetString()!;
        }

        var requestId = Guid.CreateVersion7().ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        var set = new Dictionary<string, object?>();
        if (topic is not null)
        {
            set["topic"] = topic;
        }

        if (slowModeText is not null)
        {
            set["slowModeSeconds"] = int.Parse(
                slowModeText,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        var arguments = new Dictionary<string, object?>
        {
            ["request_id"] = requestId,
            ["channel_id"] = channelId,
            ["reason"] = "Owner-authorized direct update smoke test",
            ["expected_state_digest"] = stateDigest,
            ["set"] = set,
            ["clear"] = clearTopic ? new[] { "topic" } : Array.Empty<string>()
        };

        string? completedAt;
        using (var updated = await CallAsync(tools["update_channel"], arguments, timeout.Token))
        {
            var root = updated.RootElement;
            Assert.Equal("succeeded", root.GetProperty("outcome").GetString());
            var result = root.GetProperty("data");
            Assert.Equal(requestId, result.GetProperty("requestId").GetString());
            Assert.True(result.GetProperty("verified").GetBoolean());
        }

        using (var evidence = await CallOkAsync(
                   tools["get_operation"],
                   new Dictionary<string, object?> { ["request_id"] = requestId },
                   timeout.Token))
        {
            var operation = evidence.RootElement.GetProperty("data");
            Assert.Equal("succeeded", operation.GetProperty("status").GetString());
            completedAt = operation.GetProperty("completedAt").GetString();
        }

        using (var replay = await CallAsync(tools["update_channel"], arguments, timeout.Token))
        {
            Assert.Equal("succeeded", replay.RootElement.GetProperty("outcome").GetString());
        }

        using (var evidence = await CallOkAsync(
                   tools["get_operation"],
                   new Dictionary<string, object?> { ["request_id"] = requestId },
                   timeout.Token))
        {
            Assert.Equal(
                completedAt,
                evidence.RootElement.GetProperty("data").GetProperty("completedAt").GetString());
        }

        output.WriteLine("Direct update smoke verified request {0} on channel {1}.", requestId, channelId);
    }

    private static async Task<JsonDocument> CallOkAsync(
        McpClientTool tool,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        var document = await CallAsync(tool, arguments, cancellationToken);
        var root = document.RootElement;
        var outcome = root.GetProperty("outcome").GetString();
        if (!string.Equals(outcome, "ok", StringComparison.Ordinal))
        {
            var error = root.GetProperty("error");
            var code = error.GetProperty("code").GetString();
            var message = error.GetProperty("message").GetString();
            var correlationId = error.GetProperty("correlationId").GetString();
            document.Dispose();
            Assert.Fail($"{tool.Name} failed safely with {code}: {message} Correlation ID {correlationId}.");
        }

        return document;
    }

    private static async Task<JsonDocument> CallMutationSucceededAsync(
        McpClientTool tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var document = await CallAsync(tool, arguments, cancellationToken);
        var root = document.RootElement;
        var outcome = root.GetProperty("outcome").GetString();
        if (!string.Equals(outcome, "succeeded", StringComparison.Ordinal))
        {
            var errorCode = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? error.GetProperty("code").GetString()
                : "none";
            var errorMessage = error.ValueKind == JsonValueKind.Object
                ? error.GetProperty("message").GetString()
                : "none";
            document.Dispose();
            Assert.Fail($"{tool.Name} did not succeed; outcome={outcome}, error={errorCode}, message={errorMessage}.");
        }

        return document;
    }

    private static async Task<JsonDocument> CallMutationErrorAsync(
        McpClientTool tool,
        IReadOnlyDictionary<string, object?> arguments,
        string expectedErrorCode,
        CancellationToken cancellationToken)
    {
        var document = await CallAsync(tool, arguments, cancellationToken);
        var root = document.RootElement;
        Assert.NotEqual("succeeded", root.GetProperty("outcome").GetString());
        Assert.Equal(expectedErrorCode, root.GetProperty("error").GetProperty("code").GetString());
        return document;
    }

    private static async Task<JsonDocument> CallAsync(
        McpClientTool tool,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        try
        {
            return JsonDocument.Parse(content.Text);
        }
        catch (JsonException)
        {
            Assert.Fail($"{tool.Name} returned non-JSON MCP content: {content.Text[..Math.Min(content.Text.Length, 500)]}");
            throw;
        }
    }

    private static async Task<McpClient> CreateClientAsync(
        string guildId,
        CancellationToken cancellationToken,
        string[]? toolNames = null,
        bool sensitiveReads = false,
        bool allWriteGates = false,
        string? localDataRoot = null,
        string? profilePath = null)
    {
        var repositoryRoot = GetRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        var serverAssembly = Path.Combine(
            repositoryRoot,
            "src",
            "DiscordSteward",
            "bin",
            configuration,
            "net10.0",
            "DiscordSteward.dll");

        Assert.True(File.Exists(serverAssembly), $"Build the server before running the live smoke test: {serverAssembly}");

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["DOTNET_ENVIRONMENT"] = "Development";
        environment["Discord__GuildId"] = guildId;
        environment.Remove("Discord__BotToken");
        if (profilePath is not null)
        {
            environment["Steward__ProfilePath"] = profilePath;
        }
        if (toolNames is not null)
        {
            for (var index = 0; index < 32; index++)
            {
                environment[$"toolAllowlist__{index}"] = string.Empty;
            }

            for (var index = 0; index < toolNames.Length; index++)
            {
                environment[$"toolAllowlist__{index}"] = toolNames[index];
            }

            environment["riskGates__sensitiveReads"] = sensitiveReads ? "true" : "false";
            environment["riskGates__reversibleWrites"] = allWriteGates ? "true" : "false";
            environment["riskGates__personImpactingWrites"] = allWriteGates ? "true" : "false";
            environment["riskGates__irreversibleWrites"] = allWriteGates ? "true" : "false";
        }

        if (localDataRoot is not null)
        {
            environment["Steward__DataPath"] = Path.Combine(localDataRoot, "operations.db");
            environment["Steward__AssetInboxPath"] = Path.Combine(localDataRoot, "inbox");
            environment["Steward__AssetVaultPath"] = Path.Combine(localDataRoot, "assets");
            environment["Steward__WebhookSecretVaultPath"] = Path.Combine(localDataRoot, "webhook-secrets");
        }

        return await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Discord Steward authorized target smoke test",
                Command = "dotnet",
                Arguments = [serverAssembly],
                WorkingDirectory = repositoryRoot,
                InheritEnvironmentVariables = false,
                EnvironmentVariables = environment
            }),
            cancellationToken: cancellationToken);
    }

    private static string GetRepositoryRoot([CallerFilePath] string? sourceFile = null)
    {
        var testProjectDirectory = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException("Could not determine the test project directory.");

        return Path.GetFullPath(Path.Combine(testProjectDirectory, "..", ".."));
    }
}
