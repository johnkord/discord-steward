using System.Collections.Immutable;

namespace DiscordSteward.Permissions;

internal static class PermissionCatalog
{
    internal static ImmutableHashSet<string> All { get; } = Create(
        "AddReactions",
        "Administrator",
        "AttachFiles",
        "BanMembers",
        "BypassSlowmode",
        "ChangeNickname",
        "Connect",
        "CreateEvents",
        "CreateGuildExpressions",
        "CreateInstantInvite",
        "CreatePrivateThreads",
        "CreatePublicThreads",
        "DeafenMembers",
        "EmbedLinks",
        "KickMembers",
        "ManageChannels",
        "ManageEmojisAndStickers",
        "ManageEvents",
        "ManageGuild",
        "ManageMessages",
        "ManageNicknames",
        "ManageRoles",
        "ManageThreads",
        "ManageWebhooks",
        "MentionEveryone",
        "ModerateMembers",
        "MoveMembers",
        "MuteMembers",
        "PinMessages",
        "PrioritySpeaker",
        "ReadMessageHistory",
        "RequestToSpeak",
        "SendMessages",
        "SendMessagesInThreads",
        "SendPolls",
        "SendTTSMessages",
        "SendVoiceMessages",
        "SetVoiceChannelStatus",
        "Speak",
        "StartEmbeddedActivities",
        "Stream",
        "UseApplicationCommands",
        "UseClydeAI",
        "UseExternalApps",
        "UseExternalEmojis",
        "UseExternalSounds",
        "UseExternalStickers",
        "UseSoundboard",
        "UseVAD",
        "ViewAuditLog",
        "ViewChannel",
        "ViewGuildInsights",
        "ViewMonetizationAnalytics");

    internal static ImmutableHashSet<string> ChannelScoped { get; } = Create(
        "AddReactions",
        "AttachFiles",
        "BypassSlowmode",
        "Connect",
        "CreateEvents",
        "CreateInstantInvite",
        "CreatePrivateThreads",
        "CreatePublicThreads",
        "DeafenMembers",
        "EmbedLinks",
        "ManageChannels",
        "ManageEmojisAndStickers",
        "ManageMessages",
        "ManageRoles",
        "ManageThreads",
        "ManageWebhooks",
        "MentionEveryone",
        "MoveMembers",
        "MuteMembers",
        "PinMessages",
        "PrioritySpeaker",
        "ReadMessageHistory",
        "RequestToSpeak",
        "SendMessages",
        "SendMessagesInThreads",
        "SendPolls",
        "SendTTSMessages",
        "SendVoiceMessages",
        "SetVoiceChannelStatus",
        "Speak",
        "StartEmbeddedActivities",
        "Stream",
        "UseApplicationCommands",
        "UseClydeAI",
        "UseExternalApps",
        "UseExternalEmojis",
        "UseExternalSounds",
        "UseExternalStickers",
        "UseSoundboard",
        "UseVAD",
        "ViewChannel");

    internal static ImmutableHashSet<string> SendMessageDependent { get; } = Create(
        "AttachFiles",
        "EmbedLinks",
        "MentionEveryone",
        "SendTTSMessages");

    internal static ImmutableHashSet<string> ConnectDependent { get; } = Create(
        "DeafenMembers",
        "ManageChannels",
        "MoveMembers",
        "MuteMembers",
        "PrioritySpeaker",
        "RequestToSpeak",
        "SetVoiceChannelStatus",
        "Speak",
        "StartEmbeddedActivities",
        "Stream",
        "UseExternalSounds",
        "UseSoundboard",
        "UseVAD");

    internal static bool IsTextLike(string kind) => kind is
        "text" or "announcement" or "forum" or "media" or "thread";

    internal static bool IsVoiceLike(string kind) => kind is "voice" or "stage";

    private static ImmutableHashSet<string> Create(params string[] permissions) =>
        permissions.ToImmutableHashSet(StringComparer.Ordinal);
}
