# Tool surface redesign

Status: implemented coverage catalog, researched July 25 and expanded July 26, 2026. All 173 entries have exact schemas and registry mappings. The recommended trusted local runtime advertises the complete catalog through one immutable guild-bound profile.

## Decision

In the recommended `InteractiveApproval` mode, Discord Steward tools sit below the operator approval boundary.

1. The agent prepares one complete MCP tool call.
2. The MCP host shows the tool name, exact arguments, risk annotation, and expected effect to the operator.
3. The operator approves or rejects the call before invocation.
4. On approval, the host invokes the tool once.
5. Steward validates policy and Discord state, records the invocation durably, performs only the described effect, and verifies the result.

Steward does not ask for approval after a tool starts. It does not expose an approval tool, an `approved` argument, a plan approval mode, or a second `apply` step.

This is a trust boundary, not a protocol guarantee. Standard MCP does not prove to the server that a host displayed an approval prompt. Interactive mode therefore trusts the configured local host to enforce pre-call approval. A future host-signed call attestation may strengthen this boundary, but it must be transport metadata bound to the canonical call hash, not a model-supplied boolean.

`UnrestrictedAutonomy` is an explicit alternate trust model. It does not claim operator approval, forces the complete catalog and all gates open, removes protected and denied policy, permits Administrator, and relaxes deletion blockers. It retains guild binding, typed calls, Discord permission checks, state preconditions, journaling, one-write execution, and verification.

## Consequences

The complete call must disclose its effect before invocation. A tool may not accept an opaque plan ID, saved script, arbitrary patch, server-side desired-state profile, or mutable file whose contents hide the action.

- Mutation tools take exact Discord IDs and typed `set`, `clear`, replacement, or desired-presence fields. JSON `null` is never an update instruction.
- The configured guild ID never appears as a tool argument.
- Every mutation takes a globally unique caller-generated `request_id`, a printable audit `reason`, and an appropriate `expected_state_digest` or equivalent precondition.
- Creates bind to an `expected_parent_state_digest` or `expected_guild_state_digest` for the mutable container they were reviewed against.
- A call may perform internal reads and permission checks after invocation, but it may not broaden its targets or effects.
- Failed preconditions fail closed. The tool returns a conflict; it does not ask for approval again or silently adapt.
- Retrying a failed operation requires a new invocation with a new `request_id` and, in interactive mode, fresh host approval. Reusing the same `request_id` returns the durable prior result without repeating a write; uncertain outcomes use the separate read-only reconciliation tool.
- No tool accepts `force`, `skip_checks`, raw permission integers, raw REST routes, arbitrary JSON patches, credentials, or approval fields.

## Execution contract

Every effectful tool follows the same server-side sequence:

1. Canonicalize the typed request and validate exact IDs, manifest, policy, protected targets, and asset references.
2. Atomically reserve the globally unique `request_id`, call digest, targets, reason, and redacted audit representation.
3. Re-read the complete conflict domain through the configured guild and verify ownership, preconditions, permissions, role hierarchy, and asset digests.
4. Claim execution and persist preflight evidence.
5. Immediately issue exactly one Discord mutation request. Include `Discord Steward <request_id>` in `X-Audit-Log-Reason` where Discord supports it.
6. Persist returned resource IDs before other fallible local work.
7. Re-read and verify the desired state for synchronous endpoints, or record documented asynchronous processing.
8. Persist observed state, timestamps, current outcome, and any safe error code.

For an ambiguous response, Steward never repeats the write or selects a resource by display name. `reconcile_operation` may use only exact returned IDs, an enforced nonce, desired-state verification, or an exact audit entry. Otherwise the result remains `outcome_unknown` for operator review.

When an official endpoint documents asynchronous processing, a verified HTTP acceptance returns `accepted` and leaves the journal in `processing`; this is not `outcome_unknown`. A separately approved status read records completion or failure. If Discord exposes no job ID, Steward allows only one in-flight job for that exact resource so status remains attributable.

## Tool design rules

