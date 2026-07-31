using System.Globalization;
using DiscordSteward.Configuration;
using DiscordSteward.Domain;
using DiscordSteward.Errors;
using global::Discord;
using global::Discord.Rest;

namespace DiscordSteward.Discord;

internal static class DiscordSnapshotNormalizer
{
    internal static GuildSnapshot Normalize(
        RestGuild guild,
        IReadOnlyCollection<RestGuildChannel> guildChannels,
        StewardPolicy policy)
    {
        var roles = guild.Roles.Select(role => NormalizeRole(guild.Id, role, policy));
        var categories = new List<CategorySnapshot>();
        var channels = new List<ChannelSnapshot>();

        foreach (var channel in guildChannels)
        {
            if (channel.GuildId != guild.Id)
            {
                throw new StewardException(
                    StewardErrorCodes.GuildMismatch,
                    "Discord returned a resource outside the configured guild.");
            }

            var overwrites = channel.PermissionOverwrites.Select(NormalizeOverwrite);
            if (channel is RestCategoryChannel)
            {
                categories.Add(CategorySnapshot.Create(
                    guild.Id,
                    channel.Id,
                    channel.Name,
                    channel.Position,
                    overwrites));
            }
            else
            {
                channels.Add(ChannelSnapshot.Create(
                    guild.Id,
                    channel.Id,
                    channel.Name,
                    GetChannelKind(channel),
                    channel.Position,
                    GetCategoryId(channel),
                    GetTopic(channel),
                    GetNsfw(channel),
                    GetSlowModeSeconds(channel),
                    overwrites,
                    GetParentChannelId(channel)));
            }
        }

        return GuildSnapshot.Create(
            guild.Id,
            guild.Name,
            guild.OwnerId,
            guild.Description,
            guild.CreatedAt,
            GetFeatures(guild),
            roles,
            categories,
            channels);
    }

    internal static GuildSnapshot Normalize(
        RestGuild guild,
        IReadOnlyCollection<ExactChannelSnapshot> exactChannels,
        StewardPolicy policy) => GuildSnapshot.Create(
            guild.Id,
            guild.Name,
            guild.OwnerId,
            guild.Description,
            guild.CreatedAt,
            GetFeatures(guild),
            guild.Roles.Select(role => NormalizeRole(guild.Id, role, policy)),
            exactChannels.Select(item => item.Category).OfType<CategorySnapshot>(),
            exactChannels.Select(item => item.Channel).OfType<ChannelSnapshot>());

    private static RoleSnapshot NormalizeRole(ulong guildId, RestRole role, StewardPolicy policy)
    {
        var roleId = role.Id.ToString(CultureInfo.InvariantCulture);
        var colorHex = role.Colors.PrimaryColor.RawValue == 0
            ? null
            : $"#{role.Colors.PrimaryColor.RawValue.ToString("X6", CultureInfo.InvariantCulture)}";
        var secondaryColorHex = role.Colors.SecondaryColor?.RawValue is > 0 and var secondary
            ? $"#{secondary.ToString("X6", CultureInfo.InvariantCulture)}"
            : null;
        var tertiaryColorHex = role.Colors.TertiaryColor?.RawValue is > 0 and var tertiary
            ? $"#{tertiary.ToString("X6", CultureInfo.InvariantCulture)}"
            : null;

        return RoleSnapshot.Create(
            guildId,
            role.Id,
            role.Name,
            role.Position,
            role.IsManaged,
            role.IsEveryone,
            policy.GetProtection(roleId, role.Name),
            role.Permissions.ToList().Select(permission => permission.ToString()),
            colorHex,
            role.IsHoisted,
            role.IsMentionable,
            role.Icon,
            role.Emoji?.ToString(),
            secondaryColorHex,
            tertiaryColorHex);
    }

    private static PermissionOverwriteSnapshot NormalizeOverwrite(Overwrite overwrite) =>
        PermissionOverwriteSnapshot.Create(
            overwrite.TargetType switch
            {
                PermissionTarget.Role => "role",
                PermissionTarget.User => "member",
                _ => "unknown"
            },
            overwrite.TargetId,
            overwrite.Permissions.ToAllowList().Select(NormalizePermissionName),
            overwrite.Permissions.ToDenyList().Select(NormalizePermissionName));

    private static string NormalizePermissionName(ChannelPermission permission) => permission switch
    {
        ChannelPermission.ManageEmojis => "ManageEmojisAndStickers",
        _ => permission.ToString()
    };

    private static IEnumerable<string> GetFeatures(RestGuild guild) =>
        guild.Features.Value
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(feature => !string.Equals(feature, "None", StringComparison.Ordinal))
            .Concat(guild.Features.Experimental);

    private static string GetChannelKind(RestGuildChannel channel) => channel switch
    {
        RestThreadChannel => "thread",
        RestMediaChannel => "media",
        RestForumChannel => "forum",
        RestNewsChannel => "announcement",
        RestStageChannel => "stage",
        RestVoiceChannel => "voice",
        RestTextChannel => "text",
        _ => "unknown"
    };

    private static ulong? GetCategoryId(RestGuildChannel channel) => channel switch
    {
        RestThreadChannel => null,
        RestForumChannel forumChannel => forumChannel.CategoryId,
        RestTextChannel textChannel => textChannel.CategoryId,
        _ => null
    };

    private static ulong? GetParentChannelId(RestGuildChannel channel) =>
        channel is RestThreadChannel threadChannel ? threadChannel.ParentChannelId : null;

    private static string? GetTopic(RestGuildChannel channel) => channel switch
    {
        RestForumChannel forumChannel => forumChannel.Topic,
        RestTextChannel textChannel => textChannel.Topic,
        _ => null
    };

    private static bool? GetNsfw(RestGuildChannel channel) => channel switch
    {
        RestForumChannel forumChannel => forumChannel.IsNsfw,
        RestTextChannel textChannel => textChannel.IsNsfw,
        _ => null
    };

    private static int? GetSlowModeSeconds(RestGuildChannel channel) => channel switch
    {
        RestForumChannel forumChannel => forumChannel.DefaultSlowModeInterval,
        RestTextChannel textChannel => textChannel.SlowModeInterval,
        _ => null
    };
}
