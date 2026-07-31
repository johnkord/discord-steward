# MCP tool contract

Status: active contract. The direct-operation foundation and all 173 catalog operations are implemented. The recommended trusted local runtime exposes the complete catalog through one immutable guild-bound profile. The complete catalog and migration mapping are in [the tool surface redesign](tool-surface-redesign.md).

## Authorization boundary

In the default `InteractiveApproval` mode, all Discord Steward tools execute below the MCP host's operator-approval layer.

- The agent constructs a complete tool call before execution.
- The host presents the exact tool name, arguments, annotations, and expected effect.
- The operator approves or rejects the invocation.
- Once invoked, the tool performs the authorized operation without another approval exchange.

Steward does not expose approval tools or accept `approved`, approval-mode, `force`, or bypass arguments. Standard MCP does not attest that a host displayed an approval prompt, so interactive mode trusts the configured local host to enforce this boundary. Tool annotations describe risk but are not authorization by themselves. The interactive all-tools profile must not be used with an unattended, auto-approving, or otherwise untrusted host.

Policy schema 4 may explicitly select `UnrestrictedAutonomy`. That mode trusts autonomous callers, forces the complete catalog and all risk gates open, discards protected and denied policy, permits Administrator, and relaxes deletion blockers. It makes no operator-approval claim. Guild binding, typed schemas, current Discord permission checks, state and asset preconditions, durable request IDs, one-write execution, and verification remain mandatory.

Every effect must be visible in the complete call before invocation. Opaque plan IDs, saved scripts, arbitrary patches, mutable server-side profiles, and hidden macros are not valid mutation inputs.

## Common rules

Every tool satisfies these rules:

- The guild comes from trusted process configuration. No tool accepts `guild_id`.
- Multi-server deployments use one immutable profile and process per guild. The selected profile atomically binds guild, manifest, policy, and local data paths.
- Discord snowflakes are decimal strings. Invite and guild-template codes are accepted only for resource types Discord officially identifies by code. These exact typed identifiers are identity; names remain display or search data.
- Inputs are bounded typed fields, never arbitrary JSON, routes, permission integers, or local file paths.
- Results are structured, versioned, bounded, and paginated where needed.
- Discord text is untrusted display data and is never interpreted as instructions.
- Bot tokens, OAuth tokens, webhook tokens, executable webhook URLs, raw exceptions, headers, and stack traces are never returned. Invite and guild-template codes are gated resource identifiers, not hidden secrets; only their dedicated R2/R4 tools return them.
- Every tool has explicit MCP `ReadOnly`, `Destructive`, `Idempotent`, and `OpenWorld` annotations.
- Every tool has one risk class determined by its name and typed schema, never by a runtime target subtype.
- Local configuration contains a `toolAllowlist`. The selector `"*"` expands to every implemented operation and cannot be combined with exact names. Startup intersects the result with open risk gates to produce the immutable registered manifest; unknown exact names fail startup.
- In `InteractiveApproval`, R2 through R5 tools additionally require their independent local risk gate. Disabled tools are absent from discovery. Unrestricted mode forces all gates open.
- In `InteractiveApproval`, the host requests approval before every tool, including reads and local analysis.

Every Discord resource read is bound to the configured guild before data is returned or a write is attempted. Steward uses a configured-guild route where Discord provides one, compares a returned `guild_id` when present, and validates channel or parent lineage when an object has no guild field. A write response is checked again during verification. A Discord error alone is never treated as guild-ownership proof.

R1 configuration reads do not return member identities hidden inside permission overwrites or incidental creator/uploader fields on AutoMod rules, templates, expressions, and events. They expose role overwrites plus member-overwrite counts and state digests. Scheduled-event subscriber counts and role membership counts are also R2. Exact member data requires a separately registered R2 tool. The configured guild owner ID is retained only because deterministic owner-permission analysis requires it. Aggregate snapshots and lint results preserve the same boundary.

## Typed field semantics

Mutation schemas do not accept JSON Patch, Merge Patch, arbitrary property bags, or raw nullable DTOs.

