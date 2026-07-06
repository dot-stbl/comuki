---
description: Pre-commit hook runs dotnet format whitespace on staged C# files. Install once per clone, bypass via --no-verify.
globs: ["scripts/hooks/**", "scripts/install-hooks.sh"]
always: true
---

# Pre-commit hook — dotnet format whitespace

Repo-level git hook (`scripts/hooks/pre-commit`) versioned and installed
per-clone. Runs **`dotnet format whitespace`** on staged `.cs` files
before each commit. Bypass with `git commit --no-verify`.

## Install (once per clone)

```bash
./scripts/install-hooks.sh
```

Sets `core.hooksPath = scripts/hooks` and chmod +x on the hook.

## What it checks

| Check | Where |
|-------|-------|
| Whitespace (indent, line endings, blank lines) per `.editorconfig` | **Pre-commit hook** |
| Full `dotnet format` (whitespace + style + analyzers) | **CI** — `process/build-verification.md` |
| Build with extended analyzer rules | **CI** — `process/build-verification.md` |

Pre-commit is intentionally narrow (whitespace only) to keep commits fast
and avoid blocking on style/analyzer findings mid-refactor. CI catches the rest.

## Why only whitespace

- Whitespace — purely cosmetic, no semantic risk, ~1s per commit
- Style rules — can have false positives, need review
- Analyzer fixes — can change semantics, too aggressive for pre-commit

## Skip

```bash
git commit --no-verify
```

Use when:
- The format failure is a pre-existing issue unrelated to your change
- You're in the middle of a refactor and will reformat in the next commit
- Hotfix that CI will catch anyway

## Requirements

- `dotnet` CLI in PATH (otherwise hook skips silently with a warning)
- `comuki.slnx` at repo root
- Git Bash on Windows (any Git for Windows installation)

## Related

- `scripts/hooks/pre-commit` — the actual hook
- `scripts/hooks/README.md` — installation & bypass
- `scripts/install-hooks.sh` — one-line installer
- `.agents/rules/process/build-verification.md` — CI side of the same check
- `.agents/rules/coding/ANALYZERS.md` — analyzer packages (Roslynator, Meziantou)
