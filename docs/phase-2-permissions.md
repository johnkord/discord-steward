# Phase 2 permission explanation

## Scope

Phase 2 adds deterministic role-only permission explanations without expanding the Discord REST surface. `explain_role_permissions` reads the same fresh normalized guild snapshot as the Phase 1 tools.

Inputs are:

- Required exact `role_id`.
- Optional exact `channel_id`, which may identify a category or guild channel.

The tool does not accept a guild ID, member ID, permission bitfield, role name, force flag, or arbitrary JSON. It does not read members or messages and cannot change Discord.

## Calculation order

The pure calculator follows Discord's documented order:

1. Start with the configured guild's `@everyone` role permissions.
2. Union the selected role's guild permissions. Role position does not resolve permission collisions.
3. Expand to all names in the pinned catalog for a guild owner or a role union containing `Administrator`.
4. If a channel is selected and no bypass applies, apply the `@everyone` channel deny set and then its allow set.
5. Combine all selected-role deny sets, combine all selected-role allow sets, then apply combined denies before combined allows.
6. Report member-specific overwrites as excluded from role-only analysis.
7. Preserve the post-overwrite set, then separately report and filter implicit effects for `ViewChannel`, `SendMessages`, and `Connect`.
8. For a thread snapshot, use the parent channel's overwrites and require `SendMessagesInThreads` rather than inherited `SendMessages`.

Every trace step includes stable source IDs, allowed names, denied names, the resulting sorted permission set, and a constant explanation. Discord names remain untrusted display data.

## Output distinctions

- `rawBasePermissions`: union of `@everyone` and the selected role before bypass expansion.
- `expandedBasePermissions`: base set after owner or `Administrator` expansion.
- `steps`: deterministic guild and overwrite trace.
- `permissionsAfterOverwrites`: explicit bit-like result before implicit dependencies.
- `implicitEffects`: permissions ignored because a prerequisite is absent.
- `effectivePermissionsComplete`: false when a future or unsupported channel kind prevents a complete implicit-rule assertion.
- `effectivePermissions`: final role-only result after those effects.
- `hierarchy`: whether role management can be asserted from available evidence.
- `limitations`: boundaries the caller must not infer past.

This distinction matters because Discord documents implicit permissions as ignored even when they remain present in the explicit overwrite calculation.

## Hierarchy boundary

The pure hierarchy evaluator enforces `ManageRoles`, rejects `@everyone` and integration-managed targets, and requires the actor's highest role to be above the target.

The public tool currently returns `unknown_observer_hierarchy`. The snapshot deliberately does not fetch the bot's guild-member profile or role IDs, so Steward cannot prove its highest role from trusted evidence. It must not infer identity from a role name.

## Pinned catalog and naming

Owner and `Administrator` expansion uses a string catalog checked by test against `GuildPermission` in Discord.Net 3.20.1. The adapter normalizes Discord.Net's legacy channel overwrite name `ManageEmojis` to the guild-role name `ManageEmojisAndStickers` before domain calculation.

Unknown permissions introduced after the pinned SDK require an explicit dependency and catalog review.

## Threads

The normalized channel model records an optional `parentChannelId`, and fixtures verify parent-overwrite inheritance and `SendMessagesInThreads` behavior.

If a thread snapshot lacks its parent, the MCP tool fails with a stable `resource_not_found` result rather than calculating from incomplete context.

The current guild-channel REST endpoint does not return active threads, and Steward does not call Discord's separate active-thread endpoint. Thread calculation is therefore available when a normalized snapshot contains a thread, but live active-thread discovery remains absent.

## Validation

The offline fixture matrix covers:

- Guild role union and overwrite precedence.
- Missing `ViewChannel`, `SendMessages`, and `Connect` effects.
- Owner and `Administrator` bypass.
- Excluded member overwrites.
- Thread-parent inheritance.
- Role hierarchy permission and position rules.
- Exact parity between the permission catalog and pinned Discord.Net names.

The official MCP client test verifies that `role_id` is required, `channel_id` is optional, all annotations are R1 read-only, and no guild or permission-bitfield input is exposed.

Owner-authorized live validation should use exact role and channel IDs from a private disposable guild. Record results outside the public repository and never substitute display names for identity.