- Create calls require every effect-bearing choice, including Discord defaults, to be explicit in the approved arguments.
- Update calls use a typed `set` object for new values and a bounded `clear` enum for clearable fields. Fields absent from both are unchanged.
- JSON `null` is invalid. Clearing must be visible in `clear` or in a resource-specific state transition such as `present: false`.
- A field cannot appear in both `set` and `clear`, and an update must request at least one change.
- Collection replacement is named explicitly and includes the complete desired collection. Incremental membership tools use exact IDs plus a desired boolean.
- Position and hierarchy changes use dedicated move or reorder tools; ordinary update tools cannot move resources.

This representation preserves the difference between omitted, false, empty, and cleared values in the canonical invocation digest without adding one clear tool per field.

## Effectful calls

Every mutation accepts:

- `request_id`: caller-generated UUID used as a globally unique idempotency and correlation key across the journal.
- `reason`: printable operator intent suitable for local and Discord audit evidence.
- Exact target IDs and complete typed `set`, `clear`, replacement, or desired-presence values.
- `expected_state_digest` or a resource-specific equivalent obtained from a prior approved read.

The journal reserves `request_id` through a database `PRIMARY KEY` before a write. Reuse with the same canonical call digest returns the existing result and never repeats the write. Reuse with any different tool, target, argument, or asset digest fails with `idempotency_conflict`. A terminal failed request ID is not recycled.

Create tools bind mutable containers explicitly. A channel create call includes the exact parent category and `expected_parent_state_digest`; a top-level create includes an `expected_guild_state_digest`. Snapshot, archive, template-sync, move, and reorder calls similarly include complete desired values plus digests for every resource or ordering they can affect.

Precondition digests are versioned hashes of normalized conflict domains, not display values. An update digest covers the full target resource. A relationship or hierarchy operation also covers the relevant parent, sibling order, overwrite set, or role ceiling. Activity outside that declared conflict domain does not invalidate the call.

Steward does not expose writes whose target set is chosen dynamically at execution time. Discord's guild-prune endpoint is excluded because a count preview cannot bind the exact members later removed. Ban calls always request zero history deletion because a duration would let Discord select message targets after approval.

Mutation tools then:

1. Canonicalize the typed call and validate its shape, startup manifest, policy, target protection, and asset references.
2. Atomically reserve the global `request_id` with tool name, canonical call digest, exact targets, and redacted audit representation. Fail closed if the journal is unavailable.
3. Through the configured guild, re-read the smallest complete conflict domain and validate guild ownership, precondition digests, current Discord permissions, role hierarchy, protected resources, and asset digests.
4. Atomically claim execution and persist the preflight evidence.
5. Immediately issue one Discord mutation request. Do not perform unrelated remote work between preflight and the write, broaden the effect, or automatically retry an ambiguous write.
6. Persist any returned resource IDs before other fallible local work, then re-read the exact affected state and verify the approved result when Discord processes it synchronously.
7. Persist a terminal result, a documented `accepted`/`processing` state, or an uncertain outcome with safe recovery evidence.

A single Discord endpoint may affect a bounded explicit ID list, such as a reorder call, when every target and side effect is visible before approval. Destructive calls include a typed `expected_cascade` of related channels, threads, invites, webhooks, events, roles, or other configuration references that Discord may change. If the cascade cannot be enumerated, the write is unsupported unless deletion of the collection is the explicit categorical effect, such as all content inside one exact channel. If an operation requires a second Discord mutation request, split it into a second independently approved tool call. For example, sending and crossposting an announcement are separate calls.

`analyze_deletion_impact` is a read, not a plan or authorization artifact. It returns bounded typed cascade fields and digests that the agent must place visibly in the later delete call. The delete tool does not accept an analysis ID or look up hidden desired effects.

A documented Discord background job is not a second Steward write. Such a tool returns `accepted`, stores the exact resource and originating request ID, and remains `processing` until a separately approved status read observes `completed` or `failed`. Because Discord's targeted-invite job has no job ID, Steward permits only one in-flight target-user job per invite code; another create or update fails with `operation_in_progress`.

Discord offers no conditional-write transaction, so a narrow race remains between final preflight and the API accepting a write. Post-write verification detects observable drift; it cannot prove that no concurrent actor wrote the same desired value. Steward records that residual uncertainty rather than claiming atomicity.

