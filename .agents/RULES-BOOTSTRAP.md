---
description: Agent bootstrap protocol — read rules before code change, re-read + self-audit before declaring done. Machine-checked via VerifyRuleAwareness / SelfAuditReport / rule-audit.ts.
globs: ["**/*.cs", "**/*.ts", "**/*.tsx", "**/*.csproj", "**/*.slnx", "**/package.json"]
always: true
---

# RULES-BOOTSTRAP — agent onboarding ritual

This file is the **operational contract** for any agent (human or AI) that
edits code in `comuki.orchestrator`. It is intentionally short and is
enforced by three machine-checkable artefacts:

1. `[VerifyRuleAwareness]` MSBuild target (BE) — prints a rule-scope banner
   + per-scope file list at the start of every `dotnet build comuki.slnx`.
2. `[SelfAuditReport]` MSBuild target (BE) — runs the worker-audit regex
   sweep against `HEAD~1..HEAD`'s changed .cs files and writes
   `audit-data/last-commit-audit.md` at the end of every build.
3. `dashboard/scripts/rule-audit.ts` (FE) — Bun mirror of (1) + (2);
   prints banner + writes `audit-data/last-commit-audit-fe.md`. Wired into
   `predev` and `prebuild` npm hooks.

All three are **WARNING ONLY** — they print, they write a report, but they
do not fail the build. The agent is expected to act on the reminder.

---

## Before ANY code change

1. **Read the rule corpus** in this order:
   - `~/.agents/rules/csharp/*.md` (BE) or `~/.agents/rules/typescript/*.md` (FE)
     — user-global corpus, loaded into context by your harness.
   - `.agents/rules/coding/*.md` (this repo, project-local) — rules that
     override or extend the user-global ones (e.g. `[hybrid]` commit
     prefix, Python-is-banned, Comuki palette, port pool 17000–17200).
   - `.agents/rules/process/*.md` (this repo) — process / build / commit.
   - `AGENTS.md` at the repo root — the single source of truth for
     orientation; do not duplicate content from `.agents/rules/` here.
2. **Confirm the load** to the user (one short line):
   > I have read N rule files (M csharp, K process, ...). Working on `<task>`.
3. **If a hook fires the reminder, treat it as a load gate.** The build-time
   banner is not a courtesy — it is the prompt to re-read. Skipping it
   means you skipped step 1.

## Before declaring work done

1. **Re-read every rule** from the top. Do not skim — the rules change
   session-to-session as the codebase evolves. The build banner is your
   reminder; honour it.
2. **Run the self-audit** against your changes:
   - BE: `dotnet build comuki.slnx -c Debug` — `SelfAuditReport` writes
     `audit-data/last-commit-audit.md`. Open it. Count the violations.
   - FE: `bun run audit:fe` (or rely on `predev`/`prebuild`) — writes
     `audit-data/last-commit-audit-fe.md`. Open it.
3. **Fix every violation** flagged by the audit. The audit is bounded
   to known banned patterns (`_ = `, `ConfigureAwait`, `ThrowIf`,
   `private static JsonSerializerOptions`, `throw ex;`, `export default`,
   `React.FC`, `as any`, `fetch(`). Anything else is the prose rule
   `.agents/rules/process/worker-audit.md` — read that next.
4. **Report** to the user:
   > Read N rule files. Audited M commit(s). Found K violation(s); fixed
   > all of them. Report: `audit-data/last-commit-audit[-fe].md`.

## The build-time banner (what to expect)

A successful `dotnet build comuki.slnx -c Debug` now prints:

```
[VerifyFormatOnBuild] dotnet format verify running (adds ~10-30s)...
[VerifyFormatOnBuild] Format check passed.
[VerifyRuleAwareness] ===========================================================
[VerifyRuleAwareness] REMINDER: read these rule files before any code change.
[VerifyRuleAwareness] Re-read at the end and self-audit (see RULES-BOOTSTRAP.md).
[VerifyRuleAwareness] ===========================================================
[VerifyRuleAwareness] [csharp]: 38 files -- analyzers, anti-patterns, api-design, ...
[VerifyRuleAwareness] [typescript]: 5 files -- react-and-components, README, ...
[VerifyRuleAwareness] [process]: 10 files -- agent-orchestrate, build-verification, ...
[VerifyRuleAwareness] [observability]: 1 files -- diagnostics
[VerifyRuleAwareness] [project(coding)]: 8 files -- ANALYZERS, CODING-RULES, ...
[VerifyRuleAwareness] [project(process)]: 6 files -- allowed-scripts, ...
[VerifyRuleAwareness] ===========================================================
... build proceeds ...
[SelfAuditReport] Generating audit report at <repo>/audit-data/last-commit-audit.md...
[SelfAuditReport] See <repo>/audit-data/last-commit-audit.md
... build succeeded ...
```

If the banner is missing — the targets are disabled. Re-enable by
dropping `-p:DisableRuleAwareness=true` / `-p:DisableSelfAuditReport=true`
from the build command.

## Disable (inner-loop only)

```bash
# Skip both targets (true inner-loop escape hatch):
dotnet build comuki.slnx -c Debug -p:DisableRuleAwareness=true -p:DisableSelfAuditReport=true

# Skip just one (e.g. you trust the audit and just want to silence the banner):
dotnet build comuki.slnx -c Debug -p:DisableRuleAwareness=true
```

`DesignTimeBuild` (IDE background compile) skips both targets automatically.

## Failure mode: a rule is missing from the corpus

If `[VerifyRuleAwareness]` reports `<scope>: not found`, the corpus is
absent from that machine. The agent MUST still read whatever IS there
(stale or partial). Do NOT silently treat the absence as "no rules to
read" — flag it to the user.

## See also

- `AGENTS.md` — repo orientation
- `.agents/STATE.md` — current phase + decisions
- `.agents/rules/process/worker-audit.md` — the prose self-audit gate
- `.agents/rules/process/build-verification.md` — build gate contract
- `.agents/rules/process/commit-format.md` — commit format
- `.agents/docs/operations/onboarding.md` — human onboarding doc
