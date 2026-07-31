# Phase 1 operations

## Implemented boundary

Phase 1 is a read-only Discord REST observer for exactly one configured guild. It exposes normalized guild, role, channel, category, and permission-overwrite data plus deterministic lint findings through local MCP `stdio`.

It has no Discord mutation path, Gateway client, privileged intent, HTTP listener, LLM dependency, message read, DM read, audit-log read, or generic Discord API tool.

Network-free tests cover everything through MCP discovery. Optional live read-only validation must use an owner-approved disposable guild and a private validation record.

## Dedicated bot and permissions

The owner must create or select a dedicated private Discord application before live use. Do not reuse another bot, application, or token.

Configure the Discord application as follows:

1. Create an application named `DiscSteward`. Discord application names cannot contain the reserved `Discord` brand name.
2. Add a bot user and disable `Public Bot`.
3. On the `Installation` tab, set the default install or authorization link to `None`. Private applications cannot retain a default authorization link.
4. Save the private application settings. Do not make the application public to bypass this validation.
5. On `OAuth2` > `URL Generator`, select only the `bot` scope and the `View Channels` bot permission.
6. Open the generated URL while signed in as the application owner or a team member, then install it to a disposable private guild.
7. Keep its role below owner and moderator roles.
8. Leave every privileged Gateway intent disabled.

Setting the default install link to `None` does not prevent the owner from using the manually generated OAuth2 guild-install URL.

Phase 1 does not need:

- `Administrator`
- `Read Message History`
- `View Audit Log`
- `ManageGuild`, `ManageRoles`, `ManageChannels`, or `ManageWebhooks`
- Message Content, Guild Members, or Presence intents
- The `applications.commands` scope

The optional audit-event tool was deliberately not implemented.

## Local configuration

Build first:

```bash
dotnet restore DiscordSteward.sln
dotnet build DiscordSteward.sln --no-restore
dotnet test DiscordSteward.sln --no-build
```

The guild ID is trusted process configuration. VS Code prompts for it through `.vscode/mcp.json`; no MCP tool accepts or overrides it.

Set the token directly in the owner's terminal through .NET user-secrets:

```bash
read -rsp "Discord Steward bot token: " DISCORD_TOKEN
echo
dotnet user-secrets --project src/DiscordSteward/DiscordSteward.csproj set "Discord:BotToken" "$DISCORD_TOKEN"
unset DISCORD_TOKEN
```

Do not put the token in source, JSON, `.env`, shell history, an issue, a screenshot, or chat.

Optional policy overrides use the existing top-level JSON shape:

```bash
cp config/steward-policy.example.json config/steward-policy.json
```

`config/steward-policy.json` is ignored by Git. Populate `protectedResourceIds` with exact role or channel snowflakes. The default protected name prefixes remain a secondary signal, not identity.

## Readiness behavior

`get_steward_capabilities` is local and never contacts Discord. Its readiness state is:

- `unconfigured`: guild ID or token is absent or malformed.
- `not_checked`: both values exist, but no Discord read has been attempted.
- `ready`: bot identity and configured-guild access were verified and a read succeeded.
- `degraded`: the latest Discord read failed with a safe error code.

The first R1 tool call lazily creates `DiscordRestClient`, calls `LoginAsync(TokenType.Bot, ...)`, and verifies the configured guild. The raw token is held only by the secret-owning configuration/session path and has a redacted text representation.

## Exact REST surface

For a fresh snapshot, the pinned Discord.Net 3.20.1 client performs only these reads:

1. During `LoginAsync`, Discord.Net reads the current bot user and current application metadata.
2. `DiscordRestClient.GetGuildAsync(configuredGuildId)` reads the configured guild. Roles are included in this response.
3. `RestGuild.GetChannelsAsync()` reads guild channels, categories, and permission overwrites.

Steward does not call message, member-list, DM, presence, voice-state, audit-log, invite, webhook, or mutation endpoints. Every request after login carries the caller cancellation token through `RequestOptions.CancelToken`. Discord.Net owns normal rate-limit handling; terminal failures are mapped to safe codes without custom retry timing.

