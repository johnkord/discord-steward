# Implementation plan

Each phase must leave the repository buildable, tested, and safe to stop. Do not combine Phase 1 with mutation work.

## Phase 0: safe bootstrap

Status: complete.

- .NET 10 solution and xUnit project.
- Stable MCP and Discord REST packages pinned.
- Local `stdio` MCP host with stderr-only logging.
- `get_steward_status` with explicit safety annotations.
- Pure configuration parsing that stores only token presence.
- VS Code MCP launch profile and CI workflow.
- Warnings as errors and eight passing tests, including MCP stdio discovery.

## Phase 1: read-only Discord substrate

Status: complete. MCP discovery, network-free tests, and an owner-authorized read-only guild smoke test pass.

Goal: inspect exactly one configured guild without reading messages or exposing any mutation tool.

### Work

1. Add validated `DiscordOptions` for `GuildId` and `BotToken`.
2. Add a secret-owning Discord session abstraction. Never put the token in a record used by tools, status, logging, or exceptions.
3. Initialize `DiscordRestClient` with `TokenType.Bot`. Do not add `DiscordSocketClient` or Gateway intents.
4. Preflight the current bot identity and configured guild. Reject resources from any other guild.
5. Define normalized immutable domain records for guilds, roles, channels, categories, and overwrites. Keep Discord.Net objects inside the adapter.
6. Add `IDiscordGuildReader` and a Discord.Net REST implementation.
7. Add an in-memory fake reader for tests.
8. Implement `get_guild_snapshot`, `get_role`, and `get_channel` as R1 tools.
9. Implement a pure first-pass `GuildLinter` and expose `lint_guild_configuration`.
10. Add optional `get_recent_audit_events` only if exact permission and Discord.Net behavior are verified.
11. Add structured safe errors and correlation IDs.
12. Update `get_steward_status` with live connection/capability state, without leaking credentials.

Implemented notes:

- The four required read tools are registered explicitly alongside `get_steward_status`.
- The optional audit-event tool was not added. Phase 1 therefore does not require `ViewAuditLog`.
- Steward compares canonical category and child overwrite arrays to identify drift.
- Role hierarchy manageability and effective permission calculation remain Phase 2 work.
- Setup, exact REST reads, and observed Discord.Net limitations are recorded in [Phase 1 operations](phase-1-operations.md).

### Tests

- Missing, malformed, and mismatched guild configuration.
- Resource IDs from another guild are rejected before returning data.
- Snapshot normalization is deterministic regardless of API ordering.
- Managed roles and `@everyone` are represented correctly.
- Category and channel overwrite ordering is deterministic.
- No output DTO contains a token or message body field.
- Permission denial becomes a stable capability error.
- Tool annotations match the risk table.
- Discord exceptions are mapped without sensitive raw content.

### Definition of done

- `dotnet build DiscordSteward.sln` has zero warnings.
- `dotnet test DiscordSteward.sln` passes.
- MCP Inspector or VS Code discovers all read tools.
- Tests make no network calls.
- A live smoke test, if the owner provisions a dedicated bot, performs reads only in the configured guild.
- No project references unrelated Discord bot implementations.

The definition of done requires the offline gates above. Any optional live smoke test must use a dedicated private bot and disposable guild, perform only the documented reads, and keep its observed IDs outside the public repository. Require fresh owner approval before changing bot permissions, replacing credentials outside the rotation procedure, or adding any live operation beyond the Phase 1 read surface.

## Phase 2: deterministic permission explanation

Status: role-only scope complete. Member-specific analysis remains deliberately unavailable.

Implement Discord's documented permission algorithm as a pure domain service:

- Owner and `Administrator` behavior.
- Union of guild role permissions.
- `@everyone`, role, and member overwrite ordering.
- Implicit effects of denying `ViewChannel`, `SendMessages`, and `Connect`.
- Thread inheritance rules.
- Role hierarchy constraints for Steward actions.

Build a fixture matrix before exposing `explain_role_permissions`. Compare selected fixtures against Discord.Net and a disposable guild, but keep the pure implementation as the explainable source of truth.

Implemented notes:

