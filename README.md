# Discord Steward

Discord Steward is a safety-focused control plane for managing one Discord server through natural language. It exposes deterministic Discord administration capabilities through the Model Context Protocol (MCP). In the default `InteractiveApproval` mode, the MCP client supplies the language model and operator-approval UI; this server supplies typed operations, policy, Discord API access, verification, and auditability.

## Current status

The direct-operation foundation and full 173-operation catalog are implemented offline:

- A .NET 10 `stdio` MCP server builds and runs.
- A lazy `DiscordRestClient` session authenticates as `TokenType.Bot` and is pinned to one configured guild.
- Normalized immutable snapshots contain guild, role, channel, category, and permission-overwrite metadata. R1 results redact member overwrite IDs into count plus digest; the exact IDs require a gated R2 tool.
- Deterministic linting reports administrator roles, denied `@everyone` permissions, elevated managed/protected roles, duplicate names, member overwrites, public sensitive overwrites, and category drift.
- A pure permission explainer traces guild unions, channel overwrites, owner/Administrator bypass, implicit permission effects, and thread-parent inheritance.
- All 173 catalog operations are implemented and machine-checked against the manifest and MCP registry. A profile may use `toolAllowlist: ["*"]` with all risk gates open to register the complete catalog in one immutable guild-bound process.
- Stable safe errors include correlation IDs and never return raw Discord exceptions or credentials.
- Direct mutation tools accept the complete exact effect, prior state evidence, reason, and globally unique request ID. They reserve the call in SQLite, preflight state and permissions, claim execution, issue one Discord mutation request, and verify the result.
- `get_operation`, `list_operations`, and read-only `reconcile_operation` expose durable execution and recovery evidence. Legacy plan rows remain readable but cannot execute.
- Configuration baselines can be captured and compared by exact resource ID without becoming hidden desired-state plans.
- Sensitive reads, message operations, member effects, integration credentials, and irreversible operations remain classified by independent R2 through R5 gates. The recommended interactive profile opens all gates once at startup and relies on host approval plus Steward's per-call policy, permission, hierarchy, state, and audit checks. There is still no Gateway, HTTP listener, generic REST tool, user token, or LLM capability.

The complete catalog can be advertised by one trusted local MCP server. See [the tool surface redesign](docs/tool-surface-redesign.md).

The offline suite covers the full catalog, protocol schemas, state machines, safety refusals, and typed HTTP boundaries. Optional live tests are environment-gated and must use an owner-approved disposable guild. Keep live IDs, request records, and validation logs outside the public repository; see the [mutation testing plan](docs/mutation-testing-plan.md).

## Technology

| Component | Choice |
| --- | --- |
| Runtime | .NET 10 LTS |
| Supported platform | Linux x64 |
| MCP | Official `ModelContextProtocol` C# SDK 1.4.1 |
| Discord | `Discord.Net.Rest` 3.20.1 |
| Transport | Local `stdio` only |
| Tests | xUnit |
| Audit store | SQLite |

Only stable package versions are used. The MCP 2.0 release candidate is deliberately not used.

## Core workflow

In the default `InteractiveApproval` mode:

```text
inspect -> construct complete call -> operator approves in host -> reserve -> preflight -> claim -> write once -> verify -> audit
```

Every tool is below the host approval boundary. Once invoked, Steward does not ask for another approval and accepts no approval, force, or bypass argument. Standard MCP cannot attest that the host displayed a prompt, so `InteractiveApproval` must not be used with unattended or auto-approving hosts.

The target catalog is a 173-operation typed schema list. The recommended profile registers all names in one process. No tool can change that immutable manifest or its gates at runtime.

## Authorization modes

- `InteractiveApproval` is the default and recommended mode. It uses policy schema 3, host approval, protected resources, denied permissions, Administrator refusal, and configured risk gates.
- `UnrestrictedAutonomy` is an explicit schema-4 override for operators who intentionally trust an autonomous caller. It forces the complete catalog and all risk gates open, removes protected resources and denied permissions, permits an Administrator bot and Administrator role grants, and relaxes deletion blockers. It is not a stronger approval mode.

Unrestricted mode still enforces one configured guild, typed schemas, current Discord permissions, role hierarchy where Discord requires it, state and asset preconditions, globally unique request IDs, durable journaling, one Discord write per invocation, and post-write verification. See [server profiles](docs/server-profiles.md) before considering it.

## Safety invariants

- Use a dedicated private Discord bot, never a user token or self-bot.
- Pin each process to one configured guild. MCP tools must not accept a guild ID.
- Never grant the bot `Administrator` in the recommended `InteractiveApproval` mode. `UnrestrictedAutonomy` deliberately removes that refusal and should use an isolated credential with only the Discord permissions actually required.
- Broad non-Administrator bot permissions are supported for convenient profile switching. Excess permissions are advisory unless a profile explicitly denies them; this increases the impact of token theft outside Steward.
- Message-content and member tools must collect only the exact bounded guild data their approved calls require. Never read direct messages or unrelated user activity.
- Do not add an LLM provider or API key to this server.
- Keep `stdio` as the only transport until remote authorization is separately designed and reviewed.
- Do not implement `force`, `skip_approval`, arbitrary permission bitfields, or generic Discord request tools.
- In `InteractiveApproval`, require the configured MCP host to obtain operator approval before every tool call, including reads.
- Show the complete exact mutation effect in the approved call; do not hide effects behind plan IDs, profiles, scripts, or mutable files.
- Use typed `set`, explicit `clear`, replacement, or desired-presence arguments. Do not use null or arbitrary patches as update instructions.
- Treat MCP annotations as risk metadata. Standard MCP does not prove to Steward that the host displayed an approval prompt.
- Use the normal all-tools profile only with an interactive host that approves every call. Do not convert it to unrestricted mode merely to avoid prompts.
- Keep static local policy, exact preconditions, globally unique request IDs, least Discord privilege, durable execution claims, and post-write verification independent of approval.
- Issue at most one Discord mutation request per invocation. Multi-step workflows require separately approved calls.
- Fail closed if policy evaluation, state verification, permission checks, or durable audit writing fails.

