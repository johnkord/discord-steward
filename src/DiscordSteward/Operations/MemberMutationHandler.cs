using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DiscordSteward.Configuration;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

internal sealed class MemberMutationHandler(
    DiscordApiTransport transport,
    IDiscordGuildReader guildReader,
    IDiscordMemberReader memberReader,
    IDiscordBotInspector botInspector,
    StewardPolicy policy,
    StewardRuntimeSettings settings,
    TimeProvider timeProvider) : ICatalogMutationFamilyHandler
{
    private const int BypassesVerificationFlag = 1 << 2;
    private static readonly ImmutableHashSet<string> ToolNames = new[]
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
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public bool Supports(string toolName) => ToolNames.Contains(toolName);

    public async Task<CatalogMutationPreparation> PrepareAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken) => invocation.ToolName switch
        {
            "set_member_nickname" => await PrepareNicknameAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_member_timeout" => await PrepareTimeoutAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_member_voice_channel" => await PrepareVoiceChannelAsync(invocation, cancellationToken).ConfigureAwait(false),
            "update_member_voice_state" => await PrepareVoiceStateAsync(invocation, cancellationToken).ConfigureAwait(false),
            "set_member_verification_bypass" => await PrepareVerificationBypassAsync(invocation, cancellationToken).ConfigureAwait(false),
            "kick_member" => await PrepareKickAsync(invocation, cancellationToken).ConfigureAwait(false),
            "ban_member" => await PrepareBanAsync(invocation, cancellationToken).ConfigureAwait(false),
            "bulk_ban_members" => await PrepareBulkBanAsync(invocation, cancellationToken).ConfigureAwait(false),
            "unban_member" => await PrepareUnbanAsync(invocation, cancellationToken).ConfigureAwait(false),
            _ => throw Unsupported()
        };

    public async Task<CatalogMutationWriteReceipt> WriteAsync(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation,
        CancellationToken cancellationToken)
    {
        JsonElement? response = invocation.ToolName switch
        {
            "set_member_nickname" => await PatchMemberAsync(invocation, NicknameBody(invocation), cancellationToken).ConfigureAwait(false),
            "set_member_timeout" => await PatchMemberAsync(invocation, TimeoutBody(invocation), cancellationToken).ConfigureAwait(false),
            "set_member_voice_channel" => await PatchMemberAsync(invocation, VoiceChannelBody(invocation), cancellationToken).ConfigureAwait(false),
            "update_member_voice_state" => await PatchMemberAsync(invocation, VoiceStateBody(invocation), cancellationToken).ConfigureAwait(false),
            "set_member_verification_bypass" => await PatchMemberAsync(
                invocation,
                VerificationBypassBody(invocation, preparation),
                cancellationToken).ConfigureAwait(false),
            "kick_member" => await transport.SendJsonAsync(
                HttpMethod.Delete,
                $"guilds/{GuildId()}/members/{NormalizeSnowflake(invocation.ResourceId)}",
                body: null,
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false),
            "ban_member" => await transport.SendJsonAsync(
                HttpMethod.Put,
                $"guilds/{GuildId()}/bans/{NormalizeSnowflake(invocation.ResourceId)}",
                JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                {
                    ["delete_message_seconds"] = 0
                }, CatalogJson.Options),
                AuditReason(invocation),
                cancellationToken).ConfigureAwait(false),
            "bulk_ban_members" => await WriteBulkBanAsync(invocation, cancellationToken).ConfigureAwait(false),
            "unban_member" => await transport.SendJsonAsync(
                HttpMethod.Delete,
                $"guilds/{GuildId()}/bans/{NormalizeSnowflake(invocation.ResourceId)}",
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
        if (invocation.ToolName == "kick_member")
        {
            var exists = await transport.TryGetJsonAsync(
                $"guilds/{GuildId()}/members/{NormalizeSnowflake(invocation.ResourceId)}",
                cancellationToken).ConfigureAwait(false) is not null;
            return Observation(
                new MemberPresenceState(invocation.ResourceId, exists),
                beforeStateJson,
                desiredStateJson);
        }

        if (invocation.ToolName is "ban_member" or "unban_member")
        {
            var observed = await GetBanStateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
            var desired = DeserializeState<BanPresenceState>(desiredStateJson);
            var before = DeserializeState<BanPresenceState>(beforeStateJson);
            var desiredMatch = observed.IsBanned == desired.IsBanned && observed.UserId == desired.UserId;
            var beforeMatch = observed.IsBanned == before.IsBanned && observed.UserId == before.UserId &&
                (!observed.IsBanned || observed.StateDigest == before.StateDigest);
            return new CatalogMutationObservation(
                desiredMatch
                    ? CatalogMutationObservationKind.DesiredState
                    : beforeMatch ? CatalogMutationObservationKind.BeforeState : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        if (invocation.ToolName == "bulk_ban_members")
        {
            var input = Arguments<BulkBanMembersInput>(invocation);
            var states = ImmutableArray.CreateBuilder<BanPresenceState>();
            foreach (var target in input.Targets.OrderBy(target => target.UserId, StringComparer.Ordinal))
            {
                states.Add(await GetBanStateAsync(target.UserId, cancellationToken).ConfigureAwait(false));
            }

            var observed = new BulkBanState(states.ToImmutable());
            var before = DeserializeState<BulkBanState>(beforeStateJson);
            var desired = DeserializeState<BulkBanState>(desiredStateJson);
            return new CatalogMutationObservation(
                observed.Matches(desired)
                    ? CatalogMutationObservationKind.DesiredState
                    : observed.Matches(before)
                        ? CatalogMutationObservationKind.BeforeState
                        : CatalogMutationObservationKind.UnexpectedState,
                State(observed));
        }

        MemberView member;
        try
        {
            member = await memberReader.GetMemberAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
            return new CatalogMutationObservation(
                CatalogMutationObservationKind.UnexpectedState,
                State(new MemberPresenceState(invocation.ResourceId, Exists: false)));
        }

        return Observation(MemberMutationState.FromMember(member), beforeStateJson, desiredStateJson);
    }

    private async Task<CatalogMutationPreparation> PrepareNicknameAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetMemberNicknameInput>(invocation);
        var member = await GetManageableMemberAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        var nickname = input.Nickname is null ? null : NormalizeNickname(input.Nickname);
        var before = MemberMutationState.FromMember(member);
        return new CatalogMutationPreparation(State(before), State(before with { Nickname = nickname }));
    }

    private async Task<CatalogMutationPreparation> PrepareTimeoutAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetMemberTimeoutInput>(invocation);
        var (member, snapshot) = await GetManageableMemberWithSnapshotAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        if (!policy.IsUnrestricted && member.RoleIds.Select(id => snapshot.Roles.SingleOrDefault(role => role.Id == id))
            .Where(role => role is not null)
            .Any(role => role!.Permissions.Contains("Administrator", StringComparer.Ordinal)))
        {
            throw PolicyDenied("Members with Administrator cannot be timed out.");
        }

        if (input.TimedOutUntil.HasValue)
        {
            var now = timeProvider.GetUtcNow();
            if (input.TimedOutUntil.Value <= now || input.TimedOutUntil.Value > now.AddDays(28))
            {
                throw InvalidArgument("Timeout must be in the future and no more than 28 days away.");
            }
        }

        var before = MemberMutationState.FromMember(member);
        return new CatalogMutationPreparation(
            State(before),
            State(before with { TimedOutUntil = input.TimedOutUntil }));
    }

    private async Task<CatalogMutationPreparation> PrepareVoiceChannelAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetMemberVoiceChannelInput>(invocation);
        var member = await GetManageableMemberAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        string? channelId = null;
        if (input.ChannelId is not null)
        {
            channelId = NormalizeSnowflake(input.ChannelId);
            var channel = await transport.GetJsonAsync($"channels/{channelId}", cancellationToken).ConfigureAwait(false);
            var state = ChannelApiState.Parse(channel);
            if (state.GuildId != GuildId() || state.Type is not 2 and not 13)
            {
                throw InvalidArgument("Voice destination must be a voice or stage channel in the configured guild.");
            }
        }
        else if (member.VoiceState.ChannelId is null)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The member is already disconnected from voice.");
        }

        var before = MemberMutationState.FromMember(member);
        return new CatalogMutationPreparation(State(before), State(before with { VoiceChannelId = channelId }));
    }

    private async Task<CatalogMutationPreparation> PrepareVoiceStateAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UpdateMemberVoiceStateInput>(invocation);
        if (!input.Muted.HasValue && !input.Deafened.HasValue)
        {
            throw InvalidArgument("Voice-state update must set mute, deafen, or both.");
        }

        var member = await GetManageableMemberAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        if (member.VoiceState.ChannelId is null)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The member is not connected to voice.");
        }

        var before = MemberMutationState.FromMember(member);
        return new CatalogMutationPreparation(
            State(before),
            State(before with
            {
                Muted = input.Muted ?? before.Muted,
                Deafened = input.Deafened ?? before.Deafened
            }));
    }

    private async Task<CatalogMutationPreparation> PrepareVerificationBypassAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<SetMemberVerificationBypassInput>(invocation);
        var member = await GetManageableMemberAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        var before = MemberMutationState.FromMember(member);
        var desiredFlags = input.BypassesVerification
            ? before.Flags | BypassesVerificationFlag
            : before.Flags & ~BypassesVerificationFlag;
        return new CatalogMutationPreparation(State(before), State(before with { Flags = desiredFlags }));
    }

    private async Task<CatalogMutationPreparation> PrepareKickAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<KickMemberInput>(invocation);
        var member = await GetManageableMemberAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        return new CatalogMutationPreparation(
            State(new MemberPresenceState(member.UserId, Exists: true)),
            State(new MemberPresenceState(member.UserId, Exists: false)));
    }

    private async Task<CatalogMutationPreparation> PrepareBanAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<BanMemberInput>(invocation);
        var userId = NormalizeSnowflake(invocation.ResourceId);
        var existingBan = await GetBanStateAsync(userId, cancellationToken).ConfigureAwait(false);
        if (existingBan.IsBanned)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The user is already banned.");
        }

        try
        {
            var member = await GetManageableMemberAsync(userId, cancellationToken).ConfigureAwait(false);
            if (input.ExpectedMemberStateDigest is null)
            {
                throw InvalidArgument("Banning a current member requires its exact state digest.");
            }

            EnsureDigest(input.ExpectedMemberStateDigest, member.StateDigest);
        }
        catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
        {
            if (input.ExpectedMemberStateDigest is not null)
            {
                throw new StewardException(StewardErrorCodes.StateConflict, "The expected member is no longer in the guild.");
            }

            await EnsureNotOwnerOrSelfAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        return new CatalogMutationPreparation(
            State(existingBan),
            State(new BanPresenceState(
                userId,
                IsBanned: true,
                Reason: null,
                ResourceStateCanonicalizer.ComputeBanDigest(userId, reason: null, isBanned: true))));
    }

    private async Task<CatalogMutationPreparation> PrepareBulkBanAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<BulkBanMembersInput>(invocation);
        if (input.Targets.IsDefault || input.Targets.Length is < 2 or > 25)
        {
            throw InvalidArgument("Bulk ban requires 2 to 25 explicit targets.");
        }

        var targets = input.Targets.Select(target => target with { UserId = NormalizeSnowflake(target.UserId) })
            .OrderBy(target => target.UserId, StringComparer.Ordinal)
            .ToImmutableArray();
        if (targets.Select(target => target.UserId).Distinct(StringComparer.Ordinal).Count() != targets.Length)
        {
            throw InvalidArgument("Bulk ban target IDs must be unique.");
        }

        var before = ImmutableArray.CreateBuilder<BanPresenceState>();
        var desired = ImmutableArray.CreateBuilder<BanPresenceState>();
        foreach (var target in targets)
        {
            var ban = await GetBanStateAsync(target.UserId, cancellationToken).ConfigureAwait(false);
            if (ban.IsBanned)
            {
                throw new StewardException(StewardErrorCodes.StateConflict, "A bulk-ban target is already banned.");
            }

            try
            {
                var member = await GetManageableMemberAsync(target.UserId, cancellationToken).ConfigureAwait(false);
                if (target.ExpectedMemberStateDigest is null)
                {
                    throw InvalidArgument("Each current bulk-ban member requires its exact state digest.");
                }

                EnsureDigest(target.ExpectedMemberStateDigest, member.StateDigest);
            }
            catch (StewardException exception) when (exception.Code == StewardErrorCodes.ResourceNotFound)
            {
                if (target.ExpectedMemberStateDigest is not null)
                {
                    throw new StewardException(StewardErrorCodes.StateConflict, "An expected bulk-ban member is no longer in the guild.");
                }

                await EnsureNotOwnerOrSelfAsync(target.UserId, cancellationToken).ConfigureAwait(false);
            }

            before.Add(ban);
            desired.Add(new BanPresenceState(
                target.UserId,
                IsBanned: true,
                Reason: null,
                ResourceStateCanonicalizer.ComputeBanDigest(target.UserId, reason: null, isBanned: true)));
        }

        return new CatalogMutationPreparation(
            State(new BulkBanState(before.ToImmutable())),
            State(new BulkBanState(desired.ToImmutable())));
    }

    private async Task<CatalogMutationPreparation> PrepareUnbanAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<UnbanMemberInput>(invocation);
        var ban = await GetBanStateAsync(invocation.ResourceId, cancellationToken).ConfigureAwait(false);
        if (!ban.IsBanned)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "The user is not currently banned.");
        }

        EnsureDigest(input.ExpectedBanStateDigest, ban.StateDigest);
        return new CatalogMutationPreparation(
            State(ban),
            State(new BanPresenceState(
                ban.UserId,
                IsBanned: false,
                Reason: null,
                ResourceStateCanonicalizer.ComputeBanDigest(ban.UserId, reason: null, isBanned: false))));
    }

    private async Task<MemberView> GetManageableMemberAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var (member, _) = await GetManageableMemberWithSnapshotAsync(userId, cancellationToken).ConfigureAwait(false);
        return member;
    }

    private async Task<(MemberView Member, GuildSnapshot Snapshot)> GetManageableMemberWithSnapshotAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeSnowflake(userId);
        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Id != GuildId())
        {
            throw new StewardException(StewardErrorCodes.GuildMismatch, "Discord returned a different guild.");
        }

        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        if (normalizedId == snapshot.OwnerId || normalizedId == bot.BotUserId)
        {
            throw PolicyDenied("Steward cannot target the guild owner or itself.");
        }

        if (!policy.IsUnrestricted && policy.ProtectedResourceIds.Contains(normalizedId))
        {
            throw PolicyDenied("Steward policy protects the requested member.");
        }

        var member = await memberReader.GetMemberAsync(normalizedId, cancellationToken).ConfigureAwait(false);
        if (!bot.HighestRolePosition.HasValue || member.Hierarchy >= bot.HighestRolePosition.Value)
        {
            throw PolicyDenied("The requested member is at or above the bot's role ceiling.");
        }

        return (member, snapshot);
    }

    private async Task EnsureNotOwnerOrSelfAsync(string userId, CancellationToken cancellationToken)
    {
        var snapshot = await guildReader.GetGuildSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var bot = await botInspector.GetEffectivePermissionsAsync(cancellationToken).ConfigureAwait(false);
        if (userId == snapshot.OwnerId || userId == bot.BotUserId ||
            (!policy.IsUnrestricted && policy.ProtectedResourceIds.Contains(userId)))
        {
            throw PolicyDenied("Steward cannot target the guild owner, itself, or a protected user.");
        }
    }

    private async Task<BanPresenceState> GetBanStateAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeSnowflake(userId);
        var root = await transport.TryGetJsonAsync(
            $"guilds/{GuildId()}/bans/{normalizedId}",
            cancellationToken).ConfigureAwait(false);
        if (!root.HasValue)
        {
            return new BanPresenceState(
                normalizedId,
                IsBanned: false,
                Reason: null,
                ResourceStateCanonicalizer.ComputeBanDigest(normalizedId, reason: null, isBanned: false));
        }

        var reason = root.Value.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
            ? Bound(reasonElement.GetString(), 512)
            : null;
        return new BanPresenceState(
            normalizedId,
            IsBanned: true,
            reason,
            ResourceStateCanonicalizer.ComputeBanDigest(normalizedId, reason, isBanned: true));
    }

    private Task<JsonElement?> PatchMemberAsync(
        CatalogMutationInvocation invocation,
        Dictionary<string, object?> body,
        CancellationToken cancellationToken) => transport.SendJsonAsync(
            HttpMethod.Patch,
            $"guilds/{GuildId()}/members/{NormalizeSnowflake(invocation.ResourceId)}",
            JsonSerializer.SerializeToElement(body, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);

    private Task<JsonElement?> WriteBulkBanAsync(
        CatalogMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = Arguments<BulkBanMembersInput>(invocation);
        var userIds = input.Targets.Select(target => NormalizeSnowflake(target.UserId))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return transport.SendJsonAsync(
            HttpMethod.Post,
            $"guilds/{GuildId()}/bulk-ban",
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["user_ids"] = userIds,
                ["delete_message_seconds"] = 0
            }, CatalogJson.Options),
            AuditReason(invocation),
            cancellationToken);
    }

    private static Dictionary<string, object?> NicknameBody(CatalogMutationInvocation invocation)
    {
        var input = Arguments<SetMemberNicknameInput>(invocation);
        return new Dictionary<string, object?> { ["nick"] = input.Nickname };
    }

    private static Dictionary<string, object?> TimeoutBody(CatalogMutationInvocation invocation)
    {
        var input = Arguments<SetMemberTimeoutInput>(invocation);
        return new Dictionary<string, object?> { ["communication_disabled_until"] = input.TimedOutUntil };
    }

    private static Dictionary<string, object?> VoiceChannelBody(CatalogMutationInvocation invocation)
    {
        var input = Arguments<SetMemberVoiceChannelInput>(invocation);
        return new Dictionary<string, object?>
        {
            ["channel_id"] = input.ChannelId is null ? null : NormalizeSnowflake(input.ChannelId)
        };
    }

    private static Dictionary<string, object?> VoiceStateBody(CatalogMutationInvocation invocation)
    {
        var input = Arguments<UpdateMemberVoiceStateInput>(invocation);
        var body = new Dictionary<string, object?>();
        if (input.Muted.HasValue) body["mute"] = input.Muted.Value;
        if (input.Deafened.HasValue) body["deaf"] = input.Deafened.Value;
        return body;
    }

    private static Dictionary<string, object?> VerificationBypassBody(
        CatalogMutationInvocation invocation,
        CatalogMutationPreparation preparation)
    {
        _ = Arguments<SetMemberVerificationBypassInput>(invocation);
        var desired = DeserializeState<MemberMutationState>(preparation.DesiredStateJson);
        return new Dictionary<string, object?> { ["flags"] = desired.Flags };
    }

    private static CatalogMutationObservation Observation<T>(
        T observed,
        string beforeStateJson,
        string desiredStateJson)
    {
        var observedJson = State(observed);
        return new CatalogMutationObservation(
            JsonEqual(observedJson, desiredStateJson)
                ? CatalogMutationObservationKind.DesiredState
                : JsonEqual(observedJson, beforeStateJson)
                    ? CatalogMutationObservationKind.BeforeState
                    : CatalogMutationObservationKind.UnexpectedState,
            observedJson);
    }

    private static bool JsonEqual(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
    }

    private static T Arguments<T>(CatalogMutationInvocation invocation) where T : class =>
        invocation.Arguments.Deserialize<T>(CatalogJson.Options)
        ?? throw InvalidArgument("Mutation arguments are invalid.");

    private static T DeserializeState<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, CatalogJson.Options)
        ?? throw new StewardException(StewardErrorCodes.AuditUnavailable, "Stored mutation state is invalid.");

    private static string State<T>(T value) => JsonSerializer.Serialize(value, CatalogJson.Options);

    private static string NormalizeNickname(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is >= 1 and <= 32 && !normalized.Any(char.IsControl)
            ? normalized
            : throw InvalidArgument("Nickname must contain 1 to 32 printable characters, or be null to clear.");
    }

    private static string NormalizeSnowflake(string value) => ChannelApiState.NormalizeSnowflake(value);

    private static void EnsureDigest(string? supplied, string observed)
    {
        var normalized = supplied?.Trim().ToLowerInvariant();
        if (normalized is not { Length: 64 } || !normalized.All(char.IsAsciiHexDigit))
        {
            throw InvalidArgument("Expected state digest must be a lowercase SHA-256 digest.");
        }

        if (normalized != observed)
        {
            throw new StewardException(StewardErrorCodes.StateConflict, "Member or ban state changed after approval.");
        }
    }

    private string GuildId() => settings.GuildId!.Value.ToString(CultureInfo.InvariantCulture);

    private static string AuditReason(CatalogMutationInvocation invocation)
    {
        var value = $"Discord Steward {invocation.RequestId}: {invocation.Reason}";
        return value.Length <= 512 ? value : value[..512];
    }

    private static string? Bound(string? value, int maximum) =>
        value is null ? null : value.Length <= maximum ? value : value[..maximum];

    private static StewardException InvalidArgument(string message) => new(StewardErrorCodes.InvalidArgument, message);

    private static StewardException PolicyDenied(string message) => new(StewardErrorCodes.PolicyDenied, message);

    private static StewardException Unsupported() => new(StewardErrorCodes.ToolNotEnabled, "Member mutation is not supported.");

    private sealed record MemberMutationState(
        string UserId,
        string? Nickname,
        ImmutableArray<string> RoleIds,
        DateTimeOffset? TimedOutUntil,
        int Flags,
        string? VoiceChannelId,
        bool Muted,
        bool Deafened)
    {
        internal static MemberMutationState FromMember(MemberView member) => new(
            member.UserId,
            member.Nickname,
            member.RoleIds.Order(StringComparer.Ordinal).ToImmutableArray(),
            member.TimedOutUntil,
            member.FlagsValue,
            member.VoiceState.ChannelId,
            member.VoiceState.IsMuted,
            member.VoiceState.IsDeafened);
    }

    private sealed record MemberPresenceState(string UserId, bool Exists);

    private sealed record BanPresenceState(
        string UserId,
        bool IsBanned,
        string? Reason,
        string StateDigest);

    private sealed record BulkBanState(ImmutableArray<BanPresenceState> Targets)
    {
        internal bool Matches(BulkBanState other) =>
            Targets.Length == other.Targets.Length &&
            Targets.OrderBy(target => target.UserId, StringComparer.Ordinal)
                .Zip(other.Targets.OrderBy(target => target.UserId, StringComparer.Ordinal))
                .All(pair => pair.First.UserId == pair.Second.UserId && pair.First.IsBanned == pair.Second.IsBanned);
    }
}
