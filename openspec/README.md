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
| [runs](specs/runs/spec.md) | Run / work-item aggregates, status enums (7 run / 6 work-item, no `stalled`), transition tables, the append-only `run_events` journal (incl. `budget.exceeded`, `run.artifacts_bundled`), lease-reaper policy, `GET /api/v1/runs` list + filter DSL, run-artifacts read API |
| [work-queue](specs/work-queue/spec.md) | SKIP LOCKED claim with exact label match (image / profiles_ref / profile_key), lease columns, owner-guarded heartbeat, complete/fail, queued-depth count |
| [artifacts](specs/artifacts/spec.md) | Run-bundle MinIO store (`IRunArtifactStore`, `ArtifactPointer`, `RunTerminalSnapshot`), per-run packager + host driver, `run.artifacts_bundled` event, `GET /api/v1/projects/{projectId}/runs/{runId}/artifacts`, `Artifacts:*` config, `artifacts` schema, v1 retention = never delete |
| [compute](specs/compute/spec.md) | `IComputeProvider` port, Docker + Kubernetes (batch/v1 Job) providers, env/label contract (`COMUKI_*`, `comuki.*`), opaque worker tokens, scale supervisor v0, WorkerId pre-issue |
| [identity](specs/identity/spec.md) | Users, `ck_` API keys, role assignments, permission catalog + `RequiresPermission`, OIDC linking, cookie `tokens_version`, ambient subject-scope object axis (EF filters → 404) |
| [projects](specs/projects/spec.md) | CRUD with immutable unique slugs, soft archive, settings (scale + feature flags + soft/hard budget USD micros) with optimistic concurrency and live reload, scale-settings adapter |
| [control-plane](specs/control-plane/spec.md) | Profiles / chat-commands / skills markdown+frontmatter format, tolerant parsing, catalog endpoints, `plan:read` / `chat:use` |
| [worker-runtime](specs/worker-runtime/spec.md) | Code-first gRPC bidi stream (protobuf-net), Translator loop, worker REST claim/heartbeat/complete/fail, `COMUKI_*` env, worker image, TestFakePi |
| [host](specs/host/spec.md) | Composition point, DB connection, `/health`, auth, bootstrap admin, migrator, SignalR `/hubs/runs`, OTel opt-in |
| [chat](specs/chat/spec.md) | Subject-owned sessions, Voluta turns + approve interrupt, transcript paging, slash catalog (`chat:use`) |
| [memory](specs/memory/spec.md) | `memory_facts` (+ pgvector-ready), supersede by topic, ephemeral 14d sweep, MemoryDigest, Brain `memory.search`, learning_candidates queue |
| [intake](specs/intake/spec.md) | Anonymous signed webhooks, sources, admission rules, inbox/claim, native tickets, sync_jobs |
| [costs](specs/costs/spec.md) | `usage_events`, soft/hard budgets, hard-stop gate, `GET …/projects/{id}/costs` |
| [realtime](specs/realtime/spec.md) | RunsHub joins, journal `RunEvent` broadcast, project attention signals |
| [filtering](specs/filtering/spec.md) | Shared list filter/sort DSL (`FilterQuery`) used by runs list |
| [agents-sdk](specs/agents-sdk/spec.md) | Bun workspace layout, `@comuki/agent-core`, `@comuki/worker-sdk` |
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