- Prefer one operator intent per tool. Split delete, bulk, credential-creating, and people-impacting actions from ordinary updates so MCP annotations remain truthful.
- A tool has exactly one risk class. If risk changes by target type or action, split the tool or conservatively assign the higher class.
- Use exact Discord-defined identifiers for identity: decimal-string snowflakes, or invite/template codes only for resources officially keyed by code. Names are display or search data.
- Do not expose writes whose target set is selected dynamically by Discord at execution time. Approval may cover a bounded categorical effect such as deleting one exact channel, but not an unknown set such as "all currently inactive members."
- Return bounded, paginated, versioned results. Never return secrets or raw Discord exceptions.
- Default `allowed_mentions` to none. User and role mentions must be explicit exact IDs; `@everyone` and `@here` are unsupported.
- File and image tools accept only content-addressed assets imported from a configured asset inbox. Calls bind both asset ID and digest. They never accept arbitrary local paths or remote URLs.
- Webhook tokens stay in a local secret store. Tools return an opaque webhook ID, never a token or executable webhook URL.
- Local configuration supplies a `toolAllowlist`; `"*"` expands to every implemented definition, while exact names remain available for optional restricted deployments. Startup intersects the selection with open risk gates to form the immutable registered manifest.
- The recommended server advertises all 173 tools once. It does not switch profiles or restart based on the current request.
- In `InteractiveApproval`, read tools are still below the host approval layer and the host must request approval before reads as well as writes.

## Risk classes

| Class | Meaning | Examples |
| --- | --- | --- |
| R0 | Nonsensitive local metadata or deterministic computation | capabilities, snapshot comparison, simulations |
| R1 | Non-content guild configuration read | channels, roles, onboarding, events |
| R2 | Sensitive read | members, audit log, invites, webhooks, message content |
| R3 | Reversible guild configuration write | channel topic, role appearance, guild metadata |
| R4 | Communication, credential creation, or person-impacting write | send message, create invite, assign role, timeout |
| R5 | Irreversible or bulk action | delete channel, kick, ban, bulk message delete |

Host approval applies to every class in `InteractiveApproval`. Unrestricted mode removes that assumption and local protected/denied policy; bot permissions, preconditions, durable audit, and verification remain independent controls.

## Exposure model

The recommended interactive process loads `toolAllowlist: ["*"]` with all gates open and advertises the complete catalog. The process remains pinned to one guild and cannot alter its manifest at runtime. A deployment with multiple guilds still uses one process and profile per guild.

The dangerous unrestricted process requires policy schema 4 and `authorization.mode: UnrestrictedAutonomy`. It also requires the all-tools selector, forces all risk gates open, and reports no protected resources or denied permissions.

Narrow exact-name profiles remain available as optional schema-level hardening:

| Preset | Default | Scope |
| --- | --- | --- |
| `observer` | on | Status, configuration reads, lint, and role-only permission analysis. Sensitive operation evidence remains gated. |
| `channelOperator` | off | A selected subset of channel, category, overwrite, and thread operations. |
| `guildRoleOperator` | off | Guild metadata/routing, roles, welcome screen, and widget configuration. |
| `communitySafetyOperator` | off | AutoMod, onboarding, incidents, and guild safety policy. |
| `eventStageOperator` | off | Scheduled events, stage instances, and guild templates. |
| `moderator` | off | Member reads, role assignment, timeouts, voice moderation, kick, ban, and audit evidence. |
| `publisher` | off | Guild-channel messages, reactions, pins, polls, and content reads. Never DMs. |
| `expressionOperator` | off | Asset inbox plus emoji, sticker, and soundboard administration. |
| `integrationOperator` | off | Invites and token-vaulted incoming webhooks. |

Presets do not imply permission to perform every risk class. Local policy also applies these independent gates:

| Gate | Default | Permits |
| --- | --- | --- |
| `sensitiveReads` | off | R2 member, content, audit, invite, and webhook metadata. |
| `reversibleWrites` | off | R3 guild configuration changes. |
| `personImpactingWrites` | off | R4 communication, member impact, or credential creation. |
| `irreversibleWrites` | off | R5 deletion, kick, ban, and bulk effects. |

The all-tools selector automatically includes `get_steward_capabilities`, operation evidence, reconciliation, and every precondition-source read, so companion warnings remain empty. A custom exact-name manifest may differ; `get_steward_capabilities` reports warnings for effectful tools that have no registered precondition-source read or uncertain-outcome recovery path.

In interactive mode, tool exposure is the intersection of manifest selection and startup risk gates, while invocation is controlled by host approval, static target policy, current Discord permissions and intents, hierarchy, state preconditions, and durable execution checks. Unrestricted mode forces exposure open and removes host approval plus static protected/denied policy. The manifest digest, authorization mode, and enabled names are journaled and reported by `get_steward_capabilities`; no tool can change them. A broadly permissioned non-Administrator bot identity is recommended; separate identities remain available when credential-level isolation matters.

## Target tool catalog

The inventory is organized by operator intent rather than mirroring every REST endpoint. `Support` means the operation belongs in Discord Steward's long-term coverage; it does not mean it is enabled or advertised by a particular startup manifest.

