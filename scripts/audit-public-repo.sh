#!/usr/bin/env bash
set -euo pipefail

repository_root="$(git rev-parse --show-toplevel)"
cd "$repository_root"

private_files="$({
  git ls-files '.vscode/mcp.json' 'config/steward-policy.json' 'data/**' 'docs/private/**'
  git ls-files 'config/profiles/*.json' | grep -v '\.example\.json$' || true
} | sed '/^$/d')"
if [[ -n "$private_files" ]]; then
  printf 'Private deployment files are tracked:\n%s\n' "$private_files" >&2
  exit 1
fi

allowed_snowflakes='^(100000000000000001|100000000000000002|100000000000000003|100000000000000004)$'
unexpected_snowflakes="$(
  git grep --cached -h -o -E '\b[0-9]{17,20}\b' -- . 2>/dev/null |
    sort -u |
    grep -Ev "$allowed_snowflakes" || true
)"
if [[ -n "$unexpected_snowflakes" ]]; then
  printf 'Unexpected Discord-like snowflakes are tracked:\n%s\n' "$unexpected_snowflakes" >&2
  exit 1
fi

unexpected_emails="$(
  git grep --cached -h -o -E '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}' -- . 2>/dev/null |
    sort -u |
    grep -Ev '@(example\.com|users\.noreply\.github\.com)$' || true
)"
if [[ -n "$unexpected_emails" ]]; then
  printf 'Unexpected email addresses are tracked:\n%s\n' "$unexpected_emails" >&2
  exit 1
fi

if git grep --cached -n -I -E '/(home|Users)/|[A-Z]:\\|discord\.gg/|discord\.com/(channels|events|api/webhooks)/' -- .; then
  printf 'Personal paths or live Discord links are tracked.\n' >&2
  exit 1
fi

scanner="${GITLEAKS_BIN:-$(command -v gitleaks || true)}"
if [[ -z "$scanner" || ! -x "$scanner" ]]; then
  printf 'Gitleaks is required. Set GITLEAKS_BIN or install gitleaks.\n' >&2
  exit 1
fi

"$scanner" git --config .gitleaks.toml --no-banner --redact .

public_tree="$(mktemp -d "${TMPDIR:-/tmp}/discord-steward-public-XXXXXX")"
trap 'rm -rf "$public_tree"' EXIT
git checkout-index --all --prefix="$public_tree/"
"$scanner" dir --config "$repository_root/.gitleaks.toml" --no-banner --redact "$public_tree"

git diff --check
git diff --cached --check
printf 'Public repository audit passed.\n'
