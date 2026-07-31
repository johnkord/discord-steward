using System.Security.Cryptography;
using System.Text.Json;
using DiscordSteward.Domain;

namespace DiscordSteward.Operations;

internal static class ChannelStateCanonicalizer
{
    internal static string ComputeDigest(ChannelSnapshot channel)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("resourceType", "channel");
            writer.WriteString("guildId", channel.GuildId);
            writer.WriteString("id", channel.Id);
            writer.WriteString("kind", channel.Kind);
            writer.WriteNumber("position", channel.Position);
            WriteNullableString(writer, "categoryId", channel.CategoryId);
            WriteNullableString(writer, "parentChannelId", channel.ParentChannelId);
            writer.WriteString("name", channel.Name);
            WriteNullableString(writer, "topic", channel.Topic);
            if (channel.IsNsfw.HasValue)
            {
                writer.WriteBoolean("isNsfw", channel.IsNsfw.Value);
            }
            else
            {
                writer.WriteNull("isNsfw");
            }

            if (channel.SlowModeSeconds.HasValue)
            {
                writer.WriteNumber("slowModeSeconds", channel.SlowModeSeconds.Value);
            }
            else
            {
                writer.WriteNull("slowModeSeconds");
            }

            writer.WriteStartArray("permissionOverwrites");
            foreach (var overwrite in channel.PermissionOverwrites)
            {
                PermissionOverwriteSummary.WriteOverwrite(writer, overwrite);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    internal static ChannelMutableState ToMutableState(ChannelSnapshot channel) => new(
        channel.Name,
        channel.Topic,
        channel.SlowModeSeconds ?? 0,
        channel.CategoryId);

    internal static string SerializeState(ChannelMutableState state) =>
        JsonSerializer.Serialize(state, OperationJson.SerializerOptions);

    internal static ChannelMutableState DeserializeState(string json) =>
        JsonSerializer.Deserialize<ChannelMutableState>(json, OperationJson.SerializerOptions)
        ?? throw new InvalidOperationException("Stored channel state is invalid.");

    private static void WriteState(Utf8JsonWriter writer, ChannelMutableState state)
    {
        writer.WriteStartObject();
        writer.WriteString("name", state.Name);
        if (state.Topic is null)
        {
            writer.WriteNull("topic");
        }
        else
        {
            writer.WriteString("topic", state.Topic);
        }

        writer.WriteNumber("slowModeSeconds", state.SlowModeSeconds);
        if (state.CategoryId is null)
        {
            writer.WriteNull("categoryId");
        }
        else
        {
            writer.WriteString("categoryId", state.CategoryId);
        }

        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}

internal static class OperationJson
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
