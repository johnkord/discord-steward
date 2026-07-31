using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordSteward.Discord;
using DiscordSteward.Domain;
using DiscordSteward.Errors;

namespace DiscordSteward.Operations;

public static class ResourceStateCanonicalizer
{
    public static string ComputeObjectDigest<T>(T value) =>
        ComputeDigest(JsonSerializer.Serialize(value, CatalogJson.Options));

    public static string ComputeRoleDigest(RoleSnapshot role) => ComputeDigest(SerializeRole(role));

    public static string ComputeRolesDigest(IEnumerable<RoleSnapshot> roles)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("roles");
            foreach (var role in roles.OrderBy(role => role.Id, StringComparer.Ordinal))
            {
                WriteRole(writer, role);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ComputeDigest(stream);
    }

    public static string ComputeMemberDigest(MemberView member) => ComputeDigest(SerializeMember(member));

    public static string ComputeBanDigest(string userId, string? reason, bool isBanned)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("userId", userId);
            writer.WriteBoolean("banned", isBanned);
            writer.WriteString("reason", reason);
            writer.WriteEndObject();
        }

        return ComputeDigest(stream);
    }

    public static string ComputeMessageDigest(MessageView message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("id", message.Id);
            writer.WriteString("channelId", message.ChannelId);
            writer.WriteString("authorId", message.AuthorId);
            writer.WriteString("content", message.Content);
            writer.WriteString("createdAt", message.CreatedAt);
            if (message.EditedAt.HasValue)
            {
                writer.WriteString("editedAt", message.EditedAt.Value);
            }
            else
            {
                writer.WriteNull("editedAt");
            }
            writer.WriteBoolean("pinned", message.IsPinned);
            writer.WriteNumber("type", message.Type);
            writer.WriteNumber("flags", message.Flags);
            writer.WriteBoolean("tts", message.IsTts);
            writer.WriteStartArray("mentionedUserIds");
            foreach (var id in message.MentionedUserIds.Order(StringComparer.Ordinal)) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteStartArray("mentionedRoleIds");
            foreach (var id in message.MentionedRoleIds.Order(StringComparer.Ordinal)) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteStartArray("attachments");
            foreach (var attachment in message.Attachments.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", attachment.Id);
                writer.WriteString("filename", attachment.Filename);
                writer.WriteString("description", attachment.Description);
                writer.WriteString("contentType", attachment.ContentType);
                writer.WriteNumber("sizeBytes", attachment.SizeBytes);
                writer.WriteBoolean("ephemeral", attachment.IsEphemeral);
                writer.WriteBoolean("spoiler", attachment.IsSpoiler);
                if (attachment.DurationSeconds.HasValue)
                {
                    writer.WriteNumber("durationSeconds", attachment.DurationSeconds.Value);
                }
                else
                {
                    writer.WriteNull("durationSeconds");
                }
                writer.WriteString("waveform", attachment.Waveform);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("stickerIds");
            foreach (var id in message.StickerIds) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteString("embedsJson", message.EmbedsJson);
            writer.WriteString("componentsJson", message.ComponentsJson);
            writer.WritePropertyName("sharedClientTheme");
            JsonSerializer.Serialize(writer, message.SharedClientTheme, CatalogJson.Options);
            writer.WriteString("reactionStateDigest", message.ReactionStateDigest);
            writer.WriteString("pollStateDigest", message.Poll?.StateDigest);
            writer.WriteEndObject();
        }

        return ComputeDigest(stream);
    }

    public static string ComputeReactionDigest(JsonElement reactions)
    {
        if (reactions.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return ComputeDigest("[]");
        }

        if (reactions.ValueKind != JsonValueKind.Array)
        {
            throw new StewardException(
                StewardErrorCodes.DiscordUnavailable,
                "Discord returned invalid reaction state.");
        }

        var normalized = reactions.EnumerateArray().Select(reaction =>
        {
            var emoji = reaction.GetProperty("emoji");
            return new
            {
                id = emoji.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
                name = emoji.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null,
                count = reaction.TryGetProperty("count", out var count) ? count.GetInt32() : 0,
                me = reaction.TryGetProperty("me", out var me) && me.GetBoolean(),
                meBurst = reaction.TryGetProperty("me_burst", out var meBurst) && meBurst.GetBoolean()
            };
        }).OrderBy(item => item.id, StringComparer.Ordinal)
            .ThenBy(item => item.name, StringComparer.Ordinal)
            .ToArray();
        return ComputeDigest(JsonSerializer.Serialize(normalized, CatalogJson.Options));
    }

    public static string ComputePollDigest(
        string question,
        ImmutableArray<MessagePollAnswerView> answers,
        DateTimeOffset? expiresAt,
        bool allowsMultiselect,
        bool isFinalized) => ComputeDigest(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            question,
            answers = answers.OrderBy(answer => answer.AnswerId),
            expiresAt,
            allowsMultiselect,
            isFinalized
        }, CatalogJson.Options));

    public static string ComputeChannelsDigest(GuildSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("categories");
            foreach (var category in snapshot.Categories.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", category.Id);
                writer.WriteString("name", category.Name);
                writer.WriteNumber("position", category.Position);
                WriteOverwrites(writer, category.PermissionOverwrites);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("channels");
            foreach (var channel in snapshot.Channels.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", channel.Id);
                writer.WriteString("stateDigest", ChannelStateCanonicalizer.ComputeDigest(channel));
                WriteOverwrites(writer, channel.PermissionOverwrites);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ComputeDigest(stream);
    }

    public static string SerializeRole(RoleSnapshot role)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteRole(writer, role);
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    public static string SerializeMember(MemberView member)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("userId", member.UserId);
            writer.WriteString("nickname", member.Nickname);
            writer.WriteStartArray("roleIds");
            foreach (var roleId in member.RoleIds.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(roleId);
            }

            writer.WriteEndArray();
            if (member.TimedOutUntil.HasValue)
            {
                writer.WriteString("timedOutUntil", member.TimedOutUntil.Value);
            }
            else
            {
                writer.WriteNull("timedOutUntil");
            }
            writer.WriteNumber("flags", member.FlagsValue);
            writer.WriteBoolean("isPending", member.IsPending ?? false);
            writer.WriteStartObject("voiceState");
            writer.WriteString("channelId", member.VoiceState.ChannelId);
            writer.WriteBoolean("muted", member.VoiceState.IsMuted);
            writer.WriteBoolean("deafened", member.VoiceState.IsDeafened);
            writer.WriteBoolean("suppressed", member.VoiceState.IsSuppressed);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static void WriteRole(Utf8JsonWriter writer, RoleSnapshot role)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("guildId", role.GuildId);
        writer.WriteString("id", role.Id);
        writer.WriteString("name", role.Name);
        writer.WriteNumber("position", role.Position);
        writer.WriteBoolean("managed", role.IsManaged);
        writer.WriteBoolean("everyone", role.IsEveryone);
        writer.WriteStartArray("permissions");
        foreach (var permission in role.Permissions.Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(permission);
        }

        writer.WriteEndArray();
        writer.WriteString("colorHex", role.ColorHex);
        writer.WriteString("secondaryColorHex", role.SecondaryColorHex);
        writer.WriteString("tertiaryColorHex", role.TertiaryColorHex);
        writer.WriteBoolean("hoisted", role.IsHoisted);
        writer.WriteBoolean("mentionable", role.IsMentionable);
        writer.WriteString("iconHash", role.IconHash);
        writer.WriteString("unicodeEmoji", role.UnicodeEmoji);
        writer.WriteEndObject();
    }

    private static void WriteOverwrites(
        Utf8JsonWriter writer,
        ImmutableArray<PermissionOverwriteSnapshot> overwrites)
    {
        writer.WriteStartArray("permissionOverwrites");
        foreach (var overwrite in overwrites
                     .OrderBy(item => item.TargetType, StringComparer.Ordinal)
                     .ThenBy(item => item.TargetId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("targetType", overwrite.TargetType);
            writer.WriteString("targetId", overwrite.TargetId);
            writer.WriteStartArray("allow");
            foreach (var permission in overwrite.AllowedPermissions.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(permission);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("deny");
            foreach (var permission in overwrite.DeniedPermissions.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(permission);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string ComputeDigest(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));

    private static string ComputeDigest(MemoryStream stream) =>
        Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
}