### Core, audit, and analysis

| Tool | Risk | Purpose |
| --- | --- | --- |
| `get_steward_capabilities` | R0 | Report loaded manifest, digest, risk gates, configured required permissions, transport, version, and safety limits without contacting Discord or returning credentials. |
| `get_bot_effective_permissions` | R1 | Read the bot's current guild permissions and highest role, compare them with exact manifest requirements, and flag excess or missing grants. |
| `get_bot_guild_profile` | R1 | Return Steward's guild-specific nickname, avatar hash, banner hash, bio, and exact state digest. |
| `get_operation` | R2 | Return one redacted durable invocation and verified outcome by exact `request_id`. |
| `list_operations` | R2 | Return bounded redacted operation history filtered by tool, outcome, target ID, or time. |
| `reconcile_operation` | R2 | Refresh exact target or audit evidence for one `request_id` and resolve an uncertain outcome when possible; never repeat its write or choose a result by display name. |
| `capture_guild_configuration_snapshot` | R1 | Persist a content-free snapshot with role overwrites plus only counts and digests for member overwrites. |
| `compare_guild_configuration` | R1 | Compare a current or supplied snapshot digest with a prior local snapshot. |
| `lint_guild_configuration` | R1 | Detect risky permissions, hierarchy, channel drift, exposed resources, and missing protections. |
| `explain_role_permissions` | R1 | Deterministically explain one role's guild and optional channel permissions. |
| `explain_member_permissions` | R2 | Fetch and explain one exact member's sensitive role memberships, overwrites, timeout effects, and hierarchy. |
| `analyze_role_hierarchy` | R1 | Show the bot's role ceiling and which exact roles it can manage. Member-specific manageability remains in R2 tools. |
| `analyze_channel_access` | R1 | Produce a role-oriented access matrix for one channel or category. |
| `analyze_privilege_escalation_paths` | R1 | Find combinations such as role management plus webhook or channel control that cross trust boundaries. |
| `analyze_moderation_configuration` | R1 | Review verification, AutoMod, incident actions, moderator roles, and alert routing. |
| `analyze_onboarding_configuration` | R1 | Check welcome screen and onboarding consistency, public access, and Discord enablement constraints. |
| `analyze_deletion_impact` | R2 | Return blockers, bounded typed cascade IDs, and versioned digests for one exact supported resource. The caller must copy these fields into any later delete call. |

### Guild and configuration reads

| Tool | Risk | Purpose |
| --- | --- | --- |
| `get_guild` | R1 | Return normalized guild metadata, features, limits, safety levels, and configured system channel IDs. |
| `get_guild_configuration_snapshot` | R1 | Return a content-free aggregate with role overwrites and redacted member-overwrite counts/digests, plus community settings and expressions. |
| `get_guild_preview` | R1 | Return the configured guild's public preview metadata and aggregate expression counts. |
| `get_guild_widget` | R1 | Return widget enablement and target channel. |
| `get_guild_widget_image` | R1 | Return one bounded Discord-rendered widget PNG with exact style, media type, digest, byte length, and base64 content. |
| `get_welcome_screen` | R1 | Return welcome-screen description and exact channel entries. |
| `get_onboarding` | R1 | Return prompts, options, roles, channels, mode, and enablement. |
| `get_incident_status` | R1 | Return raid detection and temporary invite or DM disablement state. |
| `get_guild_vanity_url` | R1 | Return the configured guild's vanity code and usage count when the guild has that feature. |
| `list_guild_templates` | R2 | List sanitized templates whose source is the configured guild, including sensitive shareable codes but omitting creator identity. |
| `get_guild_template` | R2 | Return one configured-guild template by exact shareable code, including dirty state but not creator identity. |
| `list_voice_regions` | R1 | List global or guild-specific voice regions that may be used for an explicit channel RTC region. |
| `list_sticker_packs` | R1 | List Discord's public standard sticker packs and bounded sticker metadata. |
| `get_sticker_pack` | R1 | Return one public standard sticker pack by exact ID. |
| `list_default_soundboard_sounds` | R1 | List Discord's public default soundboard metadata without downloading audio. |

### Channels, categories, and threads

