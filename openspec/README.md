# Comuki OpenSpec

Behavior contracts for the Comuki platform. These specs describe what the
landed code does **today** — they are backfilled from the implementation and
updated through OpenSpec changes, never edited ad hoc.

- **Issue tracker** = delivery tracking (what we're building next, in flight).
- **`openspec/specs/`** = behavior contract (what the system does now).

A spec change lands through the OpenSpec change workflow (proposal → delta
specs → apply → archive), not by silently editing a main spec.

## Capability map

| Capability | What it covers |
|---|---|
| [runs](specs/runs/spec.md) | Run / work-item aggregates, status enums (7 run / 6 work-item, no `stalled`), transition tables, the append-only `run_events` journal (incl. `budget.exceeded`, `run.artifacts_bundled`), lease-reaper policy, `GET /api/v1/runs` list + filter DSL, run-artifacts read API, the operator decision endpoints (`/approve`, `/cancel`) |
| [work-queue](specs/work-queue/spec.md) | SKIP LOCKED claim with exact label match (image / profiles_ref / profile_key), lease columns, owner-guarded heartbeat, complete/fail, queued-depth count |
| [artifacts](specs/artifacts/spec.md) | Run-bundle MinIO store (`IRunArtifactStore`, `ArtifactPointer`, `RunTerminalSnapshot`), per-run packager + host driver, `run.artifacts_bundled` event, `GET /api/v1/projects/{projectId}/runs/{runId}/artifacts`, `Artifacts:*` config, `artifacts` schema, v1 retention = never delete |
| [compute](specs/compute/spec.md) | `IComputeProvider` port, Docker + Kubernetes (batch/v1 Job) providers, env/label contract (`COMUKI_*`, `comuki.*`), opaque worker tokens, scale supervisor v0, WorkerId pre-issue |
| [identity](specs/identity/spec.md) | Users, `ck_` API keys, role assignments, permission catalog + `RequiresPermission`, OIDC linking, cookie `tokens_version`, ambient subject-scope object axis (EF filters → 404) |
| [projects](specs/projects/spec.md) | CRUD with immutable unique slugs, soft archive, settings (scale + feature flags + soft/hard budget USD micros) with optimistic concurrency and live reload, scale-settings adapter |
| [control-plane](specs/control-plane/spec.md) | Profiles / chat-commands / skills markdown+frontmatter format, tolerant parsing, catalog endpoints, `plan:read` / `chat:use` |
| [worker-runtime](specs/worker-runtime/spec.md) | Code-first gRPC bidi stream (protobuf-net), Translator loop, worker REST claim/heartbeat/complete/fail, `COMUKI_*` env, worker image, TestFakePi |
| [host](specs/host/spec.md) | Composition point, DB connection, `/health`, auth, bootstrap admin, migrator, SignalR `/hubs/runs`, OTel opt-in, CORS allow-list, per-endpoint rate limits, production-secret fail-fast |
| [chat](specs/chat/spec.md) | Subject-owned sessions, Voluta turns + approve interrupt, transcript paging, slash catalog (`chat:use`) |
| [memory](specs/memory/spec.md) | `memory_facts` (+ pgvector-ready), supersede by topic, ephemeral 14d sweep, MemoryDigest, Brain `memory.search`, learning_candidates queue |
| [intake](specs/intake/spec.md) | Anonymous signed webhooks, sources, admission rules, inbox/claim, native tickets, sync_jobs |
| [costs](specs/costs/spec.md) | `usage_events`, soft/hard budgets, hard-stop gate, `GET …/projects/{id}/costs` |
| [realtime](specs/realtime/spec.md) | RunsHub joins, journal `RunEvent` broadcast (incl. `run.artifacts_bundled`), project attention signals |
| [filtering](specs/filtering/spec.md) | Shared list filter/sort DSL (`FilterQuery`) used by runs list |
| [agents-sdk](specs/agents-sdk/spec.md) | Bun workspace layout, `@comuki/api-core`, `@@@/worker-sdk` |
| [build-and-ci](specs/build-and-ci/spec.md) | Single build gate, format-verify, analyzer policy, CI jobs |

Active OpenSpec changes under `changes/` backfill Wave 5–6 docs (`backfill-*`).
The post-Wave-6 audit (`audit-wave-6`) is applied — see its
`audit.md` for the per-issue gap analysis and the `changes/audit-wave-6/`
folder is retained as the audit record.
The earlier design-only `add-chat-memory` change remains for historical S5
intent; prefer the backfill + main `chat`/`memory` specs for landed behavior.

## Conventions

- Specs are English; requirements use RFC-style SHALL/MUST wording with
  `#### Scenario:` blocks (`WHEN`/`THEN`).
- One folder per capability under `specs/<capability>/spec.md`.
- Specs describe user-visible / API-visible behavior, not implementation
  gossip; cross-reference capabilities by name, not path.
- Repo-wide context and artifact rules live in `config.yaml`.

## How to use openspec

This section is the canonical entry point for humans and agents who need
to either *read* a spec, *change* a spec, or *verify* a spec against the
landed code. Three flows live here; pick the one that matches the task.

### Reading a spec

Specs live under `specs/<capability>/spec.md`. Every spec opens with a
`# <Capability> Specification` heading, then a `## Purpose` paragraph,
then `## Requirements` with one `### Requirement:` block per contract
followed by one or more `#### Scenario:` blocks (`WHEN`/`THEN`).

The grep-by-capability recipe — when you want to find every reference
to a feature across the spec tree:

```bash
# Every requirement and scenario mentioning, e.g., rate limiting.
grep -rn 'rate.limit\|RateLimit' openspec/specs/

# Every requirement that mentions a specific endpoint.
grep -rn '/api/v1/runs/' openspec/specs/
```

### Changing a spec

A spec change lands through the OpenSpec change workflow — never by
silently editing a main spec. The shape:

1. `openspec/changes/<change-name>/proposal.md` — the WHY and the
   high-level WHAT (RFC-style summary).
2. `openspec/changes/<change-name>/design.md` — the implementation
   sketch (interfaces, schema, files).
3. `openspec/changes/<change-name>/tasks.md` — the concrete commit
   list, in order.
4. `openspec/changes/<change-name>/specs/<capability>/spec.md` — the
   delta specs (one folder per modified capability; brand-new
   capabilities live under `specs/`).
5. `openspec/changes/<change-name>/.openspec.yaml` — the change
   manifest linking delta specs to target capabilities.

Apply the change by syncing each delta spec into its target main spec
(`openspec/specs/<capability>/spec.md`), then archiving the change folder
into `openspec/changes/archive/<yyyymmdd>/<change-name>/` for history.
The `audit-wave-6` folder is the canonical example of an archived
change.

### Verifying a spec

`audit-wave-6/specs/*/spec.md` is the canonical retrospective — the
table at the top of `audit-wave-6/audit.md` lists every closed issue
and which spec entry covers it. When in doubt about whether a code path
has a spec entry:

```bash
# 1. Find the implementation under test.
grep -rn '<feature>' platform/src/ | head

# 2. Look up the spec entry that covers it.
grep -rn '<feature>' openspec/specs/

# 3. Cross-check the change that introduced it.
ls openspec/changes/audit-wave-6/specs/
```

If the implementation has no spec entry, the gap belongs in a new
OpenSpec change — not a silent edit. If a delta spec exists in
`openspec/changes/<change>/specs/` but the main spec is missing the
content, the change was never synced; finish the sync before the next
release cuts.

## Validation

There is no canonical validator wired into the build today; the
contract is enforced by code review against the rules in
`.agents/rules/`. The patterns are:

- Every requirement uses `SHALL` / `MUST` wording.
- Every scenario uses `WHEN` / `THEN` clauses.
- One folder per capability under `specs/<capability>/spec.md`.
- Specs describe user-visible / API-visible behavior, not
  implementation gossip.

The `audit-wave-6/audit.md` table is the most recent retro and the
template for the next one.