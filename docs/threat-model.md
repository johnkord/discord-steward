# Threat model

## Security objective

In the recommended `InteractiveApproval` mode, allow the owner to inspect and deliberately change one Discord guild through natural language without turning an LLM, Discord message, MCP client, or leaked credential into an unbounded administrator. `UnrestrictedAutonomy` is a separate explicit trust model that accepts autonomous caller risk.

## Assets

- Discord bot token.
- Guild configuration, role hierarchy, channels, permissions, invites, webhooks, AutoMod rules, and members.
- The owner's intent and approval decisions.
- Approved tool arguments, request IDs, and state preconditions.
- Startup tool manifest, risk gates, policy digest, and asset digests.
- Durable audit and recovery records.
- Asset and webhook secret vaults.
- Resources owned by other bots or integrations that Steward must not accidentally alter.

## Trust boundaries

Trusted:

- Repository code reviewed by the owner.
- Local process configuration and .NET user-secrets.
- The configured guild ID.
- In `InteractiveApproval`, the local MCP host to display and bind operator approval to the exact call it invokes.
- In `InteractiveApproval`, the operator's pre-call approval decision.
- In `UnrestrictedAutonomy`, the autonomous caller and its host environment.

Untrusted:

- Model output and every MCP tool argument.
- Names, topics, descriptions, and other text read from Discord.
- Discord state that may change between read and action.
- Network failures, retries, rate limits, and partial API success.
- Third-party packages and copied community MCP implementations.

In `InteractiveApproval`, the host is trusted by deployment assumption, but its approval behavior is not attestable through standard MCP. If that assumption is unacceptable, Steward needs a host-signed call-attestation extension before writes can be enabled. Unrestricted mode makes no approval claim.

## Threats and controls