| Tool | Risk | Purpose |
| --- | --- | --- |
| `list_channels` | R1 | List normalized categories and non-thread guild channels with role overwrites and redacted member-overwrite counts/digests, excluding messages. |
| `get_channel` | R1 | Return one exact non-thread channel, role overwrites, redacted member-overwrite count/digest, parent, flags, forum tags, and type-specific settings. |
| `list_channel_member_overwrites` | R2 | Page exact member-target overwrite IDs and named permissions for one exact channel. |
| `list_active_threads` | R2 | List active public, announcement, private, forum, and media thread metadata visible to the bot. |
| `list_archived_threads` | R2 | Page archived public, private, or joined threads for one exact parent. |
| `get_thread` | R2 | Return one exact thread and content-adjacent metadata without message bodies. |
| `list_thread_members` | R2 | Page exact thread membership when a member-capable manifest and the sensitive-read gate are enabled. |
| `get_thread_member` | R2 | Return exact membership metadata for one exact user in one exact thread without unrelated profile data. |
| `create_category` | R3 | Create a category with explicit position and role overwrites only, bound to the approved guild channel-list digest. Member overwrites use an R4 follow-up. |
| `create_text_channel` | R3 | Create a text channel with explicit parent, topic, slow mode, NSFW state, and role overwrites only, bound to exact parent state. |
| `create_announcement_channel` | R3 | Create an announcement channel with explicit parent, role overwrites, and settings in an eligible Community guild, bound to parent state. |
| `create_voice_channel` | R3 | Create a voice channel with explicit parent, bitrate, region, quality, user limit, and role overwrites only, bound to parent state. |
| `create_stage_channel` | R3 | Create a stage channel with explicit parent, voice settings, and role overwrites only, bound to parent state. |
| `create_forum_channel` | R3 | Create a forum with explicit parent, role overwrites, tags, layout, sorting, reaction, and post defaults, bound to parent state. |
| `create_media_channel` | R3 | Create a documented media channel with explicit stable fields and role overwrites only, bound to exact parent state. |
| `update_channel` | R3 | Update typed non-position, non-overwrite fields valid for the exact channel type. Replaces legacy `plan_channel_update` plus `apply_change_plan`. |
| `move_channel` | R3 | Change one channel's parent or position while preserving its exact overwrite set, with digests for the channel, destination, and affected ordering. |
| `move_channel_and_sync_permissions` | R4 | Move one channel to a different exact parent and set Discord's `lock_permissions` in the same positions request. Same-parent sync uses overwrite replacement. |
| `reorder_channels` | R3 | Submit the complete desired order for the exact affected sibling set, including IDs whose positions shift indirectly. |
| `set_role_channel_permissions` | R3 | Set named allow, deny, and inherit permissions for one exact role in one exact channel. |
| `remove_role_channel_permissions` | R3 | Remove one exact role overwrite from one exact channel. |
| `set_member_channel_permissions` | R4 | Set named allow, deny, and inherit permissions for one exact member in one exact channel. |
| `remove_member_channel_permissions` | R4 | Remove one exact member overwrite from one exact channel. |
| `replace_channel_permission_overwrites` | R4 | Replace the complete explicit overwrite array in one channel PATCH. Parent sync binds the parent digest; category changes also bind the exact synced-child IDs and digests that Discord will update. |
| `archive_channel` | R4 | Apply explicit approved archive fields: destination category, name, visibility, and complete overwrite set. No convention is resolved after invocation. |
| `restore_archived_channel` | R4 | Apply explicit approved parent, name, visibility, and complete overwrite set matching a reviewed before-archive snapshot. |
| `set_voice_channel_status` | R3 | Set or clear one exact voice-channel status. Because Steward has no voice connection, require both `Set Voice Channel Status` and `Manage Channels`. |
| `trigger_typing_indicator` | R4 | Trigger one ephemeral typing indicator in an exact text-capable guild channel or thread. A confirmed 204 is journaled; an uncertain write is never retried because the event cannot be observed later. |
| `follow_announcement_channel` | R4 | Follow one announcement channel into one exact destination, creating an internal follower webhook. |
| `create_thread` | R3 | Create a public, private, or announcement thread with explicit lifecycle settings and an optional exact source message. |
| `create_forum_post` | R4 | Create one forum or media post and its initial message with up to five exact tags plus an `expected_available_tags_digest` obtained before approval. |
| `update_thread` | R3 | Explicitly set name, archived, locked, auto-archive duration, slow mode, tags, invitation state, or pin state. |
| `set_thread_member` | R4 | Add or remove one exact member from one exact thread using an explicit `present` value. |
| `delete_thread` | R5 | Permanently delete one exact thread when its manifest entry and the irreversible gate are enabled. |
| `delete_channel` | R5 | Permanently delete one exact channel with the complete typed `expected_cascade` from deletion-impact analysis. Message content is the explicit categorical deletion. |

