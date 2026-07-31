using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class CreateInviteTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_invite", Title = "Create invite", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one bounded invite with explicit channel, lifetime, uses, target, temporary state, and optional role grants.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete channel-bound invite input.")] CreateInviteInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("create_invite", request_id, input.ChannelId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateTargetedInviteTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_targeted_invite", Title = "Create targeted invite", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one invite with a bounded exact user allowlist encoded internally as CSV and returns accepted job state.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete invite and exact target-user list.")] CreateTargetedInviteInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("create_targeted_invite", request_id, input.Invite.ChannelId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateInviteTargetUsersTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_invite_target_users", Title = "Update invite target users", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Replaces one invite's exact target-user CSV after state-digest validation and returns accepted job state.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact invite code.")] string code,
        [Description("Printable operator intent.")] string reason,
        [Description("Current target-user digest and complete replacement IDs.")] UpdateInviteTargetUsersInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_invite_target_users", request_id, code, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteInviteTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_invite", Title = "Delete invite", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently revokes one exact invite after full invite-state validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact invite code.")] string code,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current invite-state digest.")] DeleteInviteInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_invite", request_id, code, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateWebhookTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_webhook", Title = "Create webhook", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one incoming webhook, stores its token in the local secret vault, and returns only an opaque stored-webhook ID.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact channel digest, name, and optional avatar asset.")] CreateWebhookInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("create_webhook", request_id, input.ChannelId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateWebhookTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_webhook", Title = "Update webhook", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates one exact webhook name, avatar asset, or destination without exposing its token.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact webhook snowflake ID.")] string webhook_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact state digest and typed update fields.")] UpdateWebhookInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_webhook", request_id, webhook_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteWebhookTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_webhook", Title = "Delete webhook", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact webhook and its optional local stored credential.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact webhook snowflake ID.")] string webhook_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact webhook state and optional opaque stored-webhook ID.")] DeleteWebhookInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_webhook", request_id, webhook_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SendWebhookMessageTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "send_webhook_message", Title = "Send webhook message", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Executes one stored webhook with bounded content, explicit mentions, thread, and approved assets, waiting for the message receipt.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Send(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Opaque stored-webhook ID, webhook digest, content, thread, mentions, and assets.")] SendWebhookMessageInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("send_webhook_message", request_id, input.StoredWebhookId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class EditWebhookMessageTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "edit_webhook_message", Title = "Edit webhook message", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Edits one exact message created through a stored webhook with explicit attachment retention.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Edit(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Stored webhook, exact message state, content, mentions, thread, and attachments.")] EditWebhookMessageInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("edit_webhook_message", request_id, input.StoredWebhookId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteWebhookMessageTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_webhook_message", Title = "Delete webhook message", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact message created through one exact stored webhook.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Stored webhook and exact webhook/message state evidence.")] DeleteWebhookMessageInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_webhook_message", request_id, input.StoredWebhookId, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class CreateGuildTemplateTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_guild_template", Title = "Create guild template", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one configured-guild template only when current configuration matches the approved digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Printable operator intent.")] string reason,
        [Description("Complete configuration digest, name, and description.")] CreateGuildTemplateInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync("create_guild_template", request_id, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class SyncGuildTemplateTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "sync_guild_template", Title = "Sync guild template", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Replaces one exact template snapshot only when guild configuration and template state match approved digests.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Sync(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact guild template code.")] string code,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact configuration and template-state digests.")] SyncGuildTemplateInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("sync_guild_template", request_id, code, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class UpdateGuildTemplateTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_guild_template", Title = "Update guild template", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates one exact template name or description after full template-state validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact guild template code.")] string code,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact template digest and typed set/clear metadata.")] UpdateGuildTemplateInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("update_guild_template", request_id, code, reason, input, cancellationToken);
}

[McpServerToolType]
public sealed class DeleteGuildTemplateTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_guild_template", Title = "Delete guild template", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact configured-guild template after state validation.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique operation UUID.")] string request_id,
        [Description("Exact guild template code.")] string code,
        [Description("Printable operator intent.")] string reason,
        [Description("Exact current template-state digest.")] DeleteGuildTemplateInput input,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync("delete_guild_template", request_id, code, reason, input, cancellationToken);
}