A stale precondition fails with `state_conflict` before the write. The tool does not silently adapt. A retry after any failed preflight is a new host-approved invocation with a new `request_id`; reusing the original ID only returns its durable result.

Verification compares normalized exact desired state, not merely HTTP status. Permission tools compare the complete expected allow and deny sets or overwrite array with Discord's observed representation. If Discord accepted a write but observed state differs, return `partial_failure` with safe field-level evidence; do not relabel it as a pre-write conflict or claim success.

## Assets and credentials

Asset tools read only regular, non-symlink files from a configured local asset inbox. They never accept a path or fetch a URL. `register_asset` binds an inbox candidate ID, SHA-256 digest, media type, and byte length into an immutable, size-limited vault entry. Every Discord call using an asset includes both `asset_id` and `asset_digest`; a mismatch fails before the write.

Webhook tokens live in a separate encrypted local secret vault with restrictive file permissions, never in the operation journal. Tool results expose only webhook IDs. Discord has no atomic webhook-token rotation endpoint, so credential replacement is two explicit operations: create a replacement webhook, move callers, then delete the old webhook. A suspected token is disabled through local policy until that workflow completes.

## Result envelope

Use this common shape where practical:

```json
{
  "schemaVersion": 1,
  "outcome": "succeeded",
  "guild": {
    "id": "667956000757776386",
    "name": "Example Guild"
  },
  "data": {},
  "warnings": [],
  "error": null
}
```

Effectful results additionally include `requestId`, canonical call digest, exact target IDs, write state (`not_attempted`, `attempted`, `processing`, `verified`, or `unknown`), execution timestamps, verification evidence, and current safe outcome. Created credentials remain server-side. The caller uses `requestId` as the operation-journal key; Steward does not expose a second competing operation identifier.

Outcomes are `succeeded`, `accepted`, `failed`, `partial_failure`, or `unknown`. `accepted` is nonterminal and is used only when an official Discord endpoint documents asynchronous server-side processing; it is not a synonym for an ambiguous response.

Errors use a stable object containing `code`, `phase`, `retryable`, `operatorAction`, and bounded safe details. `state_conflict` identifies changed fields and the newly observed digest without returning secrets. `state_missing` means a target or prerequisite disappeared. `role_hierarchy_denied` distinguishes an unmanageable target from stale field values.

Stable errors include:

- `invalid_argument`
- `not_configured`
- `tool_not_enabled`
- `discord_authentication_failed`
- `discord_permission_denied`
- `guild_mismatch`
- `resource_not_found`
- `result_too_large`
- `state_missing`
- `ambiguous_name`
- `policy_denied`
- `role_hierarchy_denied`
- `state_conflict`
- `asset_mismatch`
- `credential_unavailable`
- `idempotency_conflict`
- `operation_in_progress`
- `audit_unavailable`
- `rate_limited`
- `partial_failure`
- `outcome_unknown`

## Uncertain outcomes

`outcome_unknown` means Steward durably claimed execution and may have sent the Discord request, but no reliable response or verified state proves the result. It is not a generic timeout error.

- Steward never reissues that write, including when the same `request_id` is submitted again.
- `reconcile_operation` performs only endpoint-specific reads: exact returned IDs, desired-state verification, message nonce where supported, or an exact Discord audit reason and time window.
- Reconciliation never selects a created resource by display name. Multiple plausible candidates remain unknown and are returned only as bounded evidence for operator review.
- If reliable evidence resolves the result, the journal records `succeeded` or `failed`. Otherwise it remains `unknown` and directs the operator to inspect Discord before approving any new write.
- A new `request_id` is a new operation and may duplicate an unconfirmed create. Interactive hosts must display that warning during approval; autonomous callers must account for it explicitly.

Every released create operation documents whether Discord returns an exact resource ID or invite/template code, which audit event can correlate it, and whether an endpoint nonce exists. If none can distinguish the result after a lost response, the operation is explicitly non-reconcilable and remains `unknown`; Steward never falls back to a unique-looking display name.