### Roles and role assignment

| Tool | Risk | Purpose |
| --- | --- | --- |
| `list_roles` | R1 | List exact roles, hierarchy, colors, icons, flags, permissions, and managed state. |
| `get_role` | R1 | Return one exact role, Steward protection, and configuration-reference summary without member identities or membership counts. |
| `get_role_member_counts` | R2 | Return Discord's role member counts without downloading all members. |
| `create_role` | R3 | Create a role with explicit named permissions, colors, icon asset, hoist, and mentionability. |
| `update_role_appearance` | R3 | Update name, colors, icon, hoist, or mentionability without changing permissions. |
| `set_role_permissions` | R4 | Replace one role's complete named permission set after escalation analysis. |
| `reorder_roles` | R4 | Submit the complete desired order for the exact affected role set below the bot's highest role, changing the moderation hierarchy. |
| `set_member_role` | R4 | Add or remove one exact role from one exact member using an explicit `present` value. |
| `delete_role` | R5 | Permanently delete one exact unprotected, unmanaged role only when deletion-impact analysis proves member count zero and no channel, AutoMod, onboarding, integration, or policy references. |

### Members, voice state, and moderation

| Tool | Risk | Purpose |
| --- | --- | --- |
| `get_member` | R2 | Return one exact guild member, role IDs, nickname, timeout, flags, and voice state without unrelated profile data. |
| `search_members` | R2 | Prefix-search bounded members when the privileged members capability is enabled. Names are result data, never identity. |
| `list_members` | R2 | Page members with strict limits and data minimization. |
| `list_members_by_role` | R2 | Return bounded exact member IDs for one exact role. |
| `get_member_voice_state` | R2 | Return one exact member's current guild voice state. |
| `list_bans` | R2 | Page bans and safe reasons. |
| `get_ban` | R2 | Return ban state for one exact user ID. |
| `preview_member_prune` | R2 | Return Discord's prune count for explicit inactivity days and included roles. |
| `set_member_nickname` | R4 | Set or clear one exact member's guild nickname. |
| `set_member_timeout` | R4 | Set one exact member's timeout to an explicit timestamp or clear it explicitly. |
| `set_member_voice_channel` | R4 | Move one exact connected member to one exact voice channel or disconnect them explicitly. |
| `update_member_voice_state` | R4 | Explicitly set server mute and/or server deafen for one exact connected member. |
| `set_member_verification_bypass` | R4 | Explicitly set the membership-screening bypass flag for one exact member. |
| `kick_member` | R5 | Remove one exact member with hierarchy checks and mandatory reason. |
| `ban_member` | R5 | Ban one exact user with mandatory reason and zero Discord-selected message deletion. Remove history only through separately approved exact message IDs. |
| `bulk_ban_members` | R5 | Ban 2 to 25 explicit unique user IDs in one Discord request, with exact current-member state evidence, owner/self/protection checks, zero history deletion, and durable partial-success observation. |
| `unban_member` | R4 | Remove one exact ban with mandatory reason. |

### Messages, reactions, pins, and polls

This family is excluded from the default product. Reads and search require `Read Message History`; receiving arbitrary content may require the Message Content privileged intent. Sends do not require that privileged intent, and Steward never reads DMs.

| Tool | Risk | Purpose |
| --- | --- | --- |
| `get_message` | R2 | Return one exact guild-channel message with bounded content and attachment metadata. |
| `list_messages` | R2 | Page a bounded window in one exact channel. |
| `search_messages` | R2 | Use Discord's typed guild search filters with strict result limits and no automatic retries of indexing jobs. |
| `list_channel_pins` | R2 | Page current pins for one exact channel. |
| `list_message_reactions` | R2 | Page exact users for one emoji reaction. |
| `list_poll_voters` | R2 | Page exact voters for one answer of one exact poll message. |
| `send_message` | R4 | Send a new message, reply to one exact message, or forward one readable exact message using an explicit mode, source digest, allowed mentions, bounded content, approved assets, and enforced nonce. It never auto-publishes. |
| `edit_own_message` | R4 | Edit a Steward-authored message and explicitly retain or remove attachments. |
| `delete_message` | R5 | Delete one exact message. |
| `bulk_delete_messages` | R5 | Delete 2 to 100 explicit IDs, all younger than Discord's two-week limit. |
| `crosspost_announcement_message` | R4 | Crosspost one existing exact announcement message to its followers; it does not create a new source message. |
| `set_message_pinned` | R4 | Pin or unpin one exact message using an explicit `pinned` value. |
| `set_own_reaction` | R4 | Add or remove Steward's explicit Unicode or exact custom emoji reaction using an explicit `present` value. |
| `remove_user_reaction` | R5 | Remove one exact user's exact reaction. |
| `clear_message_reactions` | R5 | Remove all reactions, or all uses of one explicit emoji, from one exact message. |
| `create_poll` | R4 | Send an explicit poll with 2 to 10 answers, duration, and multiselect setting. |
| `end_poll` | R4 | End one Steward-authored poll immediately. |

