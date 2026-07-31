using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Assets;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using DiscordSteward.Permissions;
using global::Discord;

namespace DiscordSteward.Operations;

internal sealed class RoleMutationHandler(
    DiscordApiTransport transport,
    IDiscordGuildReader guildReader,
    IDiscordMemberReader memberReader,
    IDiscordBotInspector botInspector,
    ConfigurationAnalysisService analysisService,
    IAssetVault assetVault,
    StewardPolicy policy,
    StewardRuntimeSettings settings) : ICatalogMutationFamilyHandler
{
    private static readonly ImmutableHashSet<string> ToolNames = new[]
    {
        "create_role",
        "update_role_appearance",
        "set_role_permissions",
        "reorder_roles",
        "set_member_role",
        "delete_role"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        EnsureGuild(snapshot);
        return invocation.ToolName switch
        {
            "create_role" => await PrepareCreateAsync(invocation, snapshot, cancellationToken).ConfigureAwait(false),
            "update_role_appearance" => await PrepareAppearanceAsync(invocation, snapshot, cancellationToken).ConfigureAwait(false),
            "set_role_permissions" => await PreparePermissionsAsync(invocation, snapshot, cancellationToken).ConfigureAwait(false),
            "reorder_roles" => await PrepareReorderAsync(invocation, snapshot, cancellationToken).ConfigureAwait(false),
            "set_member_role" => await PrepareMemberRoleAsync(invocation, snapshot, cancellationToken).ConfigureAwait(false),
            "delete_role" => await PrepareDeleteAsync(invocation, snapshot, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };
    }

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response = invocation.ToolName switch
        {
            "create_role" => await WriteCreateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_role_appearance" => await WriteAppearanceAsync(invocation, preparation, cancellationToken).ConfigureAwait(false),
            "set_role_permissions" => await WritePermissionsAsync(invocation, cancellationToken).ConfigureAwait(false),
            "reorder_roles" => await WriteReorderAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_member_role" => await WriteMemberRoleAsync(invocation, cancellationToken).ConfigureAwait(false),
            "delete_role" => await transport.SendJsonAsync(
                HttpMethod.Delete,
                $"guilds/{GuildId()}/roles/{NormalizeSnowflake(invocation.ResourceId)}",
                body: null,
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false),
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
        if (invocation.ToolName == "set_member_role")
        {
            var input = Arguments<SetMemberRoleInput>(invocation);
            var member = await memberReader.GetMemberAsync(input.UserId, cancellationToken).ConfigureAwait(false);
            var observed = MemberRoleState.FromMember(member);
            return Observation(observed, beforeStateJson, desiredStateJson);
        }

        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        EnsureGuild(snapshot);
        if (invocation.ToolName == "create_role")
        {
            var createdId = CreatedRoleId(priorReceiptJson);
            var role = snapshot.Roles.SingleOrDefault(item => string.Equals(item.Id, createdId, StringComparison.Ordinal));
            if (role is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.UnexpectedState,
                    State(new RoleVerificationState(createdId, Exists: false, null, [], null, false, false, null, null, null)));
            }

            var desired = DeserializeState<RoleVerificationState>(desiredStateJson);
            var observed = RoleVerificationState.FromRole(role);
            var kind = observed.MatchesWithoutIdentity(desired)
                ? CatalogMutationObservationKind.DesiredState
                : CatalogMutationObservationKind.UnexpectedState;
            return new CatalogMutationObservation(kind, State(observed));
        }

        if (invocation.ToolName == "reorder_roles")
        {
            var observed = new RoleOrderState(
                ResourceStateCanonicalizer.ComputeRolesDigest(snapshot.Roles),
                snapshot.Roles
                    .Where(role => !role.IsEveryone)
                    .Select(role => new RolePositionInput(role.Id, role.Position))
                    .OrderBy(item => item.RoleId, StringComparer.Ordinal)
                    .ToImmutableArray());
            var before = DeserializeState<RoleOrderState>(beforeStateJson);
            var desired = DeserializeState<RoleOrderState>(desiredStateJson);
            var desiredPositions = desired.Positions.ToDictionary(item => item.RoleId, item => item.Position, StringComparer.Ordinal);
            var positionsMatch = desiredPositions.All(pair =>
                observed.Positions.Any(item => item.RoleId == pair.Key && item.Position == pair.Value));
            var kind = positionsMatch
                ? CatalogMutationObservationKind.DesiredState
                : string.Equals(observed.RolesStateDigest, before.RolesStateDigest, StringComparison.Ordinal)
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState;
            return new CatalogMutationObservation(kind, State(observed));
        }

        var current = snapshot.Roles.SingleOrDefault(role => string.Equals(role.Id, invocation.ResourceId, StringComparison.Ordinal));
        if (invocation.ToolName == "delete_role")
        {
            if (current is null)
            {
                return new CatalogMutationObservation(
                    CatalogMutationObservationKind.DesiredState,
                    State(new RoleVerificationState(invocation.ResourceId, Exists: false, null, [], null, false, false, null, null, null)));
            }

            var observed = RoleVerificationState.FromRole(current);
            var before = DeserializeState<RoleVerificationState>(beforeStateJson);
            return new CatalogMutationObservation(
                observed == before
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (current is null)
        {
            return new CatalogMutationObservation(
                CatalogMutationObservationKind.UnexpectedState,
                State(new RoleVerificationState(invocation.ResourceId, Exists: false, null, [], null, false, false, null, null, null)));
        }

        var observedRole = RoleVerificationState.FromRole(current);
        var beforeRole = DeserializeState<RoleVerificationState>(beforeStateJson);
        var desiredRole = DeserializeState<RoleVerificationState>(desiredStateJson);
        return new CatalogMutationObservation(
            observedRole.MatchesDesired(desiredRole)
                ? CatalogMutationObservationKind.DesiredState
                : observedRole == beforeRole
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
            State(observedRole));
    }

    private async Task<CatalogMutationPreparation> PrepareCreateAsync(
        CatalogMutationInvocation invocation,
        GuildSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateRoleInput>(invocation);
        EnsureDigest(input.ExpectedRolesStateDigest, ResourceStateCanonicalizer.ComputeRolesDigest(snapshot.Roles));
        var name = NormalizeName(input.Name);
        var permissions = NormalizePermissions(input.Permissions);
        if (!policy.IsUnrestricted && permissions.Contains("Administrator", StringComparer.Ordinal))
        {
            throw PolicyDenied("Create roles without Administrator, then use the separately gated permission tool after review.");
        }

        var colors = NormalizeRoleColors(
            input.ColorHex,
            input.SecondaryColorHex,
            input.TertiaryColorHex,
            snapshot.Features);
        await ValidateIconAsync(input.IconAssetId, input.UnicodeEmoji, cancellationToken).ConfigureAwait(false);
        var before = new RoleOrderState(
            ResourceStateCanonicalizer.ComputeRolesDigest(snapshot.Roles),
            snapshot.Roles.Where(role => !role.IsEveryone)
                .Select(role => new RolePositionInput(role.Id, role.Position))
                .OrderBy(item => item.RoleId, StringComparer.Ordinal)
                .ToImmutableArray());
        var desired = new RoleVerificationState(
            Id: null,
            Exists: true,
            name,
            permissions,
            colors.Primary,
            input.IsHoisted,
            input.IsMentionable,
            Position: null,
            input.IconAssetId is null ? null : "__present__",
            input.UnicodeEmoji,
            colors.Secondary,
            colors.Tertiary);
        return new CatalogMutationPreparation(State(before), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareAppearanceAsync(
        CatalogMutationInvocation invocation,
        GuildSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateRoleAppearanceInput>(invocation);
        var role = await GetManageableRoleAsync(snapshot, invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, role.StateDigest);
        var clear = NormalizeClear(input.Clear, ["color", "colors", "secondary_color", "tertiary_color", "icon", "unicode_emoji"]);
        if (input.Set.IconAssetId is not null && clear.Contains("icon") ||
            input.Set.UnicodeEmoji is not null && clear.Contains("unicode_emoji") ||
            input.Set.ColorHex is not null && (clear.Contains("color") || clear.Contains("colors")) ||
            input.Set.SecondaryColorHex is not null && (clear.Contains("secondary_color") || clear.Contains("colors")) ||
            input.Set.TertiaryColorHex is not null && (clear.Contains("tertiary_color") || clear.Contains("colors")))
        {
            throw InvalidArgument("The same role appearance field cannot be set and cleared.");
        }

        await ValidateIconAsync(input.Set.IconAssetId, input.Set.UnicodeEmoji, cancellationToken).ConfigureAwait(false);
        var before = RoleVerificationState.FromRole(role);
        var clearAllColors = clear.Contains("color") || clear.Contains("colors");
        var colors = NormalizeRoleColors(
            clearAllColors ? null : input.Set.ColorHex ?? before.ColorHex,
            clearAllColors || clear.Contains("secondary_color") ? null : input.Set.SecondaryColorHex ?? before.SecondaryColorHex,
            clearAllColors || clear.Contains("tertiary_color") ? null : input.Set.TertiaryColorHex ?? before.TertiaryColorHex,
            snapshot.Features);
        var desired = before with
        {
            Name = input.Set.Name is null ? before.Name : NormalizeName(input.Set.Name),
            ColorHex = colors.Primary,
            SecondaryColorHex = colors.Secondary,
            TertiaryColorHex = colors.Tertiary,
            IsHoisted = input.Set.IsHoisted ?? before.IsHoisted,
            IsMentionable = input.Set.IsMentionable ?? before.IsMentionable,
            IconHash = clear.Contains("icon")
                ? null
                : input.Set.IconAssetId is null ? before.IconHash : "__present__",
            UnicodeEmoji = clear.Contains("unicode_emoji")
                ? null
                : input.Set.UnicodeEmoji ?? before.UnicodeEmoji
        };
        if (desired.MatchesDesired(before) && input.Set.IconAssetId is null && input.Set.UnicodeEmoji is null &&
            !clear.Contains("icon") && !clear.Contains("unicode_emoji"))
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Role appearance already matches the requested state.");
        }

        return new CatalogMutationPreparation(State(before), State(desired));
    }

    private async Task<CatalogMutationPreparation> PreparePermissionsAsync(
        CatalogMutationInvocation invocation,
        GuildSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetRolePermissionsInput>(invocation);
        var role = await GetManageableRoleAsync(snapshot, invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, role.StateDigest);
        var before = RoleVerificationState.FromRole(role);
        var desired = before with { Permissions = NormalizePermissions(input.Permissions) };
        return new CatalogMutationPreparation(State(before), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareReorderAsync(
        CatalogMutationInvocation invocation,
        GuildSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ReorderRolesInput>(invocation);
        EnsureDigest(input.ExpectedRolesStateDigest, ResourceStateCanonicalizer.ComputeRolesDigest(snapshot.Roles));
        if (input.Positions.IsDefaultOrEmpty || input.Positions.Length > 250)
        {
            throw InvalidArgument("Role reorder requires 1 to 250 exact role positions.");
        }

        var normalized = input.Positions
            .Select(item => new RolePositionInput(NormalizeSnowflake(item.RoleId), item.Position))
            .ToImmutableArray();
        if (normalized.Select(item => item.RoleId).Distinct(StringComparer.Ordinal).Count() != normalized.Length ||
            normalized.Select(item => item.Position).Distinct().Count() != normalized.Length ||
            normalized.Any(item => item.Position < 1))
        {
            throw InvalidArgument("Role reorder IDs and positive positions must be unique.");
        }

        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        var ceiling = bot.HighestRolePosition ?? throw PolicyDenied("The bot's role ceiling is unavailable.");
        foreach (var item in normalized)
        {
            var role = snapshot.Roles.SingleOrDefault(role => role.Id == item.RoleId) ?? throw NotFound();
            EnsureMutable(role);
            if (role.Position >= ceiling || item.Position >= ceiling)
            {
                throw PolicyDenied("Role reorder cannot cross or reach the bot's highest role.");
            }
        }

        EnsureCompleteShiftSet(snapshot.Roles, normalized);
        var before = new RoleOrderState(
            ResourceStateCanonicalizer.ComputeRolesDigest(snapshot.Roles),
            snapshot.Roles.Where(role => !role.IsEveryone)
                .Select(role => new RolePositionInput(role.Id, role.Position))
                .OrderBy(item => item.RoleId, StringComparer.Ordinal)
                .ToImmutableArray());
        var desired = new RoleOrderState(string.Empty, normalized.OrderBy(item => item.RoleId, StringComparer.Ordinal).ToImmutableArray());
        return new CatalogMutationPreparation(State(before), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareMemberRoleAsync(
        CatalogMutationInvocation invocation,
        GuildSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetMemberRoleInput>(invocation);
        var userId = NormalizeSnowflake(input.UserId);
        var role = await GetManageableRoleAsync(snapshot, input.RoleId, cancellationToken).ConfigureAwait(false);
        var member = await memberReader.GetMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        var before = MemberRoleState.FromMember(member);
        var roleIds = member.RoleIds.ToHashSet(StringComparer.Ordinal);
        if (input.Present)
        {
            roleIds.Add(role.Id);
        }
        else
        {
            roleIds.Remove(role.Id);
        }

        var desired = before with { RoleIds = roleIds.Order(StringComparer.Ordinal).ToImmutableArray() };
        return new CatalogMutationPreparation(State(before), State(desired));
    }

    private async Task<CatalogMutationPreparation> PrepareDeleteAsync(
        CatalogMutationInvocation invocation,
        GuildSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var input = Arguments<DeleteRoleInput>(invocation);
        var role = await GetManageableRoleAsync(snapshot, invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedStateDigest, role.StateDigest);
        var impact = await analysisService.AnalyzeDeletionImpactAsync("role", role.Id, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedCascadeDigest, impact.CascadeDigest);
        if (!policy.IsUnrestricted && (!impact.Blockers.IsEmpty || !impact.Cascade.IsEmpty))
        {
            throw PolicyDenied("Role deletion impact is no longer empty; no delete was attempted.");
        }

        return new CatalogMutationPreparation(
            State(RoleVerificationState.FromRole(role)),
            State(new RoleVerificationState(role.Id, Exists: false, null, [], null, false, false, null, null, null)));
    }

    private async Task<JsonElement?> WriteCreateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<CreateRoleInput>(invocation);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = NormalizeName(input.Name),
            ["permissions"] = PermissionBits(input.Permissions),
            ["colors"] = RoleColorsPayload(NormalizeRoleColors(
                input.ColorHex,
                input.SecondaryColorHex,
                input.TertiaryColorHex,
                features: null)),
            ["hoist"] = input.IsHoisted,
            ["mentionable"] = input.IsMentionable
        };
        await AddIconPayloadAsync(payload, input.IconAssetId, input.UnicodeEmoji, cancellationToken).ConfigureAwait(false);
        return await transport.SendJsonAsync(
            HttpMethod.Post,
            $"guilds/{GuildId()}/roles",
            JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement?> WriteAppearanceAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateRoleAppearanceInput>(invocation);
        var clear = input.Clear.ToImmutableHashSet(StringComparer.Ordinal);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (input.Set.Name is not null) payload["name"] = NormalizeName(input.Set.Name);
        if (input.Set.ColorHex is not null || input.Set.SecondaryColorHex is not null || input.Set.TertiaryColorHex is not null ||
            clear.Contains("color") || clear.Contains("colors") || clear.Contains("secondary_color") || clear.Contains("tertiary_color"))
        {
            var desired = DeserializeState<RoleVerificationState>(preparation.DesiredStateJson);
            payload["colors"] = RoleColorsPayload(new RoleColorsState(
                desired.ColorHex,
                desired.SecondaryColorHex,
                desired.TertiaryColorHex));
        }
        if (input.Set.IsHoisted.HasValue) payload["hoist"] = input.Set.IsHoisted.Value;
        if (input.Set.IsMentionable.HasValue) payload["mentionable"] = input.Set.IsMentionable.Value;
        if (clear.Contains("icon")) payload["icon"] = null;
        if (clear.Contains("unicode_emoji")) payload["unicode_emoji"] = null;
        await AddIconPayloadAsync(payload, input.Set.IconAssetId, input.Set.UnicodeEmoji, cancellationToken).ConfigureAwait(false);
        return await transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/roles/{NormalizeSnowflake(invocation.ResourceId)}",
            JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonElement?> WritePermissionsAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetRolePermissionsInput>(invocation);
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/roles/{NormalizeSnowflake(invocation.ResourceId)}",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["permissions"] = PermissionBits(input.Permissions)
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteReorderAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<ReorderRolesInput>(invocation);
        var payload = input.Positions.Select(item => new Dictionary<string, object?>
        {
            ["id"] = NormalizeSnowflake(item.RoleId),
            ["position"] = item.Position
        });
        return transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/roles",
            JsonSerializer.SerializeToElement(payload, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private Task<JsonElement?> WriteMemberRoleAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetMemberRoleInput>(invocation);
        return transport.SendJsonAsync(
            input.Present ? HttpMethod.Put : HttpMethod.Delete,
            $"guilds/{GuildId()}/members/{NormalizeSnowflake(input.UserId)}/roles/{NormalizeSnowflake(input.RoleId)}",
            body: null,
            AuditReason(invocation),
            cancellationToken);
    }

    private async Task<RoleSnapshot> GetManageableRoleAsync(
        GuildSnapshot snapshot,
        string roleId,
        CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeSnowflake(roleId);
        var role = snapshot.Roles.SingleOrDefault(item => item.Id == normalizedId) ?? throw NotFound();
        EnsureMutable(role);
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        if (!bot.EffectivePermissions.Contains("ManageRoles", StringComparer.Ordinal) ||
            !bot.HighestRolePosition.HasValue || role.Position >= bot.HighestRolePosition.Value)
        {
            throw PolicyDenied("The bot cannot manage the requested role under Discord hierarchy rules.");
        }

        return role;
    }

    private void EnsureMutable(RoleSnapshot role)
    {
        if (role.IsEveryone || role.IsManaged ||
            (!policy.IsUnrestricted && (role.IsProtected || policy.GetProtection(role.Id, role.Name).IsProtected)))
        {
            throw PolicyDenied("The requested role is everyone, managed, or protected by Steward policy.");
        }
    }

    private static void EnsureCompleteShiftSet(
        ImmutableArray<RoleSnapshot> roles,
        ImmutableArray<RolePositionInput> positions)
    {
        var supplied = positions.Select(item => item.RoleId).ToHashSet(StringComparer.Ordinal);
        foreach (var requested in positions)
        {
            var current = roles.Single(role => role.Id == requested.RoleId).Position;
            var minimum = Math.Min(current, requested.Position);
            var maximum = Math.Max(current, requested.Position);
            var indirectlyShifted = roles.Where(role => !role.IsEveryone && role.Position >= minimum && role.Position <= maximum);
            if (indirectlyShifted.Any(role => !supplied.Contains(role.Id)))
            {
                throw InvalidArgument("Role reorder omits one or more roles whose positions would shift indirectly.");
            }
        }
    }

    private async Task ValidateIconAsync(
        string? iconAssetId,
        string? unicodeEmoji,
        CancellationToken cancellationToken)
    {
        if (iconAssetId is not null && unicodeEmoji is not null)
        {
            throw InvalidArgument("Role icon asset and Unicode emoji are mutually exclusive.");
        }

        if (unicodeEmoji is { Length: > 100 } || unicodeEmoji?.Any(char.IsControl) == true)
        {
            throw InvalidArgument("Role Unicode emoji is invalid.");
        }

        if (iconAssetId is not null)
        {
            var asset = await assetVault.GetAsync(iconAssetId, cancellationToken).ConfigureAwait(false);
            if (!asset.MediaType.StartsWith("image/", StringComparison.Ordinal) || asset.ByteLength > 256 * 1024)
            {
                throw InvalidArgument("Role icon assets must be images no larger than 256 KiB.");
            }
        }
    }

    private async Task AddIconPayloadAsync(
        Dictionary<string, object?> payload,
        string? iconAssetId,
        string? unicodeEmoji,
        CancellationToken cancellationToken)
    {
        if (iconAssetId is not null)
        {
            var asset = await assetVault.GetAsync(iconAssetId, cancellationToken).ConfigureAwait(false);
            var path = await assetVault.GetContentPathAsync(iconAssetId, cancellationToken).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            payload["icon"] = $"data:{asset.MediaType};base64,{Convert.ToBase64String(bytes)}";
        }

        if (unicodeEmoji is not null)
        {
            payload["unicode_emoji"] = unicodeEmoji;
        }
    }

    private static CatalogMutationObservation Observation<T>(
        T observed,
        string beforeStateJson,
        string desiredStateJson)
    {
        var observedJson = State(observed);
        var kind = JsonEqual(observedJson, desiredStateJson)
            ? CatalogMutationObservationKind.DesiredState
            : JsonEqual(observedJson, beforeStateJson)
                ? CatalogMutationObservationKind.BeforeState
                : CatalogMutationObservationKind.UnexpectedState;
        return new CatalogMutationObservation(kind, observedJson);
    }

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options)
        ?? throw InvalidArgument("Mutation arguments are invalid.");

    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options)
        ?? throw new StewardException(StewardErrorCodes.AuditUnavailable, "Stored mutation state is invalid.");

    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static bool JsonEqual(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
    }

    private static string CreatedRoleId(string? receiptJson)
    {
        if (receiptJson is null)
        {
            throw new StewardException(
                StewardErrorCodes.DiscordUnavailable,
                "Created role ID is unavailable; audit-log reconciliation is required.");
        }

        using var document = JsonDocument.Parse(receiptJson);
        return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? NormalizeSnowflake(id.GetString() ?? string.Empty)
            : throw new StewardException(StewardErrorCodes.DiscordUnavailable, "Discord returned an invalid role receipt.");
    }

    private static ImmutableArray<string> NormalizePermissions(ImmutableArray<string> permissions)
    {
        if (permissions.IsDefault || permissions.Length > PermissionCatalog.All.Count)
        {
            throw InvalidArgument("Role permissions are invalid.");
        }

        var known = PermissionCatalog.All.ToImmutableHashSet(StringComparer.Ordinal);
        var normalized = permissions.Select(value => value?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (normalized.Any(permission => !known.Contains(permission)))
        {
            throw InvalidArgument("Role permissions contain an unknown named permission.");
        }

        return normalized;
    }

    private static string PermissionBits(ImmutableArray<string> permissions)
    {
        ulong value = 0;
        foreach (var permission in NormalizePermissions(permissions))
        {
            value |= Convert.ToUInt64(Enum.Parse<GuildPermission>(permission), CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static ImmutableHashSet<string> NormalizeClear(
        ImmutableArray<string> clear,
        IEnumerable<string> allowed)
    {
        if (clear.IsDefault)
        {
            return ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
        }

        var normalized = clear.Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
            .ToImmutableHashSet(StringComparer.Ordinal);
        if (!normalized.IsSubsetOf(allowed))
        {
            throw InvalidArgument("Role appearance clear list contains an unsupported field.");
        }

        return normalized;
    }

    private static string NormalizeName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is >= 1 and <= 100 && !normalized.Any(char.IsControl)
            ? normalized
            : throw InvalidArgument("Role name must contain 1 to 100 printable characters.");
    }

    private static string? NormalizeColor(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 7 && normalized[0] == '#' && normalized[1..].All(char.IsAsciiHexDigit)
            ? $"#{normalized[1..].ToUpperInvariant()}"
            : throw InvalidArgument("Role color must be #RRGGBB.");
    }

    private static int ColorValue(string? value) =>
        value is null ? 0 : int.Parse(NormalizeColor(value)![1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static RoleColorsState NormalizeRoleColors(
        string? primary,
        string? secondary,
        string? tertiary,
        ImmutableArray<string>? features)
    {
        var normalized = new RoleColorsState(
            NormalizeColor(primary),
            NormalizeColor(secondary),
            NormalizeColor(tertiary));
        if (normalized.Secondary is not null && normalized.Primary is null)
        {
            throw InvalidArgument("A secondary role color requires a primary color.");
        }

        if (normalized.Secondary is not null || normalized.Tertiary is not null)
        {
            if (features.HasValue && !features.Value.Any(feature =>
                    string.Equals(NormalizeFeatureName(feature), "ENHANCEDROLECOLORS", StringComparison.Ordinal)))
            {
                throw InvalidArgument("Secondary or tertiary colors require the ENHANCED_ROLE_COLORS guild feature.");
            }
        }

        if (normalized.Tertiary is not null && normalized != new RoleColorsState("#A9C9FF", "#FFBBEC", "#FFC3A0"))
        {
            throw InvalidArgument("Tertiary role color requires Discord's exact holographic color triplet.");
        }

        return normalized;
    }

    private static string NormalizeFeatureName(string value) => new(
        value.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static Dictionary<string, object?> RoleColorsPayload(RoleColorsState colors) => new(StringComparer.Ordinal)
    {
        ["primary_color"] = ColorValue(colors.Primary),
        ["secondary_color"] = colors.Secondary is null ? null : ColorValue(colors.Secondary),
        ["tertiary_color"] = colors.Tertiary is null ? null : ColorValue(colors.Tertiary)
    };

    private static void EnsureDigest(string supplied, string observed)
    {
        var normalized = supplied?.Trim().ToLowerInvariant();
        if (normalized is not { Length: 64 } || !normalized.All(char.IsAsciiHexDigit))
        {
            throw InvalidArgument("Expected state digest must be a lowercase SHA-256 digest.");
        }

        if (!string.Equals(normalized, observed, StringComparison.Ordinal))
        {
            throw new StewardException(
                StewardErrorCodes.StateConflict,
                "Discord state does not match the approved precondition digest.");
        }
    }

    private void EnsureGuild(GuildSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Id, GuildId(), StringComparison.Ordinal))
        {
            throw new StewardException(StewardErrorCodes.GuildMismatch, "Discord returned a different guild.");
        }
    }

    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);

    private static string NormalizeSnowflake(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : throw InvalidArgument("Discord ID must be a positive snowflake string.");

    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException PolicyDenied(string message) => new(StewardErrorCodes.PolicyDenied, message);

    private static StewardException NotFound() => new(StewardErrorCodes.ResourceNotFound, "The requested role was not found.");

    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "Role mutation is not supported.");

    private sealed record RoleVerificationState(
        string? Id,
        bool Exists,
        string? Name,
        ImmutableArray<string> Permissions,
        string? ColorHex,
        bool IsHoisted,
        bool IsMentionable,
        int? Position,
        string? IconHash,
        string? UnicodeEmoji,
        string? SecondaryColorHex = null,
        string? TertiaryColorHex = null)
    {
        internal static RoleVerificationState FromRole(RoleSnapshot role) => new(
            role.Id,
            Exists: true,
            role.Name,
            role.Permissions,
            role.ColorHex,
            role.IsHoisted,
            role.IsMentionable,
            role.Position,
            role.IconHash,
            role.UnicodeEmoji,
            role.SecondaryColorHex,
            role.TertiaryColorHex);

        internal bool MatchesWithoutIdentity(RoleVerificationState desired) =>
            Exists && desired.Exists &&
            string.Equals(Name, desired.Name, StringComparison.Ordinal) &&
            Permissions.SequenceEqual(desired.Permissions, StringComparer.Ordinal) &&
            string.Equals(ColorHex, desired.ColorHex, StringComparison.Ordinal) &&
            string.Equals(SecondaryColorHex, desired.SecondaryColorHex, StringComparison.Ordinal) &&
            string.Equals(TertiaryColorHex, desired.TertiaryColorHex, StringComparison.Ordinal) &&
            IsHoisted == desired.IsHoisted &&
            IsMentionable == desired.IsMentionable &&
            IconMatches(desired.IconHash) &&
            string.Equals(UnicodeEmoji, desired.UnicodeEmoji, StringComparison.Ordinal);

        internal bool MatchesDesired(RoleVerificationState desired) =>
            string.Equals(Id, desired.Id, StringComparison.Ordinal) &&
            Position == desired.Position &&
            MatchesWithoutIdentity(desired);

        private bool IconMatches(string? desiredIconHash) =>
            desiredIconHash == "__present__"
                ? IconHash is not null
                : string.Equals(IconHash, desiredIconHash, StringComparison.Ordinal);
    }

    private sealed record RoleColorsState(
        string? Primary,
        string? Secondary,
        string? Tertiary);

    private sealed record RoleOrderState(
        string RolesStateDigest,
        ImmutableArray<RolePositionInput> Positions);

    private sealed record MemberRoleState(
        string UserId,
        ImmutableArray<string> RoleIds,
        string? Nickname,
        DateTimeOffset? TimedOutUntil,
        string? VoiceChannelId,
        bool IsMuted,
        bool IsDeafened)
    {
        internal static MemberRoleState FromMember(MemberView member) => new(
            member.UserId,
            member.RoleIds.Order(StringComparer.Ordinal).ToImmutableArray(),
            member.Nickname,
            member.TimedOutUntil,
            member.VoiceState.ChannelId,
            member.VoiceState.IsMuted,
            member.VoiceState.IsDeafened);
    }
}
