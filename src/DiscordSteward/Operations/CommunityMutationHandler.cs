using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Permissions;

namespace DiscordSteward.Operations;

internal sealed class CommunityMutationHandler(
    DiscordApiTransport transport,
    IDiscordGuildReader guildReader,
    IDiscordExtendedReader extendedReader,
    IDiscordCatalogReader catalogReader,
    IAssetVault assetVault,
    StewardRuntimeSettings settings,
    TimeProvider timeProvider) : ICatalogMutationFamilyHandler
{
    private static readonly ImmutableHashSet<string> ToolNames = new[]
    {
        "update_guild_metadata",
        "update_guild_system_routing",
        "update_guild_safety",
        "update_guild_access_controls",
        "update_guild_widget",
        "update_welcome_screen",
        "update_onboarding",
        "set_incident_actions"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken) => invocation.ToolName switch
        {
            "update_guild_metadata" => await PrepareMetadataAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_system_routing" => await PrepareRoutingAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_safety" => await PrepareSafetyAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_access_controls" => await PrepareAccessControlsAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_widget" => await PrepareWidgetAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_welcome_screen" => await PrepareWelcomeAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_onboarding" => await PrepareOnboardingAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_incident_actions" => await PrepareIncidentAsync(invocation, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response = invocation.ToolName switch
        {
            "update_guild_metadata" => await WriteMetadataAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_system_routing" => await WriteRoutingAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_safety" => await WriteSafetyAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_guild_access_controls" => await WriteAccessControlsAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
            "update_guild_widget" => await WriteWidgetAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_welcome_screen" => await WriteWelcomeAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
            "update_onboarding" => await WriteOnboardingAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
            "set_incident_actions" => await WriteIncidentAsync(invocation, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };
        return new CatalogMutationWriteReceipt(response?.GetRawText());
    }

    public async Task<CatalogMutationObservation> ObserveAsync(
        CatalogMutationInvocation invocation,
        string beforeStateJson,
        string desiredStateJson,
        string? priorReceiptJson,
        CancellationToken cancellationToken)
    {
        return invocation.ToolName switch
        {
            "update_guild_metadata" => ObserveMetadata(
                await GetGuildStateAsync(cancellationToken).ConfigureAwait(false),
                beforeStateJson,
                desiredStateJson),
            "update_guild_system_routing" or "update_guild_safety" => Observation(
                await GetGuildStateAsync(cancellationToken).ConfigureAwait(false),
                beforeStateJson,
                desiredStateJson),
            "update_guild_access_controls" => ObserveAccessControls(
                await GetGuildStateAsync(cancellationToken).ConfigureAwait(false),
                beforeStateJson,
                desiredStateJson),
            "update_guild_widget" => Observation(
                await catalogReader.GetGuildWidgetAsync(cancellationToken).ConfigureAwait(false),
                beforeStateJson,
                desiredStateJson),
            "update_welcome_screen" => ObserveWelcome(
                await extendedReader.GetWelcomeScreenAsync(cancellationToken).ConfigureAwait(false),
                beforeStateJson,
                desiredStateJson),
            "update_onboarding" => ObserveOnboarding(
                await extendedReader.GetOnboardingAsync(cancellationToken).ConfigureAwait(false),
                beforeStateJson,
                desiredStateJson),
            "set_incident_actions" => ObserveIncident(
                await catalogReader.GetIncidentStatusAsync(cancellationToken).ConfigureAwait(false),
                beforeStateJson,
                desiredStateJson),
            _ => throw Unsupported()
        };
    }

    private async Task<CatalogMutationPreparation> PrepareMetadataAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildMetadataInput>(invocation);
        var current = await GetGuildStateAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        var clear = NormalizeClear(input.Clear, ["description", "icon", "banner", "splash", "discovery_splash"]);
        ValidateSetAndClear(input.Set, clear);
        await ValidateImageAssetAsync(input.Set.IconAssetId, maximumBytes: 10 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        await ValidateImageAssetAsync(input.Set.BannerAssetId, maximumBytes: 10 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        await ValidateImageAssetAsync(input.Set.SplashAssetId, maximumBytes: 10 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        await ValidateImageAssetAsync(input.Set.DiscoverySplashAssetId, maximumBytes: 10 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        var desired = new GuildMetadataExpectation(
            input.Set.Name is null ? current.Name : NormalizeText(input.Set.Name, 100, false),
            clear.Contains("description") ? null : input.Set.Description is null ? current.Description : NormalizeText(input.Set.Description, 1_024, true),
            input.Set.PreferredLocale is null ? current.PreferredLocale : NormalizeLocale(input.Set.PreferredLocale),
            input.Set.PremiumProgressBarEnabled ?? current.PremiumProgressBarEnabled,
            AssetExpectation(current.IconHash, input.Set.IconAssetId, clear.Contains("icon")),
            AssetExpectation(current.BannerHash, input.Set.BannerAssetId, clear.Contains("banner")),
            AssetExpectation(current.SplashHash, input.Set.SplashAssetId, clear.Contains("splash")),
            AssetExpectation(current.DiscoverySplashHash, input.Set.DiscoverySplashAssetId, clear.Contains("discovery_splash")));
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareRoutingAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildSystemRoutingInput>(invocation);
        var current = await GetGuildStateAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        if (input.AfkTimeoutSeconds is not 60 and not 300 and not 900 and not 1_800 and not 3_600)
        {
            throw InvalidArgument("AFK timeout must be one of Discord's documented values.");
        }

        if (input.SystemChannelFlags is < 0 or > 63)
        {
            throw InvalidArgument("System channel flags contain unsupported bits.");
        }

        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        ValidateChannelReference(snapshot, input.AfkChannelId, voiceRequired: true);
        ValidateChannelReference(snapshot, input.SystemChannelId, voiceRequired: false);
        ValidateChannelReference(snapshot, input.RulesChannelId, voiceRequired: false);
        ValidateChannelReference(snapshot, input.PublicUpdatesChannelId, voiceRequired: false);
        ValidateChannelReference(snapshot, input.SafetyAlertsChannelId, voiceRequired: false);
        var desired = current with
        {
            AfkChannelId = NormalizeOptionalId(input.AfkChannelId),
            AfkTimeout = input.AfkTimeoutSeconds,
            SystemChannelId = NormalizeOptionalId(input.SystemChannelId),
            SystemChannelFlags = input.SystemChannelFlags,
            RulesChannelId = NormalizeOptionalId(input.RulesChannelId),
            PublicUpdatesChannelId = NormalizeOptionalId(input.PublicUpdatesChannelId),
            SafetyAlertsChannelId = NormalizeOptionalId(input.SafetyAlertsChannelId)
        };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareSafetyAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildSafetyInput>(invocation);
        var current = await GetGuildStateAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.ComputeDigest());
        if (input.VerificationLevel is < 0 or > 4 ||
            input.DefaultMessageNotifications is < 0 or > 1 ||
            input.ExplicitContentFilter is < 0 or > 2)
        {
            throw InvalidArgument("Guild safety levels are outside Discord's documented enum ranges.");
        }

        return new CatalogMutationPreparation(
            State(current),
            State(current with
            {
                VerificationLevel = input.VerificationLevel,
                DefaultMessageNotifications = input.DefaultMessageNotifications,
                ExplicitContentFilter = input.ExplicitContentFilter
            }));
    }

    private async Task<CatalogMutationPreparation> PrepareAccessControlsAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildAccessControlsInput>(invocation);
        var current = await GetGuildStateAsync(cancellationToken).ConfigureAwait(false);
        var featureDigest = ResourceStateCanonicalizer.ComputeObjectDigest(
            current.Features.Order(StringComparer.Ordinal).ToImmutableArray());
        EnsureDigest(input.ExpectedFeaturesStateDigest, featureDigest);
        var desiredFeatures = current.Features.ToHashSet(StringComparer.OrdinalIgnoreCase);
        SetFeature(desiredFeatures, "INVITES_DISABLED", input.InvitesDisabled);
        SetFeature(desiredFeatures, "RAID_ALERTS_DISABLED", input.RaidAlertsDisabled);
        var before = GuildAccessControlsState.FromFeatures(current.Features);
        var desired = new GuildAccessControlsState(
            input.InvitesDisabled,
            input.RaidAlertsDisabled,
            desiredFeatures.Order(StringComparer.Ordinal).ToImmutableArray());
        if (before.InvitesDisabled == desired.InvitesDisabled && before.RaidAlertsDisabled == desired.RaidAlertsDisabled)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Guild access controls already match the requested state.");
        }

        return new CatalogMutationPreparation(State(before), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareWidgetAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildWidgetInput>(invocation);
        var current = await catalogReader.GetGuildWidgetAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        var channelId = NormalizeOptionalId(input.ChannelId);
        if (channelId is not null)
        {
            ValidateChannelReference(await GetSnapshotAsync(cancellationToken).ConfigureAwait(false), channelId, voiceRequired: false);
        }

        var desiredBase = new GuildWidgetView(input.Enabled, channelId);
        var desired = desiredBase with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(desiredBase) };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareWelcomeAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateWelcomeScreenInput>(invocation);
        await EnsureCommunityGuildAsync(cancellationToken).ConfigureAwait(false);
        var current = await extendedReader.GetWelcomeScreenAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        if (input.Channels.IsDefault || input.Channels.Length > 5)
        {
            throw InvalidArgument("Welcome screen supports at most five exact channels.");
        }

        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var channels = input.Channels.Select(channel => NormalizeWelcomeChannel(channel, snapshot)).ToImmutableArray();
        if (channels.Select(channel => channel.ChannelId).Distinct(StringComparer.Ordinal).Count() != channels.Length)
        {
            throw InvalidArgument("Welcome screen channel IDs must be unique.");
        }

        var desiredBase = new WelcomeScreenView(
            GuildId(),
            input.Enabled,
            input.Description is null ? null : NormalizeText(input.Description, 1_024, true),
            channels.Select(channel => new WelcomeScreenChannelView(
                channel.ChannelId,
                channel.Description,
                channel.EmojiId ?? channel.EmojiName)).ToImmutableArray());
        var desired = desiredBase with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(desiredBase) };
        return new CatalogMutationPreparation(State(current), State(new WelcomeExpectation(desired, channels)));
    }

    private async Task<CatalogMutationPreparation> PrepareOnboardingAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateOnboardingInput>(invocation);
        await EnsureCommunityGuildAsync(cancellationToken).ConfigureAwait(false);
        var current = await extendedReader.GetOnboardingAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        if (input.Prompts.IsDefault || input.Prompts.Length > 5)
        {
            throw InvalidArgument("Onboarding supports at most five prompts.");
        }

        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var defaults = NormalizeIds(input.DefaultChannelIds, 500);
        if (defaults.Any(id => !snapshot.Channels.Any(channel => channel.Id == id)))
        {
            throw InvalidArgument("Onboarding default channels must exist in the configured guild.");
        }

        var prompts = input.Prompts.Select(prompt => NormalizePrompt(prompt, snapshot)).ToImmutableArray();
        var mode = NormalizeOnboardingMode(input.Mode);
        if (input.Enabled)
        {
            var publicSendable = defaults.Count(id =>
            {
                var explanation = PermissionExplainer.ExplainRole(snapshot, snapshot.Id, id);
                return explanation.EffectivePermissions.Contains("ViewChannel", StringComparer.Ordinal) &&
                    explanation.EffectivePermissions.Contains("SendMessages", StringComparer.Ordinal);
            });
            if (defaults.Length < 7 || publicSendable < 5)
            {
                throw InvalidArgument("Enabled onboarding requires at least 7 defaults and 5 public sendable defaults.");
            }
        }

        var expectation = new OnboardingExpectation(GuildId(), prompts, defaults, input.Enabled, mode);
        return new CatalogMutationPreparation(State(current), State(expectation));
    }

    private async Task<CatalogMutationPreparation> PrepareIncidentAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetIncidentActionsInput>(invocation);
        var current = await catalogReader.GetIncidentStatusAsync(cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, current.StateDigest);
        var invitesDisabledUntil = NormalizeIncidentTimestamp(input.InvitesDisabledUntil);
        var directMessagesDisabledUntil = NormalizeIncidentTimestamp(input.DirectMessagesDisabledUntil);
        var desiredBase = new IncidentStatusView(
            invitesDisabledUntil,
            directMessagesDisabledUntil,
            current.DirectMessageSpamDetectedAt,
            current.RaidDetectedAt);
        var desired = desiredBase with { StateDigest = ResourceStateCanonicalizer.ComputeObjectDigest(desiredBase) };
        return new CatalogMutationPreparation(State(current), State(desired));
    }

    private async Task<JsonElement?> WriteMetadataAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildMetadataInput>(invocation);
        var clear = input.Clear.Select(value => value.Trim().ToLowerInvariant()).ToImmutableHashSet(StringComparer.Ordinal);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (input.Set.Name is not null) body["name"] = NormalizeText(input.Set.Name, 100, false);
        if (input.Set.Description is not null) body["description"] = NormalizeText(input.Set.Description, 1_024, true);
        if (input.Set.PreferredLocale is not null) body["preferred_locale"] = NormalizeLocale(input.Set.PreferredLocale);
        if (input.Set.PremiumProgressBarEnabled.HasValue) body["premium_progress_bar_enabled"] = input.Set.PremiumProgressBarEnabled.Value;
        await AddAssetAsync(body, "icon", input.Set.IconAssetId, cancellationToken).ConfigureAwait(false);
        await AddAssetAsync(body, "banner", input.Set.BannerAssetId, cancellationToken).ConfigureAwait(false);
        await AddAssetAsync(body, "splash", input.Set.SplashAssetId, cancellationToken).ConfigureAwait(false);
        await AddAssetAsync(body, "discovery_splash", input.Set.DiscoverySplashAssetId, cancellationToken).ConfigureAwait(false);
        foreach (var field in clear) body[field] = null;
        return await PatchGuildAsync(invocation, body, cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonElement?> WriteRoutingAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildSystemRoutingInput>(invocation);
        return PatchGuildAsync(invocation, new Dictionary<string, object?>
        {
            ["afk_channel_id"] = NormalizeOptionalId(input.AfkChannelId),
            ["afk_timeout"] = input.AfkTimeoutSeconds,
            ["system_channel_id"] = NormalizeOptionalId(input.SystemChannelId),
            ["system_channel_flags"] = input.SystemChannelFlags,
            ["rules_channel_id"] = NormalizeOptionalId(input.RulesChannelId),
            ["public_updates_channel_id"] = NormalizeOptionalId(input.PublicUpdatesChannelId),
            ["safety_alerts_channel_id"] = NormalizeOptionalId(input.SafetyAlertsChannelId)
        }, cancellationToken);
    }

    private Task<JsonElement?> WriteSafetyAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildSafetyInput>(invocation);
        return PatchGuildAsync(invocation, new Dictionary<string, object?>
        {
            ["verification_level"] = input.VerificationLevel,
            ["default_message_notifications"] = input.DefaultMessageNotifications,
            ["explicit_content_filter"] = input.ExplicitContentFilter
        }, cancellationToken);
    }

    private Task<JsonElement?> WriteAccessControlsAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        _ = Arguments<UpdateGuildAccessControlsInput>(invocation);
        var desired = DeserializeState<GuildAccessControlsState>(preparation.DesiredStateJson);
        return PatchGuildAsync(invocation, new Dictionary<string, object?>
        {
            ["features"] = desired.Features
        }, cancellationToken);
    }

    private Task<JsonElement?> WriteWidgetAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateGuildWidgetInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/widget",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["enabled"] = input.Enabled,
                ["channel_id"] = NormalizeOptionalId(input.ChannelId)
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteWelcomeAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        _ = Arguments<UpdateWelcomeScreenInput>(invocation);
        var desired = DeserializeState<WelcomeExpectation>(preparation.DesiredStateJson);
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/welcome-screen",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["enabled"] = desired.View.IsEnabled,
                ["description"] = desired.View.Description,
                ["welcome_channels"] = desired.Inputs.Select(channel => new Dictionary<string, object?>
                {
                    ["channel_id"] = NormalizeSnowflake(channel.ChannelId),
                    ["description"] = NormalizeText(channel.Description, 1_024, false),
                    ["emoji_id"] = channel.EmojiId is null ? null : NormalizeSnowflake(channel.EmojiId),
                    ["emoji_name"] = channel.EmojiName
                }).ToArray()
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteOnboardingAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        _ = Arguments<UpdateOnboardingInput>(invocation);
        var desired = DeserializeState<OnboardingExpectation>(preparation.DesiredStateJson);
        return transport.SendJsonAsync(
            HttpMethod.Put,
            $"guilds/{GuildId()}/onboarding",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["prompts"] = desired.Prompts.Select(PromptPayload).ToArray(),
                ["default_channel_ids"] = desired.DefaultChannelIds,
                ["enabled"] = desired.Enabled,
                ["mode"] = desired.Mode == "Advanced" ? 1 : 0
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteIncidentAsync(CatalogMutationInvocation invocation, CancellationToken cancellationToken)
    {
        var input = Arguments<SetIncidentActionsInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Put,
            $"guilds/{GuildId()}/incident-actions",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["invites_disabled_until"] = NormalizeIncidentTimestamp(input.InvitesDisabledUntil),
                ["dms_disabled_until"] = NormalizeIncidentTimestamp(input.DirectMessagesDisabledUntil)
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> PatchGuildAsync(
        CatalogMutationInvocation invocation,
        Dictionary<string, object?> body,
        CancellationToken cancellationToken) => transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}",
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);

    private async Task<GuildApiState> GetGuildStateAsync(CancellationToken cancellationToken) =>
        GuildApiState.Parse(await transport.GetJsonAsync($"guilds/{GuildId()}", cancellationToken).ConfigureAwait(false));

    private async Task EnsureCommunityGuildAsync(CancellationToken cancellationToken)
    {
        var guild = await GetGuildStateAsync(cancellationToken).ConfigureAwait(false);
        if (!guild.Features.Contains("COMMUNITY", StringComparer.OrdinalIgnoreCase))
        {
            throw InvalidArgument("Welcome-screen and onboarding mutations require the Community guild feature.");
        }
    }

    private async Task<GuildSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Id == GuildId() ? snapshot : throw new StewardException(StewardErrorCodes.GuildMismatch, "Discord returned a different guild.");
    }

    private async Task AddAssetAsync(
        Dictionary<string, object?> body,
        string field,
        string? assetId,
        CancellationToken cancellationToken)
    {
        if (assetId is null) return;
        var asset = await assetVault.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        var path = await assetVault.GetContentPathAsync(assetId, cancellationToken).ConfigureAwait(false);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        body[field] = $"data:{asset.MediaType};base64,{Convert.ToBase64String(bytes)}";
    }

    private async Task ValidateImageAssetAsync(string? assetId, long maximumBytes, CancellationToken cancellationToken)
    {
        if (assetId is null) return;
        var asset = await assetVault.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (!asset.MediaType.StartsWith("image/", StringComparison.Ordinal) || asset.ByteLength > maximumBytes)
        {
            throw InvalidArgument("Guild branding assets must be bounded image assets.");
        }
    }

    private static WelcomeChannelInput NormalizeWelcomeChannel(WelcomeChannelInput input, GuildSnapshot snapshot)
    {
        var id = NormalizeSnowflake(input.ChannelId);
        if (!snapshot.Channels.Any(channel => channel.Id == id)) throw InvalidArgument("Welcome channel does not exist.");
        if (input.EmojiId is not null && input.EmojiName is not null) throw InvalidArgument("Welcome emoji ID and name are mutually exclusive.");
        return input with
        {
            ChannelId = id,
            Description = NormalizeText(input.Description, 1_024, false),
            EmojiId = input.EmojiId is null ? null : NormalizeSnowflake(input.EmojiId),
            EmojiName = input.EmojiName is null ? null : NormalizeText(input.EmojiName, 100, false)
        };
    }

    private static OnboardingPromptInput NormalizePrompt(OnboardingPromptInput input, GuildSnapshot snapshot)
    {
        if (input.Options.IsDefault || input.Options.IsEmpty || input.Options.Length > 50) throw InvalidArgument("Onboarding prompts require 1 to 50 options.");
        return input with
        {
            Id = input.Id is null ? null : NormalizeSnowflake(input.Id),
            Title = NormalizeText(input.Title, 100, false),
            Type = input.Type.Trim().ToLowerInvariant() switch
            {
                "multiplechoice" or "multiple_choice" => "MultipleChoice",
                "dropdown" => "Dropdown",
                _ => throw InvalidArgument("Onboarding prompt type is invalid.")
            },
            Options = input.Options.Select(option => NormalizeOption(option, snapshot)).ToImmutableArray()
        };
    }

    private static OnboardingOptionInput NormalizeOption(OnboardingOptionInput input, GuildSnapshot snapshot)
    {
        if (input.EmojiId is not null && input.EmojiName is not null) throw InvalidArgument("Onboarding emoji ID and name are mutually exclusive.");
        var roleIds = NormalizeIds(input.RoleIds, 100);
        var channelIds = NormalizeIds(input.ChannelIds, 100);
        if (roleIds.Any(id => !snapshot.Roles.Any(role => role.Id == id)) ||
            channelIds.Any(id => !snapshot.Channels.Any(channel => channel.Id == id)))
        {
            throw InvalidArgument("Onboarding option references a missing role or channel.");
        }

        return input with
        {
            Id = input.Id is null ? null : NormalizeSnowflake(input.Id),
            Title = NormalizeText(input.Title, 100, false),
            Description = input.Description is null ? null : NormalizeText(input.Description, 1_024, true),
            EmojiId = input.EmojiId is null ? null : NormalizeSnowflake(input.EmojiId),
            EmojiName = input.EmojiName is null ? null : NormalizeText(input.EmojiName, 100, false),
            RoleIds = roleIds,
            ChannelIds = channelIds
        };
    }

    private static Dictionary<string, object?> PromptPayload(OnboardingPromptInput prompt)
    {
        var result = new Dictionary<string, object?>
        {
            ["type"] = prompt.Type == "Dropdown" ? 1 : 0,
            ["title"] = prompt.Title,
            ["single_select"] = prompt.SingleSelect,
            ["required"] = prompt.Required,
            ["in_onboarding"] = prompt.InOnboarding,
            ["options"] = prompt.Options.Select(OptionPayload).ToArray()
        };
        if (prompt.Id is not null) result["id"] = prompt.Id;
        return result;
    }

    private static Dictionary<string, object?> OptionPayload(OnboardingOptionInput option)
    {
        var result = new Dictionary<string, object?>
        {
            ["title"] = option.Title,
            ["description"] = option.Description,
            ["emoji_id"] = option.EmojiId,
            ["emoji_name"] = option.EmojiName,
            ["emoji_animated"] = option.EmojiAnimated,
            ["role_ids"] = option.RoleIds,
            ["channel_ids"] = option.ChannelIds
        };
        if (option.Id is not null) result["id"] = option.Id;
        return result;
    }

    private DateTimeOffset? NormalizeIncidentTimestamp(DateTimeOffset? value)
    {
        if (!value.HasValue) return null;
        var now = timeProvider.GetUtcNow();
        if (value.Value <= now || value.Value > now.AddHours(24))
        {
            throw InvalidArgument("Incident action timestamps must be future values no more than 24 hours away.");
        }

        var utc = value.Value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private static void ValidateChannelReference(GuildSnapshot snapshot, string? value, bool voiceRequired)
    {
        if (value is null) return;
        var id = NormalizeSnowflake(value);
        var channel = snapshot.Channels.SingleOrDefault(item => item.Id == id) ?? throw InvalidArgument("Guild routing channel does not exist.");
        if (voiceRequired && channel.Kind is not "voice" and not "stage") throw InvalidArgument("AFK channel must be voice or stage.");
    }

    private static void SetFeature(HashSet<string> features, string name, bool enabled)
    {
        var current = features.SingleOrDefault(feature => string.Equals(feature, name, StringComparison.OrdinalIgnoreCase));
        if (current is not null) features.Remove(current);
        if (enabled) features.Add(name);
    }

    private static CatalogMutationObservation ObserveAccessControls(
        GuildApiState observed,
        string beforeJson,
        string desiredJson)
    {
        var state = GuildAccessControlsState.FromFeatures(observed.Features);
        var before = DeserializeState<GuildAccessControlsState>(beforeJson);
        var desired = DeserializeState<GuildAccessControlsState>(desiredJson);
        return new CatalogMutationObservation(
            state.InvitesDisabled == desired.InvitesDisabled && state.RaidAlertsDisabled == desired.RaidAlertsDisabled
                ? CatalogMutationObservationKind.DesiredState
                : state.InvitesDisabled == before.InvitesDisabled && state.RaidAlertsDisabled == before.RaidAlertsDisabled
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
            State(state));
    }

    private static void ValidateSetAndClear(GuildMetadataSet set, ImmutableHashSet<string> clear)
    {
        if (set.Description is not null && clear.Contains("description") || set.IconAssetId is not null && clear.Contains("icon") ||
            set.BannerAssetId is not null && clear.Contains("banner") || set.SplashAssetId is not null && clear.Contains("splash") ||
            set.DiscoverySplashAssetId is not null && clear.Contains("discovery_splash"))
        {
            throw InvalidArgument("The same guild metadata field cannot be set and cleared.");
        }
    }

    private static string? AssetExpectation(string? currentHash, string? assetId, bool clear) =>
        clear ? null : assetId is null ? currentHash : "__present__";

    private static string NormalizeLocale(string value)
    {
        var locale = value?.Trim() ?? string.Empty;
        return locale.Length is >= 2 and <= 32 && locale.All(character => char.IsAsciiLetter(character) || character == '-')
            ? locale
            : throw InvalidArgument("Preferred locale is invalid.");
    }

    private static string NormalizeOnboardingMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "default" or "onboarding_default" => "Default",
        "advanced" or "onboarding_advanced" => "Advanced",
        _ => throw InvalidArgument("Onboarding mode must be default or advanced.")
    };

    private static string NormalizeText(string value, int maximum, bool allowEmpty)
    {
        var text = value ?? string.Empty;
        return (allowEmpty || !string.IsNullOrWhiteSpace(text)) && text.Length <= maximum &&
            !text.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t')
            ? text
            : throw InvalidArgument("Text is empty, too long, or contains invalid control characters.");
    }

    private static ImmutableArray<string> NormalizeIds(ImmutableArray<string> values, int maximum)
    {
        if (values.IsDefault || values.Length > maximum) throw InvalidArgument("ID array exceeds its local limit.");
        return values.Select(NormalizeSnowflake).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableHashSet<string> NormalizeClear(ImmutableArray<string> values, IEnumerable<string> allowed)
    {
        if (values.IsDefault) return ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
        var result = values.Select(value => value.Trim().ToLowerInvariant()).ToImmutableHashSet(StringComparer.Ordinal);
        return result.IsSubsetOf(allowed) ? result : throw InvalidArgument("Clear list contains an unsupported field.");
    }

    private static string? NormalizeOptionalId(string? value) => value is null ? null : NormalizeSnowflake(value);
    private static string NormalizeSnowflake(string value) => ChannelApiState.NormalizeSnowflake(value);

    private static CatalogMutationObservation ObserveMetadata(GuildApiState observed, string beforeJson, string desiredJson)
    {
        var desired = DeserializeState<GuildMetadataExpectation>(desiredJson);
        if (desired.Matches(observed)) return new CatalogMutationObservation(CatalogMutationObservationKind.DesiredState, State(observed));
        return Observation(observed, beforeJson, desiredJson);
    }

    private static CatalogMutationObservation ObserveWelcome(WelcomeScreenView observed, string beforeJson, string desiredJson)
    {
        var desired = DeserializeState<WelcomeExpectation>(desiredJson);
        return new CatalogMutationObservation(
            desired.Matches(observed) ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(State(observed), beforeJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            State(observed));
    }

    private static CatalogMutationObservation ObserveOnboarding(OnboardingView observed, string beforeJson, string desiredJson)
    {
        var desired = DeserializeState<OnboardingExpectation>(desiredJson);
        return new CatalogMutationObservation(
            desired.Matches(observed) ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(State(observed), beforeJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            State(observed));
    }

    private static CatalogMutationObservation ObserveIncident(IncidentStatusView observed, string beforeJson, string desiredJson)
    {
        var desired = DeserializeState<IncidentStatusView>(desiredJson);
        var matches = observed.InvitesDisabledUntil == desired.InvitesDisabledUntil &&
            observed.DirectMessagesDisabledUntil == desired.DirectMessagesDisabledUntil;
        return new CatalogMutationObservation(
            matches ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(State(observed), beforeJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            State(observed));
    }

    private static CatalogMutationObservation Observation<T>(T observed, string beforeJson, string desiredJson)
    {
        var json = State(observed);
        return new CatalogMutationObservation(
            JsonEqual(json, desiredJson) ? CatalogMutationObservationKind.DesiredState :
                JsonEqual(json, beforeJson) ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
            json);
    }

    private static bool JsonEqual(string left, string right)
    {
        using var a = JsonDocument.Parse(left);
        using var b = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(a.RootElement, b.RootElement);
    }

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options) ?? throw InvalidArgument("Mutation arguments are invalid.");
    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options) ?? throw InvalidResponse();
    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static void EnsureDigest(string? supplied, string observed)
    {
        var value = supplied?.Trim().ToLowerInvariant();
        if (value is not { Length: 64 } || !value.All(char.IsAsciiHexDigit)) throw InvalidArgument("Expected digest is invalid.");
        if (value != observed) throw new StewardException(StewardErrorCodes.StateConflict, "Guild/community state changed after approval.");
    }

    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);
    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);
    private static StewardException InvalidResponse() => new(StewardErrorCodes.DiscordUnavailable, "Discord returned invalid community state.");
    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "Community mutation is not supported.");

    private sealed record GuildMetadataExpectation(
        string Name,
        string? Description,
        string? PreferredLocale,
        bool PremiumProgressBarEnabled,
        string? IconHash,
        string? BannerHash,
        string? SplashHash,
        string? DiscoverySplashHash)
    {
        internal bool Matches(GuildApiState state) =>
            state.Name == Name && state.Description == Description && state.PreferredLocale == PreferredLocale &&
            state.PremiumProgressBarEnabled == PremiumProgressBarEnabled &&
            HashMatches(IconHash, state.IconHash) && HashMatches(BannerHash, state.BannerHash) &&
            HashMatches(SplashHash, state.SplashHash) && HashMatches(DiscoverySplashHash, state.DiscoverySplashHash);

        private static bool HashMatches(string? expected, string? actual) => expected == "__present__" ? actual is not null : expected == actual;
    }

    private sealed record GuildAccessControlsState(
        bool InvitesDisabled,
        bool RaidAlertsDisabled,
        ImmutableArray<string> Features)
    {
        internal static GuildAccessControlsState FromFeatures(ImmutableArray<string> features) => new(
            features.Contains("INVITES_DISABLED", StringComparer.OrdinalIgnoreCase),
            features.Contains("RAID_ALERTS_DISABLED", StringComparer.OrdinalIgnoreCase),
            features.Order(StringComparer.Ordinal).ToImmutableArray());
    }

    private sealed record WelcomeExpectation(
        WelcomeScreenView View,
        ImmutableArray<WelcomeChannelInput> Inputs)
    {
        internal bool Matches(WelcomeScreenView observed) =>
            observed.GuildId == View.GuildId && observed.IsEnabled == View.IsEnabled && observed.Description == View.Description &&
            Inputs.Length == observed.Channels.Length && Inputs.Zip(observed.Channels).All(pair =>
                pair.First.ChannelId == pair.Second.ChannelId && pair.First.Description == pair.Second.Description &&
                (pair.First.EmojiId ?? pair.First.EmojiName) == pair.Second.Emoji);
    }

    private sealed record OnboardingExpectation(
        string GuildId,
        ImmutableArray<OnboardingPromptInput> Prompts,
        ImmutableArray<string> DefaultChannelIds,
        bool Enabled,
        string Mode)
    {
        internal bool Matches(OnboardingView observed) =>
            observed.GuildId == GuildId && observed.IsEnabled == Enabled &&
            NormalizeObservedMode(observed.Mode) == Mode &&
            observed.DefaultChannelIds.SequenceEqual(DefaultChannelIds, StringComparer.Ordinal) &&
            Prompts.Length == observed.Prompts.Length && Prompts.Zip(observed.Prompts).All(pair =>
                (pair.First.Id is null || pair.First.Id == pair.Second.Id) &&
                pair.First.Title == pair.Second.Title && NormalizeObservedPromptType(pair.Second.Type) == pair.First.Type &&
                pair.First.SingleSelect == pair.Second.IsSingleSelect && pair.First.Required == pair.Second.IsRequired &&
                pair.First.InOnboarding == pair.Second.IsInOnboarding && pair.First.Options.Length == pair.Second.Options.Length &&
                pair.First.Options.Zip(pair.Second.Options).All(option =>
                    (option.First.Id is null || option.First.Id == option.Second.Id) &&
                    option.First.Title == option.Second.Title && option.First.Description == option.Second.Description &&
                    EmojiMatches(option.First, option.Second.Emoji) &&
                    option.First.RoleIds.SequenceEqual(option.Second.RoleIds, StringComparer.Ordinal) &&
                    option.First.ChannelIds.SequenceEqual(option.Second.ChannelIds, StringComparer.Ordinal)));

        private static string NormalizeObservedMode(string value) =>
            value.Contains("Advanced", StringComparison.OrdinalIgnoreCase) ? "Advanced" : "Default";

        private static string NormalizeObservedPromptType(string value) =>
            value.Contains("Dropdown", StringComparison.OrdinalIgnoreCase) ? "Dropdown" : "MultipleChoice";

        private static bool EmojiMatches(OnboardingOptionInput input, string? observed)
        {
            if (input.EmojiId is null && input.EmojiName is null) return observed is null;
            if (input.EmojiName is not null) return observed == input.EmojiName || observed?.Contains(input.EmojiName, StringComparison.Ordinal) == true;
            return observed == input.EmojiId || observed?.Contains(input.EmojiId!, StringComparison.Ordinal) == true;
        }
    }
}