- `explain_role_permissions` accepts exact `role_id` and optional exact `channel_id`; it never accepts a guild or permission bitfield.
- The pure trace exposes raw and expanded guild permissions, every overwrite stage, post-overwrite permissions, implicit effects, final effective permissions, and limitations.
- Owner and `Administrator` expansion, overwrite precedence, text and voice dependencies, thread-parent inheritance, and hierarchy constraints have focused fixtures.
- The known permission catalog is checked against `GuildPermission` from Discord.Net 3.20.1.
- Active thread discovery is not added; thread inheritance is implemented for normalized snapshots that include a parent.
- Member overwrites are counted and explicitly excluded. The tool does not fetch or accept member identity.
- Role hierarchy rules are pure and tested, but live manageability remains `unknown` because the snapshot intentionally does not fetch the bot member's highest role.
- Owner-authorized live validation should use a private disposable guild and keep all resulting IDs and operation records outside the public repository.

## Phase 3: plans and policy

Status: historical implementation, retired by Phase 7. Legacy rows remain read-only evidence.

- Add SQLite through a repository abstraction.
- Add schema versioning and migrations.
- Check in a deny-by-default policy schema.
- Implement canonical JSON and SHA-256 per-resource preconditions.
- Add immutable expiring plans and typed operation records.
- Add plan, inspect, list, and cancel tools.
- Keep all Discord writes absent.

Implemented for the initial slice:

- SQLite schema version 2 with strict status and approval-mode constraints, a fail-safe version 1 migration, and restricted local file permissions.
- Canonical SHA-256 plan and per-channel precondition digests.
- One immutable expiring `channel_update` operation per plan.
- Deny-by-default local policy with an explicit `reversibleMutations` mode and operation allowlist.
- `plan_channel_update` and `get_change_plan` MCP tools.

## Phase 4: configurable approval governance

Status: historical implementation, retired. In the recommended `InteractiveApproval` mode, operator approval belongs exclusively to the MCP host. A later schema-4 mode can explicitly choose unrestricted autonomy.

- Add `externalApproval` as the default mode with a separate `discord-steward approve <plan-id>` command.
- Add `trustedOperator` for controlled local sessions where the exact-ID apply call is the approval event.
- Keep every new plan pending; planning must never approve or apply its own output.
- Persist the required mode, approval timestamp, and approval against the immutable plan digest.
- Bind the selected mode into the policy digest so mode changes invalidate older plans.
- Add cancellation and expiry behavior.
- Do not accept approval mode, approval booleans, replacement operations, or bypasses through MCP arguments.

In `externalApproval`, the command requires the literal phrase `APPROVE <plan-id>` after displaying exact IDs, before/desired values, required permissions, expiry, precondition digest, and plan digest. In `trustedOperator`, the executor atomically records approval for the exact pending plan before Discord preflight. Both paths use the same digest-bound approved lifecycle and execution gate. There is no approval MCP tool.

## Phase 5: reversible executor

Status: historical executor evidence retained; its direct-operation replacement is implemented in Phase 7.

Begin with a very small allowlist:

- Create a text channel under an existing category.
- Rename a channel or edit its topic/slow mode.
- Archive a channel without deleting it.
- Create a role with no elevated permissions.
- Apply a bounded overwrite that does not grant a denied permission.

Add journaled execution, state revalidation, post-write verification, Discord audit reasons, compensating actions, and failure-injection tests before enabling mutation mode.

The first executor slice:

- Requires `ManageChannels` and rejects a bot holding `Administrator`.
- Revalidates policy fingerprint, stored approval mode, approval digest, expiry, guild ID, protected-resource policy, and exact channel precondition.
- Persists `executing` before one Discord PATCH.
- Uses `Discord Steward plan <plan-id>` as the audit-log reason.
- Re-reads the exact channel and persists success, verified no-change failure, or partial failure.
- Never retries an uncertain write. A stale `executing` plan is recovered by reading state only.
- Has no multi-step rollback because each released plan contains one PATCH operation; unexpected state fails closed for owner review.
- Works around a verified Discord.Net 3.20.1 defect where a specified null topic is dereferenced before the request. Topic clearing sends an empty string and snapshot normalization treats empty as no topic.

