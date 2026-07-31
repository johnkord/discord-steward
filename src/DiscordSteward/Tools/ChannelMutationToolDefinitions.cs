using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class CreateCategoryTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_category", Title = "Create category", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one category with explicit position and role overwrites, bound to the guild channel-list digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete typed category input; channel-only fields must be omitted.")] CreateChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_category", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateTextChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_text_channel", Title = "Create text channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one text channel with explicit parent, topic, slow mode, age restriction, and role overwrites.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete typed text-channel input.")] CreateChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_text_channel", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateAnnouncementChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_announcement_channel", Title = "Create announcement channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one announcement channel in an eligible Community guild with explicit settings and role overwrites.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete typed announcement-channel input.")] CreateChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_announcement_channel", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateVoiceChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_voice_channel", Title = "Create voice channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one voice channel with explicit parent, bitrate, region, quality, limit, and role overwrites.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete typed voice-channel input.")] CreateChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_voice_channel", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateStageChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_stage_channel", Title = "Create stage channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one stage channel with explicit parent, voice settings, and role overwrites.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete typed stage-channel input.")] CreateChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_stage_channel", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateForumChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_forum_channel", Title = "Create forum channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one forum channel with explicit parent, post defaults, layout, sorting, and role overwrites.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete typed forum-channel input.")] CreateChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_forum_channel", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateMediaChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_media_channel", Title = "Create media channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one documented media channel with explicit stable settings and role overwrites.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete typed media-channel input.")] CreateChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_media_channel", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class MoveChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "move_channel", Title = "Move channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Moves one exact channel while preserving its complete overwrite set.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Move(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound destination and position input.")] MoveChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("move_channel", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class MoveChannelAndSyncPermissionsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "move_channel_and_sync_permissions", Title = "Move and sync channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Moves one channel to a different exact category and atomically locks permissions to that category.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Move(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound destination and position input.")] MoveChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("move_channel_and_sync_permissions", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class ReorderChannelsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "reorder_channels", Title = "Reorder channels", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Submits the complete desired order for one exact sibling set.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Reorder(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete sibling positions and channel-list digest.")] ReorderChannelsInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("reorder_channels", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetRoleChannelPermissionsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_role_channel_permissions", Title = "Set role channel permissions", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sets named allow, deny, and inherit permissions for one exact role overwrite.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel or category snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete role-overwrite input.")] SetChannelPermissionsInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_role_channel_permissions", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class RemoveRoleChannelPermissionsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "remove_role_channel_permissions", Title = "Remove role channel permissions", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Removes one exact role overwrite from one exact channel or category.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Remove(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel or category snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound role overwrite removal.")] RemoveChannelPermissionsInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("remove_role_channel_permissions", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetMemberChannelPermissionsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_member_channel_permissions", Title = "Set member channel permissions", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sets named allow, deny, and inherit permissions for one exact member overwrite.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel or category snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete member-overwrite input.")] SetChannelPermissionsInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_member_channel_permissions", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class RemoveMemberChannelPermissionsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "remove_member_channel_permissions", Title = "Remove member channel permissions", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Removes one exact member overwrite from one exact channel or category.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Remove(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel or category snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound member overwrite removal.")] RemoveChannelPermissionsInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("remove_member_channel_permissions", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class ReplaceChannelPermissionOverwritesTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "replace_channel_permission_overwrites", Title = "Replace channel overwrites", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Replaces the complete explicit overwrite array and verifies category propagation to the exact synced children.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Replace(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel or category snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete digest-bound overwrite replacement.")] ReplaceChannelOverwritesInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("replace_channel_permission_overwrites", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class ArchiveChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "archive_channel", Title = "Archive channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Applies explicit approved archive name, destination, visibility, and complete overwrites.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Archive(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete explicit archive state.")] ArchiveChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("archive_channel", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class RestoreArchivedChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "restore_archived_channel", Title = "Restore archived channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Restores explicit approved parent, name, visibility, and complete overwrites.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Restore(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete explicit restored state.")] RestoreArchivedChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("restore_archived_channel", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetVoiceChannelStatusTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_voice_channel_status", Title = "Set voice channel status", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Sets or clears one exact voice or stage channel status.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact voice or stage channel snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound status input; null clears status.")] SetVoiceChannelStatusInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_voice_channel_status", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class TriggerTypingIndicatorTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "trigger_typing_indicator", Title = "Trigger typing indicator", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Triggers one ephemeral typing indicator in an exact text-capable guild channel or thread.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Trigger(
    [Description("Globally unique operation UUID.")] string request_id,
    [Description("Exact text-capable channel or thread snowflake ID.")] string channel_id,
    [Description("Printable operator intent.")] string reason,
    [Description("Exact channel-state evidence obtained before approval.")] TriggerTypingIndicatorInput input,
    CancellationToken cancellationToken) =>
    tools.ExecuteAsync("trigger_typing_indicator", request_id, channel_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class FollowAnnouncementChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "follow_announcement_channel", Title = "Follow announcement channel", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Follows one exact announcement channel into one exact destination and verifies the follower webhook.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Follow(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact source/destination IDs and state digests.")] FollowAnnouncementChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("follow_announcement_channel", request_id, input.SourceChannelId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateThreadTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_thread", Title = "Create thread", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one public, private, or announcement thread with explicit lifecycle settings and optional source message.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete parent-bound thread input.")] CreateThreadInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("create_thread", request_id, input.ParentChannelId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateForumPostTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_forum_post", Title = "Create forum post", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one forum or media post with bounded content, no mentions, and up to five digest-bound tags.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete parent/tag-bound forum post input.")] CreateForumPostInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("create_forum_post", request_id, input.ParentChannelId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateThreadTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_thread", Title = "Update thread", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Explicitly updates thread name, lifecycle, tags, invitation state, slow mode, or forum pin state.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact thread snowflake ID.")] string thread_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound typed thread update.")] UpdateThreadInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_thread", request_id, thread_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SetThreadMemberTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_thread_member", Title = "Set thread member", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Adds or removes one exact member from one exact active thread using an explicit present value.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact digest-bound thread member operation.")] SetThreadMemberInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("set_thread_member", request_id, input.ThreadId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteThreadTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_thread", Title = "Delete thread", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact thread after validating its full state digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact thread snowflake ID.")] string thread_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Digest-bound permanent thread deletion.")] DeleteThreadInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_thread", request_id, thread_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteChannelTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_channel", Title = "Delete channel", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact non-thread channel with the complete typed deletion-impact evidence.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact non-thread channel or category snowflake ID.")] string channel_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete state and deletion-impact evidence.")] DeleteChannelInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_channel", request_id, channel_id, reason, input, cancellationToken);
}