Interactive buttons, selects, and modals are not enabled merely by supporting component-shaped messages. They require an inbound interaction service and a fresh approval architecture for callback effects. Link buttons and non-interactive Components v2 layouts are safe message content.

### AutoMod, community, events, and stages

| Tool | Risk | Purpose |
| --- | --- | --- |
| `list_automod_rules` | R1 | List normalized AutoMod rules and exemptions with creator identity omitted. |
| `get_automod_rule` | R1 | Return one exact rule with creator identity omitted. |
| `simulate_automod_rule` | R0 | Test explicit sample text or profile text locally without sending it to Discord. |
| `create_automod_rule` | R4 | Create an explicit rule. AutoMod changes member or message behavior and is always treated as person-impacting. |
| `update_automod_rule` | R4 | Update exact name, event type, trigger metadata, actions, enabled state, exempt role IDs, or exempt channel IDs. Trigger type is immutable. |
| `set_automod_rule_enabled` | R4 | Enable or disable one exact rule. |
| `delete_automod_rule` | R5 | Permanently delete one exact rule. |
| `update_guild_metadata` | R3 | Update explicit guild name, description, locale, boost progress display, or digest-bound icon/banner/splash assets. |
| `update_guild_system_routing` | R3 | Update system-message flags, AFK settings, and exact system, rules, updates, or safety-alert channel IDs. |
| `update_guild_safety` | R4 | Conservatively gate prospective verification, notification-default, or content-filter policy changes that affect members. |
| `update_guild_access_controls` | R4 | Atomically set invite pausing and raid-alert suppression from an exact feature-set digest while preserving every other feature. |
| `update_bot_guild_profile` | R3 | Update Steward's guild-specific nickname, avatar, or banner from exact state evidence and approved image assets. Bio remains read-only because bot-token live validation showed Discord silently ignoring that PATCH field. |
| `update_guild_widget` | R3 | Enable or disable the widget and choose its exact channel. |
| `update_welcome_screen` | R3 | Replace explicit welcome-screen entries and description. |
| `update_onboarding` | R4 | Atomically replace the complete prompt and option set, including future exact role and channel grants, defaults, mode, and enablement. |
| `set_incident_actions` | R4 | Temporarily disable invites or member DMs until explicit timestamps. Steward rejects values more than 24 hours in the future instead of truncating them. |
| `list_scheduled_events` | R1 | List scheduled and active event configuration with creator identities and subscriber counts omitted. |
| `get_scheduled_event` | R1 | Return one exact event's configuration with creator identity and subscriber count omitted. |
| `list_scheduled_event_subscribers` | R2 | Page exact subscribers when member access is enabled. |
| `create_scheduled_event` | R4 | Create a visible voice, stage, external, or supported recurring event with explicit fields. |
| `update_scheduled_event` | R4 | Update explicit public metadata, times, entity type, channel, external location, recurrence rule, or cover asset for one exact nonterminal event. Status transitions use separate tools. |
| `start_scheduled_event` | R4 | Irreversibly transition one exact scheduled event to active, potentially notifying or exposing it to subscribers. |
| `complete_scheduled_event` | R5 | Irreversibly transition one exact active event to the terminal completed state. |
| `cancel_scheduled_event` | R5 | Irreversibly transition one exact scheduled event to canceled. |
| `delete_scheduled_event` | R5 | Permanently delete one exact event after current-state, creator or manager permission, and entity-channel preflight. |
| `get_stage_instance` | R1 | Return the live stage instance for one exact stage channel. |
| `start_stage_instance` | R4 | Start a stage with explicit topic, scheduled event, and notification choice. |
| `update_stage_instance` | R3 | Update the privacy level of one exact live stage, the field currently exposed by Discord's documented modify endpoint. |
| `end_stage_instance` | R4 | End one exact live stage. |
| `set_stage_speaker` | R4 | Suppress or unsuppress one exact member already in one exact stage channel. |
| `request_stage_speaking` | R4 | Set or clear Steward's own request-to-speak state. |

