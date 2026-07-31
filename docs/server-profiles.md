# Server profiles

Discord Steward uses one immutable profile per Discord guild. A profile atomically selects:

- the exact guild ID
- the tool allowlist and risk gates
- protected resource IDs and denied permissions
- the SQLite journal, asset vault, and webhook-secret vault paths
- an optional named bot-token secret

This prevents a process from combining one guild target with another guild's policy or durable data. Profile changes require an MCP server restart.

## Public examples and private profiles

Files ending in `.example.json` are synthetic, public examples. Their Discord snowflakes are placeholders and must not be used as live configuration.

Real files under `config/profiles/*.json` are ignored by Git, except for the examples. The recommended setup copies the all-tools example:

```bash
cp config/profiles/all-tools.example.json config/profiles/home.json
cp .vscode/mcp.example.json .vscode/mcp.json
```

Then update every placeholder in the private copies:

1. `Steward:ProfileName`
2. `Discord:GuildId`
3. all `data/servers/<server>` paths
4. protected resource IDs
5. the tool allowlist and risk gates; keep `["*"]` and all gates open for the one-server model
6. denied permissions
7. optional `Discord:BotTokenSecretName`
8. `.vscode/mcp.json` server names and profile paths

The included examples demonstrate the recommended profile plus optional restricted alternatives:

| Example | Purpose | Write state |
| --- | --- | --- |
| `all-tools.example.json` | Recommended interactive server with the complete 173-tool catalog | All risk gates enabled; every call still requires host approval and Steward preflight |
| `observer.example.json` | Configuration inspection, linting, analysis, and snapshots | No Discord writes exposed |
| `channel-operator.example.json` | Exact channel reads plus journaled `update_channel` | Reversible writes enabled |
| `community-operator.example.json` | Community layout, guild safety settings, messages, and exact member-role assignment | All risk gates enabled; narrow exact tool list |
| `disposable-operator.example.json` | Disposable integration and mutation testing | Reversible writes enabled; broader risk gates closed |
| `unrestricted-autonomy.example.json` | Dangerous autonomous operation with no local protections or approval assumption | Complete catalog, all gates forced open, protected and denied policy removed |

Use one process and profile for a guild. Profiles for different guilds must use different data directories and separate MCP server processes because guild identity is a trusted process boundary. Do not run two write-capable profiles for the same guild concurrently.

`toolAllowlist: ["*"]` expands to every implemented tool at startup. It cannot be combined with exact names. All four risk gates must be open to register every risk class. The resulting manifest is immutable until restart.

Advertising a tool does not bypass its controls. Before a write, Steward still checks the exact tool, target protection, current Discord permissions, privileged-intent requirements where applicable, hierarchy, state digest, asset digest, and durable journal. A tool whose Discord capability is unavailable fails preflight without affecting unrelated tools.

## Dangerous unrestricted mode

The normal profiles use policy schema 3 and `InteractiveApproval`. `unrestricted-autonomy.example.json` uses schema 4 with `authorization.mode: UnrestrictedAutonomy`. Selecting it deliberately changes the trust model:

- the complete catalog is mandatory and every startup risk gate is forced open
- protected resource IDs and name prefixes are discarded
- denied permissions are discarded
- an Administrator bot and Administrator role grants are allowed
- configured deletion blockers no longer prevent the approved exact cascade
- no operator approval is assumed or enforced

Guild binding, exact typed inputs, Discord permission checks, state preconditions, journaling, one-write execution, and verification remain active. Use a dedicated least-privileged bot and isolated guild. Do not use unrestricted mode as a workaround for host approval prompts or policy failures.

## Switching in VS Code

VS Code reads `.vscode/mcp.json`. That file is ignored because server names and profile paths can reveal private deployment details. Start from `.vscode/mcp.example.json`, then keep the copy local.

Start the single server from `MCP: List Servers`. There is no per-request profile switch and no need to restart when moving between tool families.

Before approving a call, verify `get_steward_capabilities` reports the expected `profile`, `guildId`, registered tools, risk gates, and required Discord permissions. Never infer the active target from a display name.

## Credentials

Profiles and MCP configuration must not contain bot tokens. Store tokens in .NET user secrets or another external secret provider.

For one shared bot identity:

```bash
read -rsp "Discord Steward bot token: " DISCORD_TOKEN
echo
dotnet user-secrets --project src/DiscordSteward/DiscordSteward.csproj \
  set "Discord:BotToken" "$DISCORD_TOKEN"
unset DISCORD_TOKEN
```

For separate bot identities, add a non-secret alias to a private profile:

```json
"Discord": {
  "GuildId": "YOUR_GUILD_ID",
  "BotTokenSecretName": "Home"
}
```

Store the corresponding token outside the repository:

```bash
read -rsp "Discord Steward bot token: " DISCORD_TOKEN
echo
dotnet user-secrets --project src/DiscordSteward/DiscordSteward.csproj \
  set "DiscordTokens:Home" "$DISCORD_TOKEN"
unset DISCORD_TOKEN
```

`Discord:BotToken` takes precedence for backward compatibility. Otherwise Steward resolves `DiscordTokens:<BotTokenSecretName>`. Alias names are restricted to 1 to 64 ASCII letters, digits, periods, underscores, or hyphens.

## Publication checks

Ignored files remain on disk but are not encrypted, backed up, or protected from other local users. Keep a separate secure backup of private profiles and rotate a bot token immediately if it ever enters Git history, terminal output shared with others, an issue, or chat.

Before publishing or pushing, verify the private files are ignored:

```bash
git check-ignore -v .vscode/mcp.json config/profiles/home.json
git status --short --ignored
git grep -n -I -E '[0-9]{17,20}' -- . ':!tests/**'
```

Review every remaining snowflake. Synthetic fixture IDs are acceptable in tests and examples; real guild, channel, role, member, application, operation, baseline, invite, and webhook identifiers are deployment data and should stay private.

The legacy `config/steward-policy.json` path is also ignored and remains supported when no profile is selected. Named profiles are the preferred multi-server deployment model.