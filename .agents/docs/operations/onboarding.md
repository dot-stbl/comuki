# Onboarding — humans and agents working on comuki.orchestrator

> Operational onboarding. If you are an AI agent, also read
> [`.agents/RULES-BOOTSTRAP.md`](../RULES-BOOTSTRAP.md) — the
> machine-checkable contract that backs every step below.

This doc is for two audiences:

1. **A new engineer** joining the project for the first time.
2. **An AI agent** landing in a fresh session and trying to orient.

Both audiences follow the same ritual because the rule corpus, the
build gate, and the commit format are identical for humans and bots.
The only difference is **where** the audit happens: humans read the
rules themselves; bots have `[VerifyRuleAwareness]` /
`[SelfAuditReport]` to remind them.

---

## 0. Five-minute orientation

| Need | Read |
|---|---|
| What this repo is | [`AGENTS.md`](../../AGENTS.md) at the repo root |
| Where we are right now | [`.agents/STATE.md`](../STATE.md) |
| What we're working toward | [`.agents/ROADMAP.md`](../ROADMAP.md) |
| Architecture / why it is shaped this way | [`.agents/docs/architecture/`](.) |
| Code style / DI / testing | [`.agents/rules/coding/`](../../.agents/rules/coding) (project-local) and `~/.agents/rules/csharp/` (user-global) |
| Build / commit / process | [`.agents/rules/process/`](../../.agents/rules/process) |

`AGENTS.md` is **the only** pointer — do not duplicate its content here.
If you find a copy of "Critical Non-Obvious Patterns" anywhere except
`AGENTS.md`, it is stale; update `AGENTS.md` and delete the copy.

## 1. Build gate (read this first — you will hit it immediately)

```
dotnet build comuki.slnx -c Debug
```

This single command runs three things:

1. Compilation.
2. Analyzer set (`AnalysisMode=None` + the rules in `.editorconfig`;
   `TreatWarningsAsErrors=true` so any warning fails the build).
3. Format check (`VerifyFormatOnBuild` target — `dotnet format --verify`
   with `--severity hidden`).

Exit ≠ 0 means **not done**. Do not commit until the command exits 0.

The build now also prints a **rule-loading reminder** at the start and
writes a **self-audit report** at the end. See
[`.agents/RULES-BOOTSTRAP.md`](../RULES-BOOTSTRAP.md) for what the
banner means and how to act on it.

## 2. Commit format (the second thing that will trip you)

```
[hybrid] <type>(<scope>): <description>
```

| Part | Rule |
|---|---|
| `[hybrid]` | literal prefix, identifies this repo's commits |
| `<type>` | `feat` / `fix` / `refactor` / `docs` / `test` / `perf` / `build` / `ci` / `chore` / `style` |
| `<scope>` | optional; one of `bootstrap`, `orchestration`, `proxy`, `mcp`, `translator`, `database`, `routing`, `rules`, `artifacts`, `knowledge`, `agents`, `agent-core`, `worker-sdk`, `dev-sdk`, `dashboard`, `deploy`, `docker`, `docs`, `rules`, `roadmap`, `state`, `ci`, `deps` |
| `<description>` | imperative, lowercase, ≤72 chars, no period |

Full reference: [`.agents/rules/process/commit-format.md`](../../.agents/rules/process/commit-format.md).

Two anti-patterns that bit this repo in the past:

- `[stbl](feat/...)` — old prefix from `.stbl` monorepo. **Banned.**
- `[app](...)` — `[hybrid]` is the only valid prefix here. **Banned.**

The git history before this rule took effect still has those old
prefixes; do not rewrite history to match.

## 3. Scripts — `bun` only, Python is banned

This repo runs `bun@1.3.10` for every script. **Python is forbidden**
in any form (no `.py`, no `pip`, no `python -c`). When in doubt, write
the script in TypeScript and run it via `bun run scripts/<name>.ts`.

Full reasoning: [`.agents/rules/process/allowed-scripts.md`](../../.agents/rules/process/allowed-scripts.md).

Two scripts you will run often:

```bash
# Backend unit tests (xUnit v3 — VSTest can't discover it)
dotnet run --project tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine

# Frontend build + test cycle
cd dashboard && bun run typecheck && bun run lint && bun run test
cd dashboard && bun run build
```

## 4. Ports

Ports **must** come from the pool `17000–17200`. The dashboard dev
server uses **17173** (`strictPort: true` in `vite.config.ts`). If you
need a new port, check [`.agents/rules/process/ports.md`](../../.agents/rules/process/ports.md)
for the reservation table; do not invent ports.

## 5. The agent ritual (full version)

If you are an AI agent, follow this every session:

```
1. Read .agents/RULES-BOOTSTRAP.md (this file's machine sibling).
2. Read ~/.agents/rules/csharp/*.md (BE) OR ~/.agents/rules/typescript/*.md (FE).
3. Read .agents/rules/coding/*.md and .agents/rules/process/*.md.
4. Read AGENTS.md and .agents/STATE.md.
5. Confirm to the user: "I have read N rules. Working."
6. Make changes.
7. Before commit: dotnet build comuki.slnx -c Debug (exit 0).
8. Open audit-data/last-commit-audit.md and audit-data/last-commit-audit-fe.md.
   Fix every violation flagged.
9. Report: "Read N rules. Audited M commits. Fixed K violations."
```

The build-time banner in step 7 is not a courtesy — it is the prompt to
re-read in step 8.

## 6. Common mistakes (worth reading once)

- **`dotnet test` for xUnit v3** — fails silently because VSTest can't
  discover xUnit v3. Use `dotnet run --project <csproj>` instead.
- **Long-lived dev servers (`bun run dev`, `dotnet watch`, playwright)**
  — kills the agent runtime. Build + test only; user runs dev.
- **Hand-edited EF migrations** — `dotnet ef migrations add/remove` is
  the only sanctioned way. See
  [`.agents/rules/csharp/ef-migrations.md`](../../.agents/rules/csharp/ef-migrations.md).
- **Random ports** — use the 17000–17200 pool.
- **Comuki palette changes** — tokens are in `.agents/docs/design-system/`;
  see Comuki Design System doc.
- **`feat/` without `feat/` prefix** in commit messages — `[hybrid]`
  prefix is required; the feature-path-first style is from the old
  `.stbl` era.

## 7. Where to ask

- Architecture / scope questions → ask in chat with a link to
  `.agents/STATE.md` and the relevant ADR.
- Process questions → `.agents/rules/process/`.
- Code style questions → `.agents/rules/coding/` (project-local) +
  `~/.agents/rules/csharp/` (user-global).
- "Why is this done this way?" → look for an ADR in
  `.agents/docs/architecture/`.

## 8. After onboarding

Once you have read this doc and the four files in step 5, you are
operationally unblocked. **The rule corpus is not optional** — the
build banner will remind you every time, and skipping the audit is a
load-gate skip.

Welcome to comuki.orchestrator.