See [the mutation testing plan](mutation-testing-plan.md) before enabling this mode outside tests.

## Phase 6: reassess high-risk operations

Do not assume these should be built:

- Channel or role deletion.
- Kick, ban, timeout, or nickname changes.
- Webhook or invite management.
- AutoMod mutation.
- Bulk changes.
- Member-specific permission changes.

Require a fresh owner decision and threat-model update for each category. Keeping an operation manual can be the correct final design.

## Phase 7: move approval above the tool boundary

Status: complete. Direct `update_channel`, exact manifests, schema-3 operation evidence, v2 migration, legacy evidence projection, and read-only reconciliation are implemented and have owner-authorized disposable-guild evidence. Later catalog expansions still require their own focused release evidence.

Goal: in `InteractiveApproval`, every MCP call is approved by the operator in the host before invocation, and every invoked Steward tool completes its authorized operation without an internal approval handshake.

1. Add schema version 3 operation tables alongside legacy plans. Store a versioned operation discriminator, schema version, globally unique `request_id` primary key, canonical call digest, redacted audit representation, exact targets, preconditions, execution claim, timestamps, observed state, and outcome. Tool name and target IDs are indexes, not parts of the idempotency key.
2. Add a typed `OperationRegistry` whose definitions supply schema, fixed risk class, required Discord permissions, conflict-domain validator, one Discord writer, result verifier, and read-only reconciler.
3. Implement the shared direct-operation pipeline for immutable-manifest checks, policy, protected resources, configured-guild ownership, permission, hierarchy, precondition, one-write, verification, and uncertain-outcome handling.
4. Implement reusable `IResourceDigestValidator` services for channel, parent, overwrite, ordering, and role-hierarchy conflict domains. Digests are versioned and computed from normalized state.
5. Implement typed field updates: bounded `set` records, explicit `clear` enums, no JSON null, no arbitrary patch, and dedicated move/reorder inputs for position changes.
6. Implement `update_channel` with the complete desired change visible in its arguments and behavior parity with the legacy channel executor.
7. Reserve a caller-generated UUID `request_id` globally before the write using one SQLite transaction and a database uniqueness constraint. Exact reuse returns prior evidence; reuse with a different canonical call fails with `idempotency_conflict`.
8. Add a configured `toolAllowlist`; support exact names and an exclusive all-tools selector, resolve it against implemented definitions and independent R2/R3/R4/R5 gates into an immutable startup manifest, journal the manifest digest, expose no runtime availability mutation, and diagnose custom manifests missing planning-read or recovery companions.
9. Split `get_steward_status` into local-only `get_steward_capabilities` and live R1 `get_bot_effective_permissions`; rename `get_guild_snapshot` to `get_guild_configuration_snapshot`.
10. Add redacted `get_operation`, `list_operations`, and read-only `reconcile_operation` for durable evidence and uncertain outcomes.
11. Remove `plan_channel_update`, `get_change_plan`, `apply_change_plan`, `PlanApprovalCommand`, and approval-mode policy only after direct-tool parity tests pass.
12. Keep old plan rows readable as historical evidence but make them non-executable through the new surface.
13. Update MCP discovery tests to prove mutation calls contain complete explicit effects and accept no plan, approval, bypass, force, guild, route, JSON null, runtime enablement, or arbitrary patch arguments.
14. Document that standard MCP cannot attest host approval. Interactive writes remain disabled for unattended or auto-approving hosts; future signed attestation belongs in transport metadata, not a tool.

Definition of done:

- The exact `update_channel` call shown for approval is the call journaled and executed.
- The registered tool set exactly matches the startup manifest and cannot change without restart; the all-tools profile matches the complete catalog.
- A stale digest, protected resource, policy denial, excessive bot permission, missing bot permission, or unavailable journal stops before Discord writes.
- Duplicate or colliding `request_id` calls cannot repeat or alias a write.
- Concurrent reservations of the same `request_id` prove one winner at the database constraint and deterministic exact-reuse or conflict behavior for every loser.
- A handler cannot issue more than one Discord mutation request.
- Ambiguous outcomes are verified or recorded as unknown without blind retry; reconciliation performs reads only.
- R1 aggregate and channel outputs contain no member IDs from overwrites; exact member-overwrite reads require R2.
- Post-write Discord state and local durable evidence agree.
- The legacy approval CLI and plan/apply tools are absent from MCP discovery.

