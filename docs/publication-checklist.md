# Public repository checklist

Use this checklist before making the repository public or pushing a release branch.

## Keep private

Do not commit:

- Discord bot or user tokens, OAuth credentials, webhook URLs, or vault keys
- real guild, application, channel, category, role, member, message, invite, webhook, operation, or baseline IDs
- `.vscode/mcp.json`
- real files under `config/profiles/*.json`
- `config/steward-policy.json`
- `data/`, SQLite journals, asset inboxes, asset vaults, or webhook-secret vaults
- live validation logs, screenshots, exports, audit-log excerpts, or private Discord content
- local machine paths, usernames, personal email addresses, or shell history

Synthetic IDs in tests and `*.example.json` files are intentional. Keep them obviously reusable and unrelated to a live Discord deployment.

## Keep local configuration

Start from the public examples:

```bash
cp config/profiles/all-tools.example.json config/profiles/home.json
cp .vscode/mcp.example.json .vscode/mcp.json
```

Real profile and MCP files remain in the repository working directory but are ignored by Git. This is convenient, not secure storage. Keep a separate encrypted backup if losing those files would be costly. Tokens belong in .NET user secrets or another external secret provider, never in either JSON file.

Confirm the local files are ignored:

```bash
git check-ignore -v .vscode/mcp.json config/profiles/home.json
git status --short --ignored
```

## Audit the public tree

Review exactly what Git will publish:

```bash
git ls-files
git diff --cached --check
git grep -n -I -E '[0-9]{17,20}' -- .
git grep -n -I -E 'BotToken|DiscordTokens|Authorization|webhook' -- .
git log --format=fuller --decorate --stat
git remote -v
```

Run the automated audit with a verified Gitleaks installation:

```bash
GITLEAKS_BIN=/path/to/gitleaks scripts/audit-public-repo.sh
```

Classify every match. Source references to credential APIs and synthetic test fixtures are expected; credential values and deployment identifiers are not.

Also check the commit author email. Use a public or provider-generated no-reply address if the configured Git email is private.

## If sensitive data reached Git

1. Rotate or revoke the credential first. History rewriting does not invalidate a copied secret.
2. Remove the value from the working tree and add the path or file class to `.gitignore` when appropriate.
3. Rewrite every affected branch and tag with a history-rewriting tool such as `git filter-repo`.
4. Force-push rewritten refs only after coordinating with other contributors.
5. Delete or replace affected releases, pull-request refs, caches, artifacts, mirrors, and forks where possible.
6. Ask all collaborators to reclone. Old clones still contain the removed objects.

If sensitive history was ever pushed, assume it was copied even when the remote repository was private.

## Publication policy

- Enable GitHub secret scanning, push protection, Dependabot alerts, and private vulnerability reporting when available.
- Keep branch protection and required CI checks enabled.
- Preserve the Apache-2.0 `LICENSE` file and package license expression in releases.
- Run the full offline suite with every live authorization variable absent.