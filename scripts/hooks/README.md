# Git hooks

Repo-level pre-commit hook that runs `dotnet format whitespace` on staged
C# files. Versioned, installed per clone.

## Install (one-time per clone)

```bash
./scripts/install-hooks.sh
```

Sets `core.hooksPath = scripts/hooks` and marks `pre-commit` executable.

## What the hook does

1. Collects staged `.cs` files
2. Skips if no C# files staged (e.g. markdown-only commit)
3. Skips if `dotnet` CLI is not in PATH (warns, doesn't fail)
4. Runs `dotnet format whitespace comuki.slnx --verify-no-changes --include <staged.cs>`
5. Fails the commit if formatting drift is detected

**Only whitespace** — not style rules, not analyzer fixes. Those are CI-only
(`process/build-verification.md`).

## Bypass

```bash
git commit --no-verify
```

Use sparingly. Format drift should be fixed in the same commit.

## Uninstall

```bash
git config --unset core.hooksPath
```

## Requirements

- `dotnet` CLI in PATH (skip is automatic otherwise)
- Repository root contains `comuki.slnx`
- Git Bash on Windows (any version of Windows with Git for Windows installed)
