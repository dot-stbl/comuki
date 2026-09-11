# Git hooks

Repo-level git hooks, versioned here and installed per clone. Two of them:

| Hook | Runs | Does |
|------|------|------|
| `pre-commit` | before the message editor opens | `dotnet format whitespace` on staged `.cs` files |
| `commit-msg` | after the message is written | strips AI attribution, enforces the `[hybrid]` subject format |

## Install (one-time per clone)

```bash
./scripts/install-hooks.sh
```

Sets `core.hooksPath` to this directory (repo-relative, so linked worktrees
under `.agents/worktree/` each use their own copy) and marks every hook
executable. Adding a new hook file here is the whole install step — the
installer loops, it does not name hooks one by one.

## `pre-commit` — dotnet format whitespace

1. Collects staged `.cs` files
2. Skips if no C# files staged (e.g. markdown-only commit)
3. Skips if `dotnet` CLI is not in PATH (warns, doesn't fail)
4. Runs `dotnet format whitespace comuki.slnx --verify-no-changes --include <staged.cs>`
5. Fails the commit if formatting drift is detected

**Only whitespace** — not style rules, not analyzer fixes. Those are CI-only
(`process/build-verification.md`).

## `commit-msg` — commit hygiene gate

Delegates everything to `scripts/commit-lint.mjs --file "$1"` (plain node ESM,
zero dependencies). Skips with a warning if `node` is not in PATH — a broken
gate must never brick a commit.

**Strips, does not reject** — AI authorship bylines are rewritten out of the
message in place, with each removed line echoed to stderr:

- `Co-Authored-By:` naming claude / anthropic / chatgpt / openai / gpt- /
  codex / copilot / cursor / gemini / opencode / devin / aider / windsurf,
  or with such a domain in the email
- `🤖 Generated with …` / `Generated with [Claude Code](…)`, whole line or
  as a fragment appended to a real line
- `Co-authored by <model>`, `Written with <model>`, `Built by <model>`, …
- `Assisted-By:`, `AI-generated`, `AI-assisted`, `AI-authored`
- inline `noreply@anthropic.com`

An attribution trailer never costs anyone their commit. See
`.agents/rules/process/no-ai-attribution.md`.

**Rejects** a malformed subject — only the author can fix that one:

```
[hybrid] <type>(<scope>)!: <description>
```

- types: `feat fix refactor docs test perf build ci chore style revert`
- scope optional, lowercase `[a-z0-9][a-z0-9._/-]*`
- description: imperative, starts lowercase, no trailing `.`
- subject ≤ 100 characters
- `Merge …` / `Revert …` / `fixup!` / `squash!` subjects git writes itself are exempt
- `#` comment lines and the `--verbose` diff below the scissors line are ignored

See `.agents/rules/process/commit-format.md`.

### Checking without committing

```bash
# lint a message you have in hand
echo '[hybrid] feat(api): add the thing' | node scripts/commit-lint.mjs --stdin

# audit a range of existing commits (attribution is an error here — you
# cannot rewrite someone's pushed commit)
node scripts/commit-lint.mjs --range master..HEAD

# the unit tests
node --test scripts/commit-lint.test.mjs
```

## Bypass

```bash
git commit --no-verify
```

Skips both hooks. Use sparingly — format drift should be fixed in the same
commit, and a bad subject is one `git commit --amend` away.

## Uninstall

```bash
git config --unset core.hooksPath
```

## Requirements

- `dotnet` CLI in PATH for `pre-commit` (skip is automatic otherwise)
- `node` in PATH for `commit-msg` (skip is automatic otherwise)
- Repository root contains `comuki.slnx`
- Git Bash on Windows (any version of Windows with Git for Windows installed)
