# Discord API coverage audit

Discord Steward's 173-tool catalog covers the request-response operations identified in the July 26, 2026 single-guild administration audit. It is organized by operator intent, not one tool per Discord REST endpoint, and it does not cover the entire Discord developer platform.

This audit distinguishes additions that fit Steward's current execution model from deliberate safety exclusions and capabilities that require a separate long-running service. Discord.Net is an implementation dependency, not the product boundary; a missing SDK method can be implemented through a narrow typed HTTP adapter.

## Implemented expansion

The audit identified 13 new tools plus field-level expansions to existing tools. They now use the same typed, exact-target, host-approved, one-write model as the original catalog.

- `update_channel` now covers documented typed settings for text, announcement, voice, stage, forum, and media channels, including reversible text/announcement conversion and named channel flags. Forum/media creation includes tags and defaults. Flags are update-only because live Discord accepted but ignored `REQUIRE_TAG` during creation, which cannot satisfy verified one-write semantics.
- Normal messages, forum posts, and webhook execution support constrained rich embeds, approved attachments, explicit flags, static non-interactive Components v2, and endpoint-specific fields. Interactive component IDs and callbacks remain excluded.
- Role snapshots, digests, creation, and appearance updates preserve Discord's primary, secondary, and tertiary color model with enhanced-color invariants.
- `update_guild_access_controls` manages only `INVITES_DISABLED` and `RAID_ALERTS_DISABLED`, preserving all other features and binding the complete feature-set digest.
- `get_bot_guild_profile` exposes Steward's guild-specific nickname, avatar, banner, bio, and digest. `update_bot_guild_profile` safely manages nickname/avatar/banner with approved assets; bio is read-only because Discord silently ignored the documented PATCH field for the bot credential.
- `list_voice_regions`, `get_thread_member`, `list_channel_invites`, `list_sticker_packs`, `get_sticker_pack`, `list_default_soundboard_sounds`, `get_guild_preview`, and `get_guild_widget_image` provide the focused reference reads identified by the audit.
- `trigger_typing_indicator` emits one explicit ephemeral event. A confirmed 204 is journaled; an uncertain write is never retried because later state cannot prove the event occurred.
- `bulk_ban_members` accepts 2 to 25 explicit unique IDs, requires exact state evidence for current members, protects owner/self/configured IDs, deletes zero message history, and persists mixed results as partial failure.

## Covered areas that can look missing

- Guild emoji, sticker, and soundboard expression list/get/create/update/delete are covered by the typed guild-expression tools. Default Discord sounds are readable; soundboard playback remains a voice-service concern.
- Voice region, bitrate, quality, and user-limit fields are available during typed voice or stage creation and update.
- AutoMod CRUD supports exemptions plus the newer `MEMBER_PROFILE` trigger and `BLOCK_MEMBER_INTERACTION` action. This surface still needs periodic API-drift tests as Discord adds enum values or metadata fields.
- Role member counts, onboarding, welcome screen, incident actions, templates, voice status, and other SDK gaps already use focused reads or typed HTTP adapters where Discord.Net 3.20.1 is incomplete or unreliable.

## Deliberate safety exclusions

These APIs exist but conflict with Steward's exact-target, bounded-effect, or hard safety model.

- Begin guild prune. Discord chooses a changing set of members after approval; only prune preview is exposed.
- Ban-time message-history deletion. Discord selects messages by a time window; Steward bans with zero history deletion and requires separately approved exact message IDs.
- Guild integration deletion. Discord can delete associated webhooks and remove an associated bot as hidden side effects.
- `COMMUNITY` and `DISCOVERABLE` feature changes, plus any other operation requiring `Administrator`.
- Guild MFA-level changes, ownership transfer, guild creation/deletion, and owner-only operations.
- Unbounded or broad member-impacting bulk operations. Steward's bulk-ban subset is capped at 25 exact approved IDs even though Discord accepts up to 200.
- Adding members through user OAuth tokens, DMs, group DMs, user relationships, user connections, self-bot behavior, generic REST, arbitrary JSON, and credential-returning tools.

## Separate services

The following are unsupported by this local request-response MCP server because they need persistent connections, inbound networking, or callback-specific authorization.

- Gateway event ingestion for messages, member joins and leaves, reactions, polls, presence, voice state, AutoMod execution, audit-log events, and other real-time dispatches.
- Slash commands, context commands, autocomplete, buttons, selects, modals, and interaction callbacks.
- Incoming Discord event-webhook receivers.
- Persistent voice connections, voice audio send/receive, and soundboard playback.
- Continuous presence or typing behavior. One explicitly approved typing event is available through REST.

If implemented, these should be separate services with their own event authentication, durable queues, replay rules, approval model, and privileged-intent review. They should not be added as hidden behavior behind the existing MCP tools.

## Outside product scope

- Application metadata, installation settings, global or guild application-command administration, command permissions, and application emojis.
- OAuth2 bearer-token, team, application-transfer, token-reset, and client-secret operations.
- SKUs, subscriptions, entitlements, Social Commerce, and other monetization APIs.
- Activities, the Embedded App SDK, the Social SDK, provisional accounts, account linking, friends, lobbies, game DMs, and game voice.
- Role-connection metadata and user-account profile or relationship APIs.

## Ongoing compatibility work

Keep API-drift fixtures for Discord enums, flags, channel fields, message fields, permissions, and AutoMod types so SDK lag cannot silently narrow supported semantics. Re-run this audit when Discord changes its machine-readable resource index or the pinned Discord.Net version.

Do not treat endpoint count as the success metric. The useful boundary is complete, typed coverage of approved single-guild operator workflows without generic escape hatches or server-selected targets.