## Phase 8: catalog coverage expansion

Status: implemented offline. All 173 catalog operations have typed schemas, fixed risk classes, manifest definitions, exact MCP registration, precondition sources, and read or write adapters. Mutation families use the common journaled one-write executor and endpoint-specific verification. The complete catalog can be registered in one process; live release evidence is still organized by operation family.

Validate operation families independently in this order:

1. `observer`: focused guild, channel, thread, role, event, community, expression, lint, and analysis reads without sensitive operation history or new Discord permissions.
2. `channelOperator` and `roleOperator`: reversible channel, overwrite, role appearance, guild setting, welcome, and widget operations as separate exact manifests.
3. `communitySafetyOperator` and `eventOperator`: AutoMod, onboarding, incidents, scheduled events, stages, and templates. AutoMod enablement is R4 because it changes member and message treatment.
4. `moderator`: bounded member and audit reads, role assignment, nickname, timeout, voice moderation, kick, exact-ID ban, bounded exact-ID bulk ban, and unban. Member prune remains preview-only, and bans always request zero history deletion because Discord otherwise selects message targets dynamically.
5. `publisher`: guild message reads, best-effort indexed search, sends, reactions, pins, polls, and separate send/crosspost announcement calls after Message Content and retention review.
6. `expressionOperator`: configured asset inbox, immutable content-addressed vault, centralized asset validation, and typed emoji, sticker, and soundboard administration.
7. `integrationOperator`: ordinary and targeted invites, explicit asynchronous target-user job status, announcement follows, sanitized integration reads, and token-vaulted incoming webhooks. Integration removal remains excluded because Discord chooses associated side effects.
8. Validate individually reviewed R5 tools only when the independent irreversible gate is enabled. Unbounded bulk member actions, dynamic prune, and multi-write announcement macros remain excluded.

Each released operation must have a typed fixed-risk schema, exact required permissions, one documented Discord mutation request, conflict-domain digest, post-write verifier, read-only reconciliation strategy, failure injection, MCP annotation tests, and disposable-guild evidence. Destructive operations additionally require a typed cascade analyzer or an explicit bounded categorical-effect justification. Creates require an exact-ID/code or explicitly non-reconcilable recovery classification. Official asynchronous endpoints additionally require an `accepted`/`processing` lifecycle, resource-level in-flight exclusion, and a bounded status-read tool. If Discord.Net 3.20.1 lacks the endpoint, add a narrow typed HTTP adapter with the same error mapping and rate-limit controls rather than a generic REST escape hatch.

The all-tools manifest may include capabilities the current Discord credential does not hold; each invocation still checks its exact required permissions and fails closed. Prefer a separate bot identity when credential-level isolation is required.

## Phase 9: explicit authorization modes

Status: implemented. Existing policy schema 3 profiles remain `InteractiveApproval`. Policy schema 4 requires an explicit `authorization.mode` and supports `InteractiveApproval` or dangerous `UnrestrictedAutonomy`.

Unrestricted mode is intentionally difficult to select accidentally:

1. Require policy schema 4 and the exact `UnrestrictedAutonomy` enum value.
2. Require `toolAllowlist: ["*"]`; partial autonomous manifests are rejected.
3. Force every risk gate open and register the complete catalog.
4. Discard protected resource IDs, protected name prefixes, and denied permissions.
5. Permit an Administrator bot, Administrator role grants, protected member or role targets, and exact deletion cascades with blockers.
6. Preserve configured-guild binding, required Discord permission checks, Discord hierarchy constraints, typed inputs, state and asset preconditions, globally unique request IDs, one-write claims, durable operation evidence, and post-write verification.
7. Record authorization mode, policy digest, manifest digest, profile digest, and bounded MCP request metadata with every operation.
8. Report an explicit danger warning through `get_steward_capabilities`.

This mode is for operators who consciously accept autonomous caller risk. It must not be presented as an approval convenience or enabled by default.