Community feature enablement requiring `Administrator`, such as enabling Community or Discovery, remains manual because Steward never runs with `Administrator`.

### Expressions and approved assets

| Tool | Risk | Purpose |
| --- | --- | --- |
| `register_asset` | R0 | Import one exact regular-file candidate from the configured inbox by candidate ID, expected digest, media type, and byte length into an immutable size-limited vault. |
| `list_assets` | R0 | List bounded immutable asset metadata and digests from the configured asset inbox and vault. |
| `get_asset` | R0 | Return metadata and digest for one local asset ID, not raw secret data. |
| `delete_asset` | R0 | Remove one unreferenced local asset. |
| `list_guild_expressions` | R1 | List sanitized emoji, sticker, or soundboard metadata by explicit type with uploader identities omitted. |
| `get_guild_expression` | R1 | Return one exact sanitized emoji, sticker, or soundboard sound by type and ID with uploader identity omitted. |
| `create_guild_expression` | R3 | Create one typed expression from a digest-bound asset and type-specific explicit metadata. |
| `update_guild_expression` | R3 | Update one exact typed expression's supported metadata. |
| `delete_guild_expression` | R5 | Permanently delete one exact typed expression. |

Playing soundboard audio requires a maintained Gateway voice connection and is not a request-response administration tool.

### Audit logs, invites, integrations, webhooks, and templates

| Tool | Risk | Purpose |
| --- | --- | --- |
| `list_audit_events` | R2 | Page up to 100 events filtered by exact actor, action type, or cursor; Discord retains entries for 45 days. |
| `list_invites` | R2 | List guild invites and metadata. |
| `list_channel_invites` | R2 | List sanitized invite metadata and exact codes for one exact channel. |
| `get_invite` | R2 | Return one exact invite code only when an integration manifest and the sensitive-read gate are enabled. |
| `list_invite_target_users` | R2 | Parse one targeted invite's CSV allowlist into bounded exact user IDs and return its state digest; reject responses above local byte or user limits. |
| `create_invite` | R4 | Create one bounded invite with explicit channel, lifetime, uses, temporary state, stream or application target, and optional role grants. It does not upload a target-user list. |
| `create_targeted_invite` | R4 | Create one invite with a bounded exact user-ID allowlist encoded internally as CSV. Return `accepted` while Discord's documented per-invite job processes it. |
| `update_invite_target_users` | R4 | Replace one invite's bounded exact-user allowlist using `expected_target_users_digest` and return `accepted`; do not invent a job ID that Discord does not provide. |
| `get_invite_target_user_job_status` | R2 | Read the current per-invite target-user job by exact invite code and originating `request_id` without waiting or polling internally. |
| `delete_invite` | R5 | Revoke one exact invite. |
| `list_integrations` | R2 | List up to Discord's 50 accessible guild integrations with tokens omitted. |
| `list_webhooks` | R2 | List sanitized guild or channel webhooks with tokens and executable URLs omitted. |
| `get_webhook` | R2 | Return one sanitized exact webhook. |
| `create_webhook` | R4 | Create an incoming webhook and store its token in the local secret vault; return only an opaque ID. |
| `update_webhook` | R4 | Update one exact stored webhook's name, avatar asset, or exact destination. |
| `delete_webhook` | R5 | Permanently delete one exact webhook and its local secret. |
| `send_webhook_message` | R4 | Execute one stored webhook with explicit content, thread, mentions, and approved assets; wait for Discord to return the created message. |
| `edit_webhook_message` | R4 | Edit one exact message created through a stored webhook. |
| `delete_webhook_message` | R5 | Delete one exact message created through a stored webhook. |
| `create_guild_template` | R4 | Create a shareable template of the configured guild only when current configuration matches the approved configuration digest. |
| `sync_guild_template` | R4 | Replace one exact template snapshot only when current configuration matches the approved configuration digest. |
| `update_guild_template` | R3 | Update one exact template's name or description. |
| `delete_guild_template` | R5 | Permanently delete one exact template. |

## Existing tool migration

| Current tool | Target disposition |
| --- | --- |
| `get_steward_status` | Split into local-only `get_steward_capabilities` and live R1 `get_bot_effective_permissions`; remove server-side approval mode. |
| `get_guild_snapshot` | Rename to `get_guild_configuration_snapshot`; retain a content-free aggregate and add focused list/get tools. |
| `get_role` | Retain with richer hierarchy, color, and permission evidence. |
| `get_channel` | Retain and add all documented channel types and flags. |
| `lint_guild_configuration` | Retain and broaden across community, AutoMod, integrations, and escalation paths. |
| `explain_role_permissions` | Retain; add separately gated member explanation. |
| `plan_channel_update` | Remove. The host approves the complete `update_channel` call before invocation. |
| `get_change_plan` | Remove. Use `get_operation` for completed or in-progress invocation evidence. |
| `apply_change_plan` | Remove. Every effectful tool executes its own already-approved operation. |

