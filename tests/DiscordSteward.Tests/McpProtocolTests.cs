using System.Runtime.CompilerServices;
using System.Text.Json;
using DiscordSteward.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DiscordSteward.Tests;

public sealed class McpProtocolTests
{
    [Fact]
    public async Task Server_AllToolsProfileRegistersCompleteCatalog()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await CreateClientAsync(
            timeout.Token,
            profilePath: "config/profiles/all-tools.example.json");

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(StewardToolManifest.DefinedNames.Length, tools.Count);
        Assert.Equal(
            StewardToolManifest.DefinedNames.ToArray(),
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray());

        var capabilitiesTool = tools.Single(tool => tool.Name == "get_steward_capabilities");
        var result = await capabilitiesTool.CallAsync(cancellationToken: timeout.Token);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(content.Text);
        var root = document.RootElement;
        Assert.Equal("example-all-tools", root.GetProperty("profile").GetString());
        Assert.Equal(173, root.GetProperty("registeredTools").GetArrayLength());
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());
        var gates = root.GetProperty("riskGates");
        Assert.True(gates.GetProperty("sensitiveReads").GetBoolean());
        Assert.True(gates.GetProperty("reversibleWrites").GetBoolean());
        Assert.True(gates.GetProperty("personImpactingWrites").GetBoolean());
        Assert.True(gates.GetProperty("irreversibleWrites").GetBoolean());
    }

    [Fact]
    public async Task Server_UnrestrictedAutonomyProfileRegistersCompleteCatalog()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await CreateClientAsync(
            timeout.Token,
            profilePath: "config/profiles/unrestricted-autonomy.example.json");

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(StewardToolManifest.DefinedNames.ToArray(), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray());

        var capabilitiesTool = tools.Single(tool => tool.Name == "get_steward_capabilities");
        var result = await capabilitiesTool.CallAsync(cancellationToken: timeout.Token);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(content.Text);
        var root = document.RootElement;
        Assert.Equal("example-unrestricted-autonomy", root.GetProperty("profile").GetString());
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("profileDigest").GetString());
        Assert.Equal("UnrestrictedAutonomy", root.GetProperty("authorizationMode").GetString());
        Assert.Equal("unrestricted", root.GetProperty("mode").GetString());
        Assert.Equal(StewardToolManifest.DefinedNames.Length, root.GetProperty("registeredTools").GetArrayLength());
        Assert.Equal(0, root.GetProperty("policy").GetProperty("protectedResourceCount").GetInt32());
        Assert.Equal(0, root.GetProperty("policy").GetProperty("protectedNamePrefixCount").GetInt32());
        Assert.Equal(0, root.GetProperty("policy").GetProperty("deniedPermissionCount").GetInt32());
        var gates = root.GetProperty("riskGates");
        Assert.True(gates.GetProperty("sensitiveReads").GetBoolean());
        Assert.True(gates.GetProperty("reversibleWrites").GetBoolean());
        Assert.True(gates.GetProperty("personImpactingWrites").GetBoolean());
        Assert.True(gates.GetProperty("irreversibleWrites").GetBoolean());
    }

    [Fact]
    public async Task Server_ProfilePathAtomicallySelectsExampleObserver()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await CreateClientAsync(
            timeout.Token,
            profilePath: "config/profiles/observer.example.json");

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(29, tools.Count);
        Assert.Contains(tools, tool => tool.Name == "get_steward_capabilities");
        Assert.Contains(tools, tool => tool.Name == "get_guild");
        Assert.DoesNotContain(tools, tool => tool.Name == "update_channel");
        Assert.DoesNotContain(tools, tool => tool.Name == "list_bans");

        var capabilitiesTool = tools.Single(tool => tool.Name == "get_steward_capabilities");
        var result = await capabilitiesTool.CallAsync(cancellationToken: timeout.Token);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(content.Text);
        var root = document.RootElement;
        Assert.Equal("example-observer", root.GetProperty("profile").GetString());
        Assert.Equal("100000000000000001", root.GetProperty("guildId").GetString());
        Assert.Equal("unconfigured-observer", root.GetProperty("mode").GetString());
        Assert.False(root.GetProperty("discordTokenConfigured").GetBoolean());
        Assert.False(root.GetProperty("riskGates").GetProperty("reversibleWrites").GetBoolean());
    }

    [Fact]
    public async Task Server_DefaultObserverManifestListsOnlyTargetReadTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await CreateClientAsync(timeout.Token);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(
            [
                "analyze_channel_access",
                "analyze_moderation_configuration",
                "analyze_onboarding_configuration",
                "analyze_privilege_escalation_paths",
                "analyze_role_hierarchy",
                "capture_guild_configuration_snapshot",
                "compare_guild_configuration",
                "explain_role_permissions",
                "get_bot_effective_permissions",
                "get_bot_guild_profile",
                "get_channel",
                "get_guild",
                "get_guild_configuration_snapshot",
                "get_guild_preview",
                "get_guild_widget_image",
                "get_onboarding",
                "get_role",
                "get_steward_capabilities",
                "get_sticker_pack",
                "get_welcome_screen",
                "lint_guild_configuration",
                "list_automod_rules",
                "list_channels",
                "list_default_soundboard_sounds",
                "list_guild_expressions",
                "list_roles",
                "list_scheduled_events",
                "list_sticker_packs",
                "list_voice_regions"
            ],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));

        foreach (var tool in tools)
        {
            var isLocalRead = tool.Name == "get_steward_capabilities";
            var isCapture = tool.Name == "capture_guild_configuration_snapshot";
            Assert.Equal(!isCapture, tool.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint);
            Assert.Equal(!isCapture, tool.ProtocolTool.Annotations?.IdempotentHint);
            Assert.Equal(!isLocalRead, tool.ProtocolTool.Annotations?.OpenWorldHint);

            var propertyNames = GetInputPropertyNames(tool);
            Assert.DoesNotContain(propertyNames, property =>
                property.Contains("guild", StringComparison.OrdinalIgnoreCase) ||
                property.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                property.Contains("route", StringComparison.OrdinalIgnoreCase) ||
                property.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                property.Contains("mutation", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(["role_id"], GetInputPropertyNames(tools.Single(tool => tool.Name == "get_role")));
        Assert.Equal(["channel_id"], GetInputPropertyNames(tools.Single(tool => tool.Name == "get_channel")));
        var explainTool = tools.Single(tool => tool.Name == "explain_role_permissions");
        Assert.Equal(["channel_id", "role_id"], GetInputPropertyNames(explainTool));
        Assert.Equal(["role_id"], GetRequiredPropertyNames(explainTool));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "get_guild_configuration_snapshot")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "get_guild")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "list_channels")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "list_roles")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "list_automod_rules")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "get_onboarding")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "get_welcome_screen")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "list_scheduled_events")));
        var expressions = tools.Single(tool => tool.Name == "list_guild_expressions");
        Assert.Equal(["expression_type"], GetInputPropertyNames(expressions));
        Assert.Equal(["expression_type"], GetRequiredPropertyNames(expressions));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "lint_guild_configuration")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "get_steward_capabilities")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "get_bot_effective_permissions")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "analyze_role_hierarchy")));
        Assert.Equal(
            ["channel_id"],
            GetInputPropertyNames(tools.Single(tool => tool.Name == "analyze_channel_access")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "analyze_privilege_escalation_paths")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "analyze_moderation_configuration")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "analyze_onboarding_configuration")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "capture_guild_configuration_snapshot")));
        var compareTool = tools.Single(tool => tool.Name == "compare_guild_configuration");
        Assert.Equal(["snapshot_id"], GetInputPropertyNames(compareTool));
        Assert.Equal(["snapshot_id"], GetRequiredPropertyNames(compareTool));

        var snapshotTool = tools.Single(tool => tool.Name == "get_guild_configuration_snapshot");
        var result = await snapshotTool.CallAsync(cancellationToken: timeout.Token);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var document = JsonDocument.Parse(content.Text);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("error", root.GetProperty("outcome").GetString());
        Assert.Equal("not_configured", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(
            root.GetProperty("operationId").GetString(),
            root.GetProperty("error").GetProperty("correlationId").GetString());
        Assert.DoesNotContain("Discord__BotToken", content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_DirectWriteManifestExposesCompleteUpdateWithoutLegacyTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["toolAllowlist__0"] = "get_steward_capabilities",
            ["toolAllowlist__1"] = "get_channel",
            ["toolAllowlist__2"] = "get_operation",
            ["toolAllowlist__3"] = "reconcile_operation",
            ["toolAllowlist__4"] = "update_channel",
            ["toolAllowlist__5"] = "list_channel_member_overwrites",
            ["riskGates__sensitiveReads"] = "true",
            ["riskGates__reversibleWrites"] = "true"
        };
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(
            [
                "get_channel",
                "get_operation",
                "get_steward_capabilities",
                "list_channel_member_overwrites",
                "reconcile_operation",
                "update_channel"
            ],
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("plan", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("approval", StringComparison.Ordinal));

        var update = tools.Single(tool => tool.Name == "update_channel");
        Assert.False(update.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(update.ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(update.ProtocolTool.Annotations?.IdempotentHint);
        Assert.True(update.ProtocolTool.Annotations?.OpenWorldHint);
        Assert.Equal(
            ["channel_id", "clear", "expected_state_digest", "reason", "request_id", "set"],
            GetInputPropertyNames(update));
        Assert.Equal(
            ["channel_id", "expected_state_digest", "reason", "request_id", "set"],
            GetRequiredPropertyNames(update));

        var properties = update.ProtocolTool.InputSchema.GetProperty("properties");
        var setProperties = properties.GetProperty("set").GetProperty("properties");
        Assert.Equal(
            [
                "availableTags",
                "bitrate",
                "channelType",
                "defaultAutoArchiveMinutes",
                "defaultForumLayout",
                "defaultReaction",
                "defaultSortOrder",
                "defaultThreadSlowModeSeconds",
                "hideMediaDownloadOptions",
                "isNsfw",
                "isSpoiler",
                "name",
                "requireTag",
                "rtcRegion",
                "slowModeSeconds",
                "topic",
                "userLimit",
                "videoQualityMode"
            ],
            setProperties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        foreach (var prohibited in new[] { "approved", "force", "guild_id", "plan_id", "route", "skip_checks" })
        {
            Assert.False(properties.TryGetProperty(prohibited, out _), prohibited);
        }

        var memberOverwrites = tools.Single(tool => tool.Name == "list_channel_member_overwrites");
        Assert.True(memberOverwrites.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Equal(
            ["after_member_id", "channel_id", "limit"],
            GetInputPropertyNames(memberOverwrites));
        Assert.Equal(["channel_id"], GetRequiredPropertyNames(memberOverwrites));
    }

    [Fact]
    public async Task Server_MemberReadManifestExposesOnlyBoundedSensitiveReads()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var names = new[]
        {
            "get_steward_capabilities",
            "get_member",
            "search_members",
            "list_members",
            "list_members_by_role",
            "get_member_voice_state",
            "get_role_member_counts",
            "preview_member_prune"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__sensitiveReads"] = "true";
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.DoesNotContain(tools, tool => tool.Name.StartsWith("set_member", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name is "kick_member" or "ban_member");

        Assert.Equal(["user_id"], GetRequiredPropertyNames(tools.Single(tool => tool.Name == "get_member")));
        Assert.Equal(["limit", "query"], GetInputPropertyNames(tools.Single(tool => tool.Name == "search_members")));
        Assert.Equal(["query"], GetRequiredPropertyNames(tools.Single(tool => tool.Name == "search_members")));
        Assert.Equal(
            ["after_user_id", "limit", "role_id"],
            GetInputPropertyNames(tools.Single(tool => tool.Name == "list_members_by_role")));
        Assert.Equal(
            ["inactivity_days", "role_ids"],
            GetInputPropertyNames(tools.Single(tool => tool.Name == "preview_member_prune")));
        Assert.Equal(
            ["inactivity_days"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "preview_member_prune")));
    }

    [Fact]
    public async Task Server_CatalogReadManifestExposesExactGatedSchemas()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var names = new[]
        {
            "get_guild_widget",
            "get_incident_status",
            "get_guild_vanity_url",
            "list_guild_templates",
            "get_guild_template",
            "list_thread_members",
            "list_scheduled_event_subscribers",
            "get_stage_instance",
            "list_invite_target_users",
            "get_invite_target_user_job_status"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__sensitiveReads"] = "true";
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "get_guild_widget")));
        Assert.Equal(["code"], GetRequiredPropertyNames(tools.Single(tool => tool.Name == "get_guild_template")));
        Assert.Equal(
            ["after_user_id", "limit", "thread_id"],
            GetInputPropertyNames(tools.Single(tool => tool.Name == "list_thread_members")));
        Assert.Equal(
            ["event_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "list_scheduled_event_subscribers")));
        Assert.Equal(
            ["code", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "get_invite_target_user_job_status")));
    }

    [Fact]
    public async Task Server_ReferenceReadManifestExposesExactTypedSchemas()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var names = new[]
        {
            "list_voice_regions",
            "get_thread_member",
            "list_channel_invites",
            "list_sticker_packs",
            "get_sticker_pack",
            "list_default_soundboard_sounds",
            "get_guild_preview",
            "get_guild_widget_image"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__sensitiveReads"] = "true";
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.Equal(["scope"], GetRequiredPropertyNames(tools.Single(tool => tool.Name == "list_voice_regions")));
        Assert.Equal(
            ["thread_id", "user_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "get_thread_member")));
        Assert.Equal(["channel_id"], GetRequiredPropertyNames(tools.Single(tool => tool.Name == "list_channel_invites")));
        Assert.Empty(GetInputPropertyNames(tools.Single(tool => tool.Name == "list_sticker_packs")));
        Assert.Equal(["pack_id"], GetRequiredPropertyNames(tools.Single(tool => tool.Name == "get_sticker_pack")));
        Assert.Empty(GetRequiredPropertyNames(tools.Single(tool => tool.Name == "get_guild_widget_image")));
    }

    [Fact]
    public async Task Server_MessageReadManifestExposesOnlyBoundedReadTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var names = new[]
        {
            "get_message",
            "list_messages",
            "search_messages",
            "list_channel_pins",
            "list_message_reactions",
            "list_poll_voters"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__sensitiveReads"] = "true";
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.Equal(
            ["channel_id", "message_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "get_message")));
        Assert.Equal(
            ["after_message_id", "around_message_id", "before_message_id", "channel_id", "limit"],
            GetInputPropertyNames(tools.Single(tool => tool.Name == "list_messages")));
        Assert.Equal(
            ["answer_id", "channel_id", "message_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "list_poll_voters")));
        Assert.Contains("channel_ids", GetInputPropertyNames(tools.Single(tool => tool.Name == "search_messages")));
        Assert.DoesNotContain(tools, tool => tool.Name is "send_message" or "delete_message");
    }

    [Fact]
    public async Task Server_LocalCatalogManifestNeedsNoDiscordRiskGate()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var names = new[]
        {
            "register_asset",
            "list_assets",
            "get_asset",
            "delete_asset",
            "simulate_automod_rule"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["byte_length", "candidate_id", "expected_digest", "media_type"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "register_asset")));
        Assert.True(tools.Single(tool => tool.Name == "delete_asset").ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(tools.Single(tool => tool.Name == "simulate_automod_rule").ProtocolTool.Annotations?.OpenWorldHint);
    }

    [Fact]
    public async Task Server_RoleMutationManifestExposesExactRiskAnnotatedSchemas()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var names = new[]
        {
            "create_role",
            "update_role_appearance",
            "set_role_permissions",
            "reorder_roles",
            "set_member_role",
            "delete_role"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__reversibleWrites"] = "true";
        environment["riskGates__personImpactingWrites"] = "true";
        environment["riskGates__irreversibleWrites"] = "true";
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint));
        Assert.True(tools.Single(tool => tool.Name == "delete_role").ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(tools.Single(tool => tool.Name == "create_role").ProtocolTool.Annotations?.DestructiveHint);
        Assert.Equal(
            ["expected_roles_state_digest", "name", "permissions", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "create_role")));
        Assert.Contains("secondary_color_hex", GetInputPropertyNames(tools.Single(tool => tool.Name == "create_role")));
        Assert.Contains("tertiary_color_hex", GetInputPropertyNames(tools.Single(tool => tool.Name == "create_role")));
        Assert.Equal(
            ["expected_member_state_digest", "present", "reason", "request_id", "role_id", "user_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "set_member_role")));
        Assert.Equal(
            ["expected_cascade_digest", "expected_state_digest", "reason", "request_id", "role_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "delete_role")));
    }

    [Fact]
    public async Task Server_ChannelMutationManifestExposesAllTwentySixExactTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var names = new[]
        {
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
            "set_role_channel_permissions",
            "remove_role_channel_permissions",
            "set_member_channel_permissions",
            "remove_member_channel_permissions",
            "replace_channel_permission_overwrites",
            "archive_channel",
            "restore_archived_channel",
            "set_voice_channel_status",
            "trigger_typing_indicator",
            "follow_announcement_channel",
            "create_thread",
            "create_forum_post",
            "update_thread",
            "set_thread_member",
            "delete_thread",
            "delete_channel"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__reversibleWrites"] = "true";
        environment["riskGates__personImpactingWrites"] = "true";
        environment["riskGates__irreversibleWrites"] = "true";
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.IdempotentHint));
        Assert.True(tools.Single(tool => tool.Name == "delete_thread").ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(tools.Single(tool => tool.Name == "delete_channel").ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(tools.Single(tool => tool.Name == "trigger_typing_indicator").ProtocolTool.Annotations?.DestructiveHint);
        Assert.Equal(
            ["input", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "create_forum_channel")));
        Assert.Equal(
            ["channel_id", "input", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "replace_channel_permission_overwrites")));
        Assert.Equal(
            ["input", "reason", "request_id", "thread_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "delete_thread")));
        Assert.Equal(
            ["channel_id", "input", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "trigger_typing_indicator")));
    }

    [Fact]
    public async Task Server_MemberMutationManifestSeparatesPersonImpactAndIrreversibleTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var names = new[]
        {
            "set_member_nickname",
            "set_member_timeout",
            "set_member_voice_channel",
            "update_member_voice_state",
            "set_member_verification_bypass",
            "kick_member",
            "ban_member",
            "bulk_ban_members",
            "unban_member"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__personImpactingWrites"] = "true";
        environment["riskGates__irreversibleWrites"] = "true";
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.True(tools.Single(tool => tool.Name == "kick_member").ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(tools.Single(tool => tool.Name == "ban_member").ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(tools.Single(tool => tool.Name == "bulk_ban_members").ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(tools.Single(tool => tool.Name == "unban_member").ProtocolTool.Annotations?.DestructiveHint);
        Assert.Equal(
            ["input", "reason", "request_id", "user_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "set_member_timeout")));
        Assert.Equal(
            ["input", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "bulk_ban_members")));
    }

    [Fact]
    public async Task Server_MessageMutationManifestExposesAllElevenExactTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var names = new[]
        {
            "send_message",
            "edit_own_message",
            "delete_message",
            "bulk_delete_messages",
            "crosspost_announcement_message",
            "set_message_pinned",
            "set_own_reaction",
            "remove_user_reaction",
            "clear_message_reactions",
            "create_poll",
            "end_poll"
        };
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__personImpactingWrites"] = "true";
        environment["riskGates__irreversibleWrites"] = "true";
        await using var client = await CreateClientAsync(timeout.Token, environment);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.True(tools.Single(tool => tool.Name == "delete_message").ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(tools.Single(tool => tool.Name == "remove_user_reaction").ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(tools.Single(tool => tool.Name == "send_message").ProtocolTool.Annotations?.DestructiveHint);
        Assert.Equal(
            ["channel_id", "input", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "send_message")));
        Assert.Equal(
            ["channel_id", "input", "message_id", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "end_poll")));
    }

    [Fact]
    public async Task Server_GovernanceEventExpressionManifestExposesAllTwentyFiveTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var names = new[]
        {
            "create_automod_rule",
            "update_automod_rule",
            "set_automod_rule_enabled",
            "delete_automod_rule",
            "update_guild_metadata",
            "update_guild_system_routing",
            "update_guild_safety",
            "update_guild_widget",
            "update_welcome_screen",
            "update_onboarding",
            "set_incident_actions",
            "create_scheduled_event",
            "update_scheduled_event",
            "start_scheduled_event",
            "complete_scheduled_event",
            "cancel_scheduled_event",
            "delete_scheduled_event",
            "start_stage_instance",
            "update_stage_instance",
            "end_stage_instance",
            "set_stage_speaker",
            "request_stage_speaking",
            "create_guild_expression",
            "update_guild_expression",
            "delete_guild_expression"
        };
        var tools = await ListToolsForAllGatesAsync(names, timeout.Token);

        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.True(tools.Single(tool => tool.Name == "delete_automod_rule").ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(tools.Single(tool => tool.Name == "complete_scheduled_event").ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(tools.Single(tool => tool.Name == "update_stage_instance").ProtocolTool.Annotations?.DestructiveHint);
        Assert.Equal(
            ["input", "reason", "request_id", "rule_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "update_automod_rule")));
        Assert.Equal(
            ["channel_id", "input", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "set_stage_speaker")));
    }

    [Fact]
    public async Task Server_IntegrationMutationManifestExposesAllFourteenTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var names = new[]
        {
            "create_invite",
            "create_targeted_invite",
            "update_invite_target_users",
            "delete_invite",
            "create_webhook",
            "update_webhook",
            "delete_webhook",
            "send_webhook_message",
            "edit_webhook_message",
            "delete_webhook_message",
            "create_guild_template",
            "sync_guild_template",
            "update_guild_template",
            "delete_guild_template"
        };
        var tools = await ListToolsForAllGatesAsync(names, timeout.Token);

        Assert.Equal(names.Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.True(tools.Single(tool => tool.Name == "delete_webhook_message").ProtocolTool.Annotations?.DestructiveHint);
        Assert.True(tools.Single(tool => tool.Name == "delete_guild_template").ProtocolTool.Annotations?.DestructiveHint);
        Assert.False(tools.Single(tool => tool.Name == "send_webhook_message").ProtocolTool.Annotations?.DestructiveHint);
        Assert.Equal(
            ["input", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "create_webhook")));
        Assert.Equal(
            ["code", "input", "reason", "request_id"],
            GetRequiredPropertyNames(tools.Single(tool => tool.Name == "sync_guild_template")));
    }

    private static async Task<IList<McpClientTool>> ListToolsForAllGatesAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var environment = names
            .Select((name, index) => new KeyValuePair<string, string?>($"toolAllowlist__{index}", name))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        environment["riskGates__sensitiveReads"] = "true";
        environment["riskGates__reversibleWrites"] = "true";
        environment["riskGates__personImpactingWrites"] = "true";
        environment["riskGates__irreversibleWrites"] = "true";
        await using var client = await CreateClientAsync(cancellationToken, environment);
        return await client.ListToolsAsync(cancellationToken: cancellationToken);
    }

    private static string[] GetInputPropertyNames(McpClientTool tool)
    {
        var schema = tool.ProtocolTool.InputSchema;
        return schema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray()
            : [];
    }

    private static string[] GetRequiredPropertyNames(McpClientTool tool)
    {
        var schema = tool.ProtocolTool.InputSchema;
        return schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(property => property.GetString()!).Order(StringComparer.Ordinal).ToArray()
            : [];
    }

    private static async Task<McpClient> CreateClientAsync(
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
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

        Assert.True(File.Exists(serverAssembly), $"Build the server before running the protocol test: {serverAssembly}");

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["DOTNET_ENVIRONMENT"] = "Production";
        environment["Discord__BotToken"] = string.Empty;
        if (profilePath is null)
        {
            environment["Discord__GuildId"] = "100000000000000001";
            environment["Steward__PolicyPath"] = "config/nonexistent-test-policy.json";
        }
        else
        {
            environment["Steward__ProfilePath"] = profilePath;
        }
        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
            {
                environment[key] = value;
            }
        }

        return await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Discord Steward protocol test",
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
