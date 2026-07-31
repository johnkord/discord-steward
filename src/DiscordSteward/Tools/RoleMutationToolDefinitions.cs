using System.Collections.Immutable;
using System.ComponentModel;
using DiscordSteward.Operations;
using ModelContextProtocol.Server;

namespace DiscordSteward.Tools;

[McpServerToolType]
public sealed class CreateRoleTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "create_role", Title = "Create role", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Creates one explicit role after binding the call to the current roles-list digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Create(
        [Description("Globally unique caller-generated operation UUID.")] string request_id,
        [Description("Printable operator intent from 1 to 500 characters.")] string reason,
        [Description("Roles-state digest returned by get_guild_configuration_snapshot.")] string expected_roles_state_digest,
        [Description("Role name from 1 to 100 characters.")] string name,
        [Description("Complete named guild permission set; Administrator is rejected during creation.")] string[] permissions,
        CancellationToken cancellationToken,
        [Description("Optional role color in #RRGGBB form.")] string? color_hex = null,
        [Description("Whether to display members separately.")] bool is_hoisted = false,
        [Description("Whether anyone may mention the role.")] bool is_mentionable = false,
        [Description("Optional immutable local image asset ID.")] string? icon_asset_id = null,
        [Description("Optional Unicode role emoji, mutually exclusive with icon_asset_id.")] string? unicode_emoji = null,
        [Description("Optional enhanced secondary role color in #RRGGBB form.")] string? secondary_color_hex = null,
        [Description("Optional enhanced tertiary role color in #RRGGBB form; only Discord's holographic triplet is accepted.")] string? tertiary_color_hex = null) =>
        tools.ExecuteForGuildAsync(
            "create_role",
            request_id,
            reason,
            new CreateRoleInput(
                expected_roles_state_digest,
                name,
                [.. permissions],
                color_hex,
                is_hoisted,
                is_mentionable,
                icon_asset_id,
                unicode_emoji,
                secondary_color_hex,
                tertiary_color_hex),
            cancellationToken);
}

[McpServerToolType]
public sealed class UpdateRoleAppearanceTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "update_role_appearance", Title = "Update role appearance", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Updates explicit non-permission role fields after validating the exact role state digest.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Update(
        [Description("Globally unique caller-generated operation UUID.")] string request_id,
        [Description("Exact role snowflake ID.")] string role_id,
        [Description("Printable operator intent from 1 to 500 characters.")] string reason,
        [Description("State digest returned by get_role.")] string expected_state_digest,
        [Description("Typed appearance fields to set.")] RoleAppearanceSet set,
        CancellationToken cancellationToken,
        [Description("Explicit fields to clear: color/colors, secondary_color, tertiary_color, icon, or unicode_emoji.")] string[]? clear = null) =>
        tools.ExecuteAsync(
            "update_role_appearance",
            request_id,
            role_id,
            reason,
            new UpdateRoleAppearanceInput(expected_state_digest, set, ToImmutable(clear)),
            cancellationToken);

    private static ImmutableArray<string> ToImmutable(string[]? values) => values is null ? [] : [.. values];
}

[McpServerToolType]
public sealed class SetRolePermissionsTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_role_permissions", Title = "Set role permissions", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Replaces one exact role's complete named guild permission set after state and hierarchy checks.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique caller-generated operation UUID.")] string request_id,
        [Description("Exact role snowflake ID.")] string role_id,
        [Description("Printable operator intent from 1 to 500 characters.")] string reason,
        [Description("State digest returned by get_role.")] string expected_state_digest,
        [Description("Complete desired named guild permission set.")] string[] permissions,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "set_role_permissions",
            request_id,
            role_id,
            reason,
            new SetRolePermissionsInput(expected_state_digest, [.. permissions]),
            cancellationToken);
}

[McpServerToolType]
public sealed class ReorderRolesTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "reorder_roles", Title = "Reorder roles", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Submits exact role positions including every indirectly shifted role below the bot's ceiling.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Reorder(
        [Description("Globally unique caller-generated operation UUID.")] string request_id,
        [Description("Printable operator intent from 1 to 500 characters.")] string reason,
        [Description("Roles-state digest returned by get_guild_configuration_snapshot.")] string expected_roles_state_digest,
        [Description("Complete exact ID and position set for all shifted roles.")] RolePositionInput[] positions,
        CancellationToken cancellationToken) =>
        tools.ExecuteForGuildAsync(
            "reorder_roles",
            request_id,
            reason,
            new ReorderRolesInput(expected_roles_state_digest, [.. positions]),
            cancellationToken);
}

[McpServerToolType]
public sealed class SetMemberRoleTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "set_member_role", Title = "Set member role", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Adds or removes one exact manageable role on one exact member using an explicit present value.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Set(
        [Description("Globally unique caller-generated operation UUID.")] string request_id,
        [Description("Exact member user snowflake ID.")] string user_id,
        [Description("Exact role snowflake ID.")] string role_id,
        [Description("True to add the role; false to remove it.")] bool present,
        [Description("Printable operator intent from 1 to 500 characters.")] string reason,
        [Description("State digest returned by get_member.")] string expected_member_state_digest,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "set_member_role",
            request_id,
            user_id,
            reason,
            new SetMemberRoleInput(user_id, role_id, present, expected_member_state_digest),
            cancellationToken);
}

[McpServerToolType]
public sealed class DeleteRoleTool(CatalogMutationTools tools)
{
    [McpServerTool(Name = "delete_role", Title = "Delete role", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Permanently deletes one exact unprotected role only when current deletion impact remains empty and digest-bound.")]
    public Task<OperationToolResultEnvelope<CatalogMutationResult>> Delete(
        [Description("Globally unique caller-generated operation UUID.")] string request_id,
        [Description("Exact role snowflake ID.")] string role_id,
        [Description("Printable operator intent from 1 to 500 characters.")] string reason,
        [Description("State digest returned by get_role.")] string expected_state_digest,
        [Description("Cascade digest returned by analyze_deletion_impact with no blockers or references.")] string expected_cascade_digest,
        CancellationToken cancellationToken) =>
        tools.ExecuteAsync(
            "delete_role",
            request_id,
            role_id,
            reason,
            new DeleteRoleInput(expected_state_digest, expected_cascade_digest),
            cancellationToken);
}
