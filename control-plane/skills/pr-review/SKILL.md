---
name: pr-review
description: How to evaluate an inbound pull request — summary, risks, gates, verdict. Used by the pr-review worker profile.
---

# Inbound PR review

Evaluate a foreign pull request the orchestrator put in front of you.
The brief carries the PR title/body, the external id, the source key,
and the worker brief goal (the goal field on the run); the workspace
holds the foreign repo at the PR's HEAD.

## Steps

1. **Summary** — what the PR changes in 2–5 bullets. Lead with the
   user-visible behavior, then the implementation outline. Skip the
   boilerplate.

2. **Risks** — list anything that could break in production:
   - Concurrency, idempotency, ordering assumptions.
   - Error paths the diff skips over (`catch (Exception)`, swallowed
     nulls, missing `CancellationToken`).
   - Security: input validation, auth, secrets in code, deserialization.
   - Performance: O(n²) over expected input size, missing index,
     unbounded allocations.
   - Convention drift: a file that ignores the project's style rules
     (`~/.agents/rules/csharp/` for .NET, the repo-local equivalents
     elsewhere).

   Skip risks you cannot ground in a file path and line range — a
   risk is not "could be cleaner".

3. **Gates** — run what the foreign repo actually runs:
   - `.NET` → `dotnet build <solution>.slnx -c Debug`; for monorepos,
     build only the affected project. Run the touched unit suites via
     `dotnet run --project <csproj>` (xUnit v3 — `dotnet test` does
     not discover).
   - TypeScript → `bun run typecheck && bun run lint && bun run test`.
   - Other ecosystems → follow the repo's own gates; never invent one.
   - If the repo's gate is a dev server / watch / long-lived process,
     **stop** and report — the worker runtime forbids those
     (`agent-runtime-safety.md`); ask the operator to run the gate in
     their own terminal.

   Mark any gate you did not run as "not verified" in the report — the
   `pr-report` skill forbids claiming green you didn't observe.

4. **Verdict** — exactly one of:
   - `approve` — no blocking risks, gates green or only advisory
     warnings.
   - `request-changes` — at least one blocking risk; list the risks
     and the smallest change that would unblock.
   - `comment` — non-blocking observations only; nothing to gate the
     merge on.

## Output

Follow the `pr-report` skill format. The sync-back worker reads the
verdict and posts one issue-comment on the PR thread with this report —
do **not** post anything yourself.

## Boundaries

- Never push, merge, close, label, or otherwise mutate the foreign
  branch or its PR.
- Never read secrets or credentials beyond what the diff already
  exposes.
- Never expand the PR's scope — drive-by refactors are a separate
  ticket.