See [the threat model](docs/threat-model.md) for the rationale.

## Build and test

```bash
dotnet restore DiscordSteward.sln
dotnet build DiscordSteward.sln --no-restore
dotnet test DiscordSteward.sln --no-build
```

The SDK is pinned by `global.json`. C# Dev Kit is recommended in `.vscode/extensions.json`.

## Run as an MCP server

Build once, then create private configuration from the public examples:

```bash
cp config/profiles/all-tools.example.json config/profiles/home.json
cp .vscode/mcp.example.json .vscode/mcp.json
```

Replace every synthetic ID and example path in the private copies. Both destinations are ignored by Git. Open `.vscode/mcp.json` and start the single server using the inline action or `MCP: List Servers`. Each process remains pinned to one Discord guild; a second guild still requires a second process and profile. See [server profiles](docs/server-profiles.md).

The server starts without a token so `get_steward_capabilities` remains available. Discord-facing tools then return `not_configured`. For an owner-approved live session, store the dedicated bot token outside the repository:

```bash
read -rsp "Discord Steward bot token: " DISCORD_TOKEN
echo
dotnet user-secrets --project src/DiscordSteward/DiscordSteward.csproj set "Discord:BotToken" "$DISCORD_TOKEN"
unset DISCORD_TOKEN
```

Never paste the token into chat, an issue, source control, `mcp.json`, or a command literal that will remain in shell history.

### Startup probe

Use `--probe` to validate process startup, profile parsing, authorization mode, and immutable manifest construction without starting the stdio server or contacting Discord:

```bash
Steward__ProfilePath=config/profiles/unrestricted-autonomy.example.json \
	dotnet run --project src/DiscordSteward/DiscordSteward.csproj -- --probe
```

The command writes one safe JSON record to stdout and exits `0` when configuration is ready. It never returns a bot token or contacts Discord.

The default observer manifest needs only Discord `View Channels`. It does not need `Read Message History`, `View Audit Log`, `Administrator`, or a privileged Gateway intent. Message and member manifests report their additional permissions or privileged-intent requirements. The code does not create a Gateway connection.

`update_channel` additionally requires `Manage Channels`. Never grant `Administrator`.

The implemented tools are the exact 173 names in [the target catalog](docs/tool-surface-redesign.md#target-tool-catalog). Automated tests require the documentation, risk definitions, and MCP registry to match exactly.

Profile schema 1 binds one guild, policy schema 3, a `toolAllowlist` or `"*"`, independent R2/R3/R4/R5 gates, protected resources, denied permissions, and isolated local data paths. The all-tools profile opens every gate and advertises all 173 schemas. Discord permissions are still checked for the invoked tool, so unavailable capabilities fail before a write. Profile, tool, or gate changes require a restart.

The separate `unrestricted-autonomy.example.json` uses policy schema 4 and intentionally removes local safety policy. It is provided for explicit advanced use, not as a setup shortcut.

There is no approval MCP tool, plan/apply tool, or approval command. See [the canonical contract](docs/tool-contract.md) and [coverage inventory](docs/tool-surface-redesign.md#target-tool-catalog).

The local SQLite journal defaults to `data/discord-steward.db`. On Linux it uses the patched system `libsqlite3.so.0` rather than a bundled native SQLite library.

Copy `config/steward-policy.example.json` to the ignored `config/steward-policy.json` only when exact protected resource IDs need local configuration. Names are display data; IDs are the primary protection boundary.

## Repository map

```text
src/DiscordSteward/                 MCP host and control plane
tests/DiscordSteward.Tests/         Pure and adapter-level tests
docs/architecture.md                Runtime and ownership boundaries
docs/threat-model.md                Assets, attacks, mitigations, release gates
docs/tool-contract.md               MCP contract and risk tiers
docs/tool-surface-redesign.md       Coverage inventory and implemented subset
docs/implementation-plan.md         Ordered delivery plan
docs/phase-1-operations.md           Phase 1 setup, permissions, limits, smoke plan
docs/phase-2-permissions.md          Deterministic permission algorithm and limits
docs/mutation-testing-plan.md        Offline, disposable-guild, and promotion gates
docs/server-profiles.md              Public examples, private profiles, switching, and credentials
docs/publication-checklist.md        Redaction, local config, pre-push, and history-cleanup checks
docs/references.md                  Official and comparative research
config/profiles/*.example.json      Synthetic public profile templates
.vscode/mcp.example.json            Synthetic public VS Code MCP template
scripts/audit-public-repo.sh        History, secret, ID, and private-path audit
**/packages.lock.json               Locked direct and transitive dependency graphs
```

## Design position

Existing community Discord MCP servers are useful capability references, but they generally expose broad mutation surfaces and depend only on client-side confirmation. The configured MCP host owns operator approval. Steward independently owns exact tool exposure, static policy, state preconditions, at-most-once execution, verification, and durable evidence. Neither boundary substitutes for the other.

## License

Licensed under the [Apache License 2.0](LICENSE).