## Output and lint behavior

Discord snowflakes are serialized as decimal strings. Collections are sorted by Discord position and then stable ID. Permission names and overwrites are sorted ordinally. Guild and resource names are bounded to 256 UTF-16 code units; descriptions and topics are bounded to 1,024 without splitting a surrogate pair.

All Discord text is untrusted display data. Names are never used as identity or instructions.

The linter reports evidence only. It never changes Discord. Initial checks cover:

- Roles with `Administrator`.
- Managed or protected roles with denied permissions.
- Permissions denied by Steward policy when they are granted to `@everyone`.
- Elevated unprotected roles, without claiming Steward can manage them.
- Duplicate case-insensitive role and channel names.
- Member-specific permission overwrites.
- Sensitive permissions explicitly allowed to `@everyone`.
- Child overwrites that differ from the parent category.
- Resources protected only by a name prefix instead of an exact ID.

## Verified Discord.Net limitations

- `LoginAsync` and `LogoutAsync` do not accept a cancellation token. Steward waits with the caller token and disposes a provisional client on cancellation. Guild and channel requests have native request cancellation.
- The guild-channels response does not include active threads. Phase 1 does not call the separate active-threads endpoint.
- Category synchronization is inferred by comparing canonical overwrite entries; no separate sync flag is trusted.
- The snapshot does not fetch guild members or the bot member's role hierarchy. Permission explanations therefore report live manageability as unknown.
- Role-only permission calculation is implemented in Phase 2; member-specific effective permissions remain unavailable.
- Discord.Net controls automatic rate-limit waiting. Steward maps a terminal 429 or `RateLimitedException` to `rate_limited` and does not add retry loops.

## Proposed disposable-guild smoke test

This procedure requires explicit owner approval after reviewing the bot identity, role position, and `View Channels` grant. Use a disposable guild, and keep its identifiers and validation record outside the public repository.

1. Create a disposable guild fixture with one category, two text channels, duplicate test role names, and a harmless member overwrite.
2. Start Steward with the disposable guild ID and dedicated private bot secret.
3. Call `get_steward_capabilities`; confirm `not_checked`, the configured guild ID, and token presence only.
4. Call `get_guild_configuration_snapshot`; confirm readiness becomes `ready` and only the disposable guild appears.
5. Call `get_role` and `get_channel` with exact fixture IDs.
6. Call `lint_guild_configuration`; compare findings with the fixture.
7. Call `explain_role_permissions` with exact fixture role and channel IDs.
8. Confirm no message, audit-log, member-list, or mutation request occurred.
9. Stop Steward and remove the bot from the disposable guild if it is not needed.

The exact live read set is current bot/application identity, configured guild, and configured guild channels. Any request outside that set stops the smoke test for review.

## Expected live result

An authorized read-only smoke test should demonstrate all of the following without committing the observed IDs:

- Readiness changes from `not_checked` to `ready`.
- The normalized snapshot contains only resources from the configured disposable guild.
- Exact `get_role` and `get_channel` calls succeed using IDs returned by the snapshot.
- Local policy marks configured resources protected by exact ID and optional name prefix.
- Lint and permission explanations return deterministic findings for the prepared fixture.
- No authentication, permission, guild mismatch, rate-limit, or availability error occurs.
- No Discord mutation, member list, direct message, presence, voice-state, audit-log, invite, or webhook endpoint is called.

Store guild, role, channel, member, application, request, and baseline IDs in an ignored local validation log if they are needed for later cleanup or reconciliation.

## Safe errors

Read tools return a schema-versioned envelope with an operation ID. Errors use a safe explanation, stable code, and a correlation ID equal to the operation ID.

Phase 1 maps:

- `not_configured`
- `discord_authentication_failed`
- `discord_permission_denied`
- `guild_mismatch`
- `resource_not_found`
- `ambiguous_name`
- `rate_limited`
- `discord_unavailable`

Raw Discord exceptions, request headers, routes, stack traces, and credentials are never returned through MCP.