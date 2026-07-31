# Mutation testing plan

Status: direct `update_channel` implementation. Offline gates pass; a fresh owner-authorized direct-operation pilot is still required before production promotion.

## Released candidate

`update_channel` changes one standard text channel. The approved call contains:

- Globally unique UUID `request_id`.
- Exact `channel_id`.
- Printable `reason`.
- `expected_state_digest` from `get_channel.mutationStateDigest`.
- A typed `set` object for name, topic, or slow mode.
- Optional `clear: ["topic"]`.

It cannot create, delete, archive, reorder, move, change overwrites, edit roles, or affect members. It issues one Discord PATCH and never retries an ambiguous write.

## Policy

The checked-in example is observer-only. For a disposable-guild test, use schema 3 and enable only the required tools:

```json
{
  "schemaVersion": 3,
  "toolAllowlist": [
    "get_steward_capabilities",
    "get_bot_effective_permissions",
    "get_channel",
    "get_operation",
    "list_operations",
    "reconcile_operation",
    "update_channel"
  ],
  "riskGates": {
    "sensitiveReads": true,
    "reversibleWrites": true,
    "personImpactingWrites": false,
    "irreversibleWrites": false
  }
}
```

Keep protected IDs, name prefixes, and denied permissions from the example policy. Restart after any policy change. The recommended schema-3 profile uses `InteractiveApproval`; schema 4 can explicitly select dangerous `UnrestrictedAutonomy`. There is no approval command or model-supplied approval field.

## Offline gates

These automated checks must remain green:

1. Zero-warning solution build.
2. Normal tests run with all live authorization variables absent.
3. MCP discovery contains only resolved allowlist names, never plan/apply, approval, force, route, guild-selection, or arbitrary patch inputs.
4. Canonical-call tests bind request ID, target, reason, state digest, set values, and clear values.
5. SQLite tests prove global request-ID uniqueness, exact replay, collision rejection, one-winner execution claims, v2-to-v3 migration, legacy evidence preservation, and terminal evidence across reopen.
6. Executor tests cover malformed input, protected targets, stale state, missing permission, `Administrator` refusal, journal failure, concurrent exact calls, lost responses, unknown outcomes, and read-only reconciliation without another write.
7. R1 outputs contain no member overwrite IDs; the R2 tool is independently gated.
8. Configuration snapshot capture/compare uses immutable redacted JSON and exact resource IDs.

## Disposable-guild preparation

1. Install the dedicated private bot in a disposable guild.
2. Require `View Channels` and `Manage Channels`; additional non-Administrator permissions are allowed and reported as excess.
3. Keep every privileged Gateway intent disabled.
4. Confirm `get_bot_effective_permissions` reports no `Administrator`.
5. Create one disposable text channel and record its exact ID.
6. Enable the narrow tool manifest above and restart Steward.
7. Confirm `get_steward_capabilities` reports the expected manifest digest and no missing-companion warnings.

## Direct happy path

1. Call `get_channel` for the exact channel and record `stateDigest`, current topic, and slow mode.
2. Construct the complete `update_channel` arguments, including a new UUID request ID.
3. Verify the host displays the exact tool name and full JSON, then approve the invocation in the host.
4. Confirm the result is `succeeded`, `verified_desired_state`, and `verified: true`.
5. Call `get_operation` with the same request ID. Confirm call digest, target, manifest digest, execution start, completion, desired/observed evidence, and terminal status.
6. Invoke the identical call again only for the idempotency test. Confirm it returns the same terminal evidence and does not change completion timestamps.
7. Check Discord's audit log for `Discord Steward <request_id>`.
8. Restore the original state with a separately approved call and a new request ID.

## Fail-closed matrix

Run each case with a new request ID unless testing exact replay:

1. **Stale state:** read the digest, manually edit the channel, then invoke. Expect `state_conflict`, a durable failed preflight, and no Steward write.
2. **Permission denied:** remove `Manage Channels`. Expect `discord_permission_denied` before execution claim.
3. **Administrator refusal:** in the disposable guild only, grant `Administrator`, expect `policy_denied`, then remove it immediately.
4. **Protected target:** add the channel ID to `protectedResourceIds`, restart, and expect `policy_denied`.
5. **Request-ID collision:** reuse a prior request ID with any changed argument. Expect `idempotency_conflict` before Discord access.
6. **Concurrent exact calls:** invoke the same approved JSON concurrently. At most one writes; the other reports `operation_in_progress` or the terminal result.
7. **Unknown outcome:** use offline failure injection. Confirm no automatic retry and resolve only through `reconcile_operation` reads.
8. **Policy or manifest drift:** a reserved call created under old context must fail and require a new approved request ID.

Do not simulate disk corruption against the production data directory. Journal failures are covered offline.

## Production promotion

Promote only after disposable-guild evidence is recorded:

1. Review the exact manifest and current bot permission comparison.
2. Keep the bot below owner/moderator roles and without `Administrator`.
3. Add `Manage Channels` only while `update_channel` is in the active manifest.
4. Choose a harmless reversible topic or slow-mode change.
5. Read current state, approve the complete direct call in the host, execute, verify, inspect journal evidence, and inspect Discord audit evidence.
6. Restore the original state through another approved call.
7. Remove `update_channel` or close `reversibleWrites` and restart if ongoing mutation access is not desired.

## Stop conditions

Stop immediately if:

- The host does not show the exact complete call before approval.
- A write occurs before durable `executing` state.
- Any ambiguous result causes a second write.
- Observed state differs from both before and desired state.
- A disabled tool appears in discovery.
- The journal cannot persist execution or completion.
- The bot has broader permissions than the active manifest requires.

Preserve request IDs and the database for review. Use `reconcile_operation` before considering any new write after an unknown outcome.