The immutable plan store becomes an immutable operation journal. Existing rows remain readable for historical evidence but are not executable through the new surface.

## Explicit exclusions

The official Discord platform contains capabilities that do not belong in this single-guild administration server.

### Never expose

- Generic HTTP routes, arbitrary JSON bodies, shell execution, arbitrary permission integers, or user tokens.
- A tool that selects, creates, deletes, transfers, or leaves a guild.
- Self-bot operations or automation of a normal Discord user account.
- DMs, group DMs, friend relationships, user connections, presence surveillance, or arbitrary profile enrichment.
- Token reset, client-secret access, team management, application transfer, payout, or developer-account operations.
- Tools that return bot tokens, OAuth tokens, webhook tokens, executable webhook URLs, or other credentials.
- Unbounded bulk operations or hidden server-side macros.
- Dynamic-target writes such as Discord's guild-prune endpoint. `preview_member_prune` may report a count, but targets can change before execution; remove members only through explicit exact-ID tools.
- Ban-time history deletion. Discord selects messages dynamically from a duration; Steward always sends zero and uses separately approved exact-ID message deletion when needed.
- Guild integration removal. Discord may delete associated webhooks and remove an associated bot as server-selected side effects, so Steward reports integrations but does not remove them.

### Separate products, not Steward manifests

- Discord Social SDK: provisional accounts, account linking, relationships, unified friends lists, game invites, lobbies, linked-channel game chat, DMs, and game voice.
- Activities and Embedded App SDK: iframe applications, participant state, client authorization, purchases, Rich Presence, share dialogs, mobile layout, and Activity UI.
- Legacy Game SDK and local RPC: archived or client-side game integration, overlay, local voice settings, Rich Presence, and user-client control.
- Application owner and monetization APIs: app metadata, global commands, SKUs, subscriptions, entitlements, Social Commerce, application emojis, and role-connection metadata.
- Inbound Gateway, interaction, and event-webhook receivers: these are long-running services, not MCP request tools. If built, they require a separate network and event-processing threat model.

Guild application-command management is omitted because Steward has no interaction receiver and command-permission updates require a separately authorized user bearer token. Creating inert commands would add surface without an operator benefit.

Discord documents reading a vanity URL but not setting one through the bot API. Steward therefore exposes `get_guild_vanity_url` but no vanity mutation tool.

## Delivery order

1. Replace internal approval and plan/apply with the common direct-operation journal and migrate `update_channel`.
2. Complete focused configuration reads and analysis without adding new Discord permissions.
3. Add reversible channel, role, onboarding, AutoMod, event, and template writes as exact typed tools with companion reads and per-call preflight.
4. Add audit-log correlation and sensitive member reads only after explicit permission review.
5. Add messages and member-impacting operations as separate opt-in products with data-retention and privileged-intent review.
6. Add integration credentials and R5 tools last, each disabled by default and tested in a disposable guild.

No phase grants permissions for a later phase in advance.

## Research basis

The catalog was checked against Discord's current machine-readable documentation index and the following official families:

- [Discord developer documentation index](https://docs.discord.com/llms.txt)
- [API reference](https://docs.discord.com/developers/reference)
- [Guild, channel, message, user, voice, and permissions](https://docs.discord.com/developers/platform/server-and-channel-management)
- [Audit log, AutoMod, scheduled events, stages, expressions, invites, webhooks, polls, templates, and lobbies](https://docs.discord.com/developers/resources/guild)
- [Gateway and webhook events](https://docs.discord.com/developers/events/overview)
- [Application commands, interactions, and Components v2](https://docs.discord.com/developers/interactions/overview)
- [Activities and Embedded App SDK](https://docs.discord.com/developers/activities/overview)
- [Discord Social SDK](https://docs.discord.com/developers/discord-social-sdk/overview)
- [Discord.Net 3.20.1 REST API](https://docs.discordnet.dev/api/Discord.Rest.DiscordRestClient.html)

Discord.Net is an implementation dependency, not the product boundary. If a supported Discord endpoint is missing or stale in Discord.Net, Steward may use a narrow typed HTTP adapter for that endpoint. It must never expose a generic REST tool.