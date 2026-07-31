# References

Verified July 25, 2026. Prefer these primary sources over copied examples.

## Model Context Protocol

- [MCP documentation](https://modelcontextprotocol.io/)
- [MCP specification, 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25)
- [MCP transports](https://modelcontextprotocol.io/docs/concepts/transports)
- [MCP security best practices](https://modelcontextprotocol.io/specification/2025-11-25/basic/security_best_practices)
- [Official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP C# SDK documentation](https://csharp.sdk.modelcontextprotocol.io/)
- [Official C# quickstart server sample](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/QuickstartWeatherServer)
- [`ModelContextProtocol` 1.4.1 on NuGet](https://www.nuget.org/packages/ModelContextProtocol/1.4.1)

The C# SDK has a 2.0 release candidate, but this repository pins stable 1.4.1. Do not upgrade as incidental work.

## Discord

- [Discord Developer Platform](https://docs.discord.com/developers/intro)
- [OAuth2 and bot users](https://docs.discord.com/developers/topics/oauth2#bot-users)
- [Permissions and hierarchy](https://docs.discord.com/developers/topics/permissions)
- [Rate limits](https://docs.discord.com/developers/topics/rate-limits)
- [Gateway intents](https://docs.discord.com/developers/events/gateway#gateway-intents)
- [Guild audit log](https://docs.discord.com/developers/resources/audit-log)
- [Discord Developer Policy](https://support-dev.discord.com/hc/en-us/articles/8563934450327-Discord-Developer-Policy)
- [Discord Developer Terms](https://support-dev.discord.com/hc/en-us/articles/8562894815383-Discord-Developer-Terms-of-Service)
- [Discord community libraries](https://docs.discord.com/developers/developer-tools/community-resources)
- [Discord OpenAPI preview](https://github.com/discord/discord-api-spec)

Discord does not maintain an official general bot SDK. It lists Discord.Net as a maintained community library. User-account automation and self-bots are prohibited.

## Discord.Net

- [Discord.Net documentation](https://docs.discordnet.dev/)
- [Discord.Net repository](https://github.com/discord-net/Discord.Net)
- [`Discord.Net.Rest` 3.20.1 on NuGet](https://www.nuget.org/packages/Discord.Net.Rest/3.20.1)

Use the REST package unless a future feature has a demonstrated need for Gateway events.

## VS Code MCP development

- [Add and manage MCP servers](https://code.visualstudio.com/docs/agent-customization/mcp-servers)
- [MCP configuration reference](https://code.visualstudio.com/docs/agents/reference/mcp-configuration)
- [Custom instructions](https://code.visualstudio.com/docs/agent-customization/custom-instructions)

The workspace uses `.vscode/mcp.json`. In `InteractiveApproval`, the MCP host must show every complete call for operator approval before invocation. Do not use interactive write profiles when sandboxing or client configuration auto-approves tools. Standard MCP does not attest approval to Steward; a host-signed call-attestation extension would be required to enforce that boundary server-side. `UnrestrictedAutonomy` is an explicit alternative trust model and does not claim approval.

## Community implementations reviewed

These are comparative references, not dependencies:

- [SaseQ/discord-mcp](https://github.com/SaseQ/discord-mcp): broad Java/JDA capability coverage. Its open [security review issue 34](https://github.com/SaseQ/discord-mcp/issues/34) documents unauthenticated HTTP access and SSRF risk. Destructive tools execute directly.
- [barryyip0625/mcp-discord](https://github.com/barryyip0625/mcp-discord): active TypeScript project with tests and releases. Its HTTP server binds broadly without an application authorization layer, tools mutate directly, and [issue 41](https://github.com/barryyip0625/mcp-discord/issues/41) reports multi-client session failure.
- [v-3/discordmcp](https://github.com/v-3/discordmcp): small read/send prototype, not an administrative control plane.
- [hanweg/mcp-discord](https://github.com/hanweg/mcp-discord): older Python implementation with a limited management surface.

Useful ideas may be reimplemented from first principles after license and security review. Do not copy their credential, transport, or direct-mutation patterns.