`partial_failure` is reserved for a single Discord endpoint that reports mixed outcomes for an explicit bounded target list, or for a Discord write that succeeded before required local credential persistence failed. It is not permission to run an automatic compensation or a hidden second write.

## Risk classes

| Class | Meaning | MCP posture |
| --- | --- | --- |
| R0 | Local metadata or pure analysis | Read-only or local-state annotations as applicable |
| R1 | Non-content guild configuration read | Read-only, open-world |
| R2 | Sensitive member, audit, operation, credential metadata, or content read | Read-only, open-world, opt-in gate |
| R3 | Reversible configuration write | Destructive false only when genuinely reversible; open-world |
| R4 | Communication, credential creation, or person-impacting write | Destructive true, open-world |
| R5 | Irreversible or bulk action | Destructive true, separate disabled-by-default gate |

Pre-call operator approval applies to all classes in `InteractiveApproval`. Local policy and Discord permissions remain independent defense-in-depth controls there. Unrestricted mode removes local protected and denied policy but retains Discord permissions and the execution invariants described above.

## Startup manifests

The recommended interactive profile uses `toolAllowlist: ["*"]` and opens all four gates, producing one immutable manifest containing the complete 173-tool catalog and every companion read. This avoids profile changes or MCP server restarts between unrelated operator requests.

The dangerous unrestricted profile requires policy schema 4, `authorization.mode: UnrestrictedAutonomy`, and exactly `toolAllowlist: ["*"]`. It forces all risk gates open regardless of configured gate values and reports zero protected resources, protected prefixes, or denied permissions.

Exact-name allowlists and closed gates remain supported for deployments that want schema-level restriction, but they are optional hardening rather than the normal request-routing mechanism. Capability status warns when a custom registered write has no visible precondition-source read or no uncertain-outcome recovery tool.

`get_steward_capabilities` reports only loaded local state: registered names, manifest digest, gate state, and configured permission requirements. `get_bot_effective_permissions` is the separate R1 Discord read for observed grants and role ceiling. A full-catalog union may report Discord permissions or privileged intents the bot does not have; only tools requiring a missing capability fail their own preflight. Neither status tool can alter the manifest. Allowlist or gate changes still require local configuration and a process restart.

The coverage inventory includes direct operations for:

- Local capability, operation-journal, snapshot, lint, permission, and simulation work.
- Guild, channel, category, thread, role, member, voice-state, and permission administration.
- Message, reaction, pin, poll, AutoMod, onboarding, incident, scheduled-event, and stage operations.
- Emoji, sticker, soundboard, audit-log, invite, integration, webhook, and guild-template administration.

See [the target tool catalog](tool-surface-redesign.md#target-tool-catalog) for every operation name, risk class, and purpose.

## Existing surface migration

The legacy `plan_channel_update`, `get_change_plan`, and `apply_change_plan` tools are removed in the target model. Their replacement is a direct `update_channel` call whose full target and desired fields are visible before invocation. The plan store becomes an immutable operation journal, exposed through `get_operation` and `list_operations`.

Existing read tools remain or receive clearer names:

- `get_steward_status` splits into local `get_steward_capabilities` and live `get_bot_effective_permissions`.
- `get_guild_snapshot` becomes `get_guild_configuration_snapshot`.
- `get_role`, `get_channel`, `lint_guild_configuration`, and `explain_role_permissions` remain and expand.

## Prohibited surface

Do not expose:

- Generic REST, shell, script, arbitrary patch, or raw permission-bitfield tools.
- Guild selection, creation, deletion, transfer, or leave operations.
- DMs, group DMs, arbitrary message surveillance, presence surveillance, or friend relationships in the default product.
- User tokens, self-bot operations, application secrets, token resets, or developer-team administration.
- A tool that changes Steward policy, authorization mode, or host approval policy.
- Hidden bulk or multi-write macros, runtime tool enablement, or a call whose effect cannot be understood from its complete arguments.

Activities, Embedded App SDK, Social SDK, local RPC, monetization, application-owner APIs, and long-running event receivers are separate products rather than Steward manifests. Their identities, user consent, network surfaces, and data policies do not fit this single-guild administration boundary.