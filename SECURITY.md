# Security policy

## Supported mode

Only the local `stdio` transport is supported. No remote HTTP deployment is supported.

The recommended trusted local process uses `InteractiveApproval` and exposes all 173 exact tools through one private, guild-bound profile with all risk gates open. Exact-name restricted profiles remain supported. The public example profiles contain only synthetic IDs. Real profiles, MCP configuration, operation journals, assets, and secret vaults must remain ignored local files.

Operator approval belongs to the MCP host before every invocation. Standard MCP does not prove that a host displayed approval, so the all-tools profile must never run through an unattended or auto-approving host. MCP annotations are descriptive risk metadata, not authorization. See [the tool contract](docs/tool-contract.md).

Never grant the Steward bot `Administrator`. A full-catalog profile may advertise tools whose individual Discord permissions or privileged intents are absent; those tools fail their own preflight. Broad non-Administrator grants increase the impact of token theft outside Steward. Follow [the mutation testing plan](docs/mutation-testing-plan.md) before enabling writes for a non-disposable guild.

## Unrestricted autonomy

`UnrestrictedAutonomy` is a dangerous explicit schema-4 mode. It does not require or attest operator approval. It disables protected resources, denied permissions, Administrator refusal, deletion blockers, and startup risk gates. A compromised autonomous caller can exercise every tool allowed by the Discord credential.

If used at all, isolate it to a dedicated bot and guild, grant only required Discord permissions, protect the host account and local journal, and monitor Discord audit logs. Do not expose the stdio process through a remote or multi-tenant transport. The remaining guild binding, typed schemas, Discord permission checks, preconditions, journaling, one-write execution, and verification reduce mistakes but do not replace authorization.

## Reporting

Report suspected vulnerabilities through GitHub private vulnerability reporting when available, or contact the repository owner privately. Do not include bot tokens, user tokens, webhook URLs, private Discord content, server identifiers, or other credentials in a public issue, commit, screenshot, or chat transcript.

Include:

- A concise description of the behavior.
- The affected commit and package versions.
- Reproduction steps using fake credentials or a disposable guild.
- Expected and actual policy decisions.
- Any evidence of unauthorized access, with sensitive values redacted.

## Credential response

If the Discord bot token may have been exposed:

1. Reset it immediately in the Discord Developer Portal.
2. Remove the old value from .NET user-secrets and any process environment.
3. Store the replacement outside source control.
4. Review Discord audit logs and the Steward execution store.
5. Stop the MCP server until the exposure path is understood.

Never automate a normal Discord user account. User tokens and self-bots are outside the design and prohibited by Discord's developer rules.