| Threat | Primary controls |
| --- | --- |
| Prompt injection in Discord content | Keep publisher manifests and content tools absent by default. Never read DMs. Treat all Discord text as display data, never instructions. |
| Model targets another guild | Guild ID exists only in trusted process configuration. Tools do not accept it. Validate every resource belongs to that guild. |
| Host invokes without operator approval | Deployment trusts the local host. Keep writes disabled for untrusted or unattended hosts; add host-signed call attestation if enforcement is required. |
| Autonomous caller is compromised or misdirected | Do not use `UnrestrictedAutonomy` by default. If explicitly selected, isolate its bot and guild, grant least Discord privilege, protect the host, retain audit evidence, and accept that local protected/denied policy is disabled. |
| Model changes arguments after approval | Host approval must bind the exact serialized call. Steward journals the canonical call digest before execution and accepts no follow-up fields. |
| Opaque reference hides the effect from approval | Mutation tools accept exact targets and desired values, never plan IDs, mutable profiles, scripts, or arbitrary patches. |
| Omitted, null, empty, and cleared fields are confused | Typed update schemas separate `set` from an explicit bounded `clear` list, reject JSON null, and require at least one change. |
| One tool hides a higher-risk variant | Every tool has one risk class fixed by its name and schema. Split role and member effects or assign the higher class. |
| A large schema catalog degrades model selection | Use exact typed names and descriptions, require host approval for the selected call, reject generic request tools, and keep all server-side policy and preflight checks independent of model selection. Restricted exact-name profiles remain available if a host cannot reliably select from the full catalog. |
| Tool adapts after approval | Re-read only to validate. Stale or mismatched state fails closed rather than changing targets, values, or strategy. |
| Discord selects targets dynamically after approval | Do not expose the write. Guild prune remains preview-only because its exact member targets can change between count and execution. |
| Stale call overwrites a human change | Versioned conflict-domain digests, a final re-read immediately before execution claim, and post-write verification. Discord still has no atomic conditional write. |
| Permission misunderstanding grants excess access | Pure permission analysis, explicit deny policy, role-hierarchy preflight, and no arbitrary bitfields. |
| Multi-step macro leaves mixed state | One Discord mutation request per invocation. A second write is a second independently approved call; no hidden automatic compensation. |
| Endpoint reports a mixed bounded result | Every target is explicit, per-target outcomes are preserved when Discord supplies them, and no failed target is retried automatically. |
| Bot credential is stolen | Dedicated private bot, no `Administrator`, role hierarchy ceiling, local secret storage, rotation runbook, and optional separate identities for operators who want credential-level isolation. Broad credentials are supported, but profile controls do not constrain a stolen token used outside Steward. |
| Token leaks through logs or tools | Never log configuration objects or headers. Store only a configured boolean in status. Redact Discord exceptions before audit. |
| Local MCP server is reached over the network | `stdio` only. No HTTP listener or remote transport in v1. |
| Client auto-approves a destructive tool | Do not enable write manifests with a host configured for automatic approval. MCP annotations are descriptive, not server-verifiable authorization. |
| Generic tool enables policy bypass | No raw REST route, generic request, shell, arbitrary permission, `force`, or `skip_approval` tools. |
| Sensitive read exceeds operator intent | Require the independent R2 gate, exact manifest entry, bounded pagination, redacted operation evidence, and data minimization. |
| R1 configuration leaks member identities | Redact member overwrite IDs, creator/uploader fields, and member counts. Preserve only role data, counts/digests where allowed, and the guild owner ID required for permission analysis. Exact member data requires R2. |
| Destructive action causes irrecoverable loss | Keep the independent R5 gate off by default. Use separately named delete, ban, kick, and bulk tools; prefer archive and deny-send operations. |
| Delete has hidden Discord cascades | Bind a typed exact cascade of dependent resources and configuration references, revalidate it immediately before the write, and reject unenumerable unrelated effects. |
| Ban deletes a dynamically selected message window | Always send zero history deletion. Message cleanup uses separately approved exact IDs. |
| File or URL input exfiltrates local data or triggers SSRF | Import only regular non-symlink files identified inside a configured asset inbox. Bind candidate ID, digest, media type, and size; never accept a path or URL. |
| Asset changes after approval | Every consuming call binds immutable asset ID plus digest and fails before Discord access on mismatch. |
| Webhook creation leaks a posting credential | Store encrypted tokens outside the journal, return only webhook IDs, and never return executable webhook URLs. Suspected credentials are disabled until explicit replacement and deletion complete. |
| Audit store is unavailable | Mutations fail closed before contacting Discord. |
| Duplicate or colliding request repeats a mutation | Reserve globally unique `request_id` before the write. Exact reuse returns prior evidence; reuse with a different canonical digest fails with `idempotency_conflict`. |
| Discord API response is ambiguous | Never automatically retry a claimed write. Record `outcome_unknown`; read-only reconciliation uses exact IDs, nonce, desired state, or audit correlation and never display names. |
| Discord background job cannot be attributed | Use `accepted` and `processing`, bind the originating request and exact resource, and allow only one in-flight job per resource when Discord exposes no job ID. |
| Cross-guild resource slips through an unscoped endpoint | Use configured-guild routes, compare returned guild IDs where present, validate parent lineage otherwise, and repeat ownership checks during verification. |
| Policy or manifest changes erase operational context | Journal manifest names and digest, risk gates, policy digest, and binary version at each process start. No MCP tool changes them. |
| Dependency compromise | Pin stable packages, review lock changes, use CI, and avoid opaque one-click community MCP packages. |

## Permission hazards

Discord's `Administrator` permission bypasses channel overwrites and is denied in `InteractiveApproval`. `UnrestrictedAutonomy` deliberately allows it, which removes a major blast-radius boundary. The bot can otherwise manage only roles and members below its highest role, so avoid moving the bot role upward unnecessarily.

The following are high-risk even when individually legitimate:

- `ManageRoles`, because it can alter privilege boundaries.
- `ManageChannels`, because permission overwrites can expose private spaces.
- `ManageWebhooks`, because webhooks are durable posting credentials.
- `BanMembers`, `KickMembers`, and `ModerateMembers`, because they affect people directly.
- `ManageGuild`, because it covers broad guild configuration and AutoMod access.
- `MentionEveryone`, which Steward should never need.

Interactive profiles universally forbid `Administrator` and may deny additional permissions. Unrestricted mode removes denied-permission policy. Required and excess permissions are reported so operators can choose their own credential posture. A broader bot token has a correspondingly larger impact if it is stolen and used outside Steward.

## Risk tiers

- R0: Local metadata or deterministic computation.
- R1: Non-content guild configuration reads.
- R2: Sensitive member, audit, credential metadata, or content reads.
- R3: Reversible guild configuration writes.
- R4: Communication, credential creation, or person-impacting writes.
- R5: Irreversible or bulk actions.

In `InteractiveApproval`, all tiers require host approval before invocation and R2 through R5 use independent risk gates. `UnrestrictedAutonomy` forces all gates open and does not assume host approval.

## Release gates before the first mutation

All of these must be true:

1. Read-only adapters and permission analysis have focused tests.
2. Tools cannot select a guild.
3. Interactive policy denies `Administrator`, protected resources, every tool absent from a custom exact startup manifest, and every closed risk gate; the all-tools selector expands only to implemented typed definitions. Unrestricted mode must be treated as a separate, explicitly accepted release posture.
4. Effectful tools expose complete targets and typed `set`, `clear`, replacement, or desired-presence values before invocation and require versioned state preconditions.
5. For `InteractiveApproval`, the selected MCP host requires operator approval for every tool and does not auto-approve writes. For unrestricted mode, the autonomous caller and host are explicitly trusted and isolated.
6. Audit persistence fails closed.
7. The operation journal reserves globally unique `request_id`, canonical call digest, operation discriminator, execution claim, and observed outcome before the write.
8. Every effectful tool issues at most one Discord mutation request and defines its conflict domain, verifier, and read-only reconciliation strategy.
9. The schema enforces `request_id` as a primary key, and concurrent-collision tests prove that only one reservation can win.
10. Permission verifiers compare complete normalized role or overwrite state; a post-write mismatch is a partial failure, never success.
11. R1 snapshot and resource fixtures prove member, creator, and uploader IDs are absent while permitted counts and digests remain stable.
12. Every destructive operation has a cascade analyzer or an explicit bounded categorical-effect justification.
13. Every create has exact-ID, code, nonce, or audit reconciliation evidence, or is explicitly documented as non-reconcilable.
14. Documented asynchronous jobs have an accepted/processing lifecycle, one-in-flight resource lock where needed, and a bounded status read.
15. Executor tests cover stale calls, guild mismatch, request-ID collision, asset substitution, cascade drift, partial failure, uncertain responses, and recovery without duplicate writes.
16. Asset and webhook vault failure modes are tested before their manifests ship.
17. Message-reading manifests require explicit Message Content and retention review when applicable; sending alone never grants or requires that privileged intent.
18. A disposable test guild validates exact Discord.Net or typed HTTP-adapter behavior.
19. The owner reviews the bot's actual Discord permissions, role position, exact manifest, and risk gates.

## Residual risks

- A compromised local account can access the bot token, host, and operation journal.
- A compromised, unattended, or auto-approving MCP host can invoke any registered tool without meaningful operator consent.
- Discord itself may return incomplete or eventually consistent state.
- Discord can change after Steward's final preflight because its API provides no atomic compare-and-swap condition.
- A legitimately approved tool call can still express the operator's mistaken intent.
- One Discord endpoint may have documented server-side effects or return a mixed result for an explicit target list.
- Package and MCP client vulnerabilities remain possible.

These risks support conservative permissions, small explicit calls, exact approval previews, and reversible operations. They do not justify pretending the system can be made perfectly safe.