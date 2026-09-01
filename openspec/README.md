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
| [runs](specs/runs/spec.md) | Run / work-item aggregates, status enums (7 run / 6 work-item, no `stalled`), transition tables, the append-only `run_events` journal, lease-reaper policy (requeue vs fail by MaxAttempts) |
| [work-queue](specs/work-queue/spec.md) | SKIP LOCKED claim with exact label match (image / profiles_ref / profile_key), lease columns, owner-guarded heartbeat, complete/fail, queued-depth count |
| [compute](specs/compute/spec.md) | `IComputeProvider` port, Docker provider env/label contract (`COMUKI_*`, `comuki.*`), opaque 256-bit worker tokens (HMAC + pepper, TTL, revoke), scale supervisor v0 (create-per-task policy, MinIdle/MaxConcurrent, idle-TTL reaping), WorkerId pre-issue |
| [identity](specs/identity/spec.md) | Users (own store + PasswordHasher), `ck_` API keys (prefix + HMAC, show-once, burned prefixes, last_used throttle), role assignments (subject × role × scope, seniority guard, partial unique indexes), permission catalog + RoleMatrix + startup validation + `RequiresPermission` filter, OIDC linking, cookie `tokens_version` validation |
| [projects](specs/projects/spec.md) | CRUD with immutable unique slugs, soft archive, settings with optimistic concurrency (409) and live reload (change-token + 15s refresher + 30s TTL cache), the scale-settings adapter (Get from cache, Override = NotSupportedException) |
| [control-plane](specs/control-plane/spec.md) | Profiles / chat-commands / skills markdown+frontmatter format, tolerant parsing (malformed → skip+warn), catalog endpoints (`GET /profiles`, `/profiles/{key}`, `/chat-commands`; prompt bodies not exposed), `plan:read` / `chat:use` |
| [worker-runtime](specs/worker-runtime/spec.md) | Code-first gRPC bidi stream (protobuf-net), stream end-on-events-complete, StageStart journal binding, worker REST claim/heartbeat/complete/fail (409 ownership), Translator loop (claim → pi spawn → stream → report → complete), `COMUKI_*` env contract, worker image (bun + pi multi-stage), TestFakePi |
| [host](specs/host/spec.md) | Single internal composition point, one resolved DB connection (`COMUKI_DB`; missing → boot fails), anonymous `/health`, auth endpoints (login/logout/me/OIDC start+callback), idempotent bootstrap admin (half-pair fails boot), the migrator tool |
| [agents-sdk](specs/agents-sdk/spec.md) | Bun workspace layout, `@comuki/agent-core` (zod pi-event mirror, brief/report protocol, rule-doc reader), `@comuki/worker-sdk` (lock kinds + matching, default lock set, skills loader) |
| [build-and-ci](specs/build-and-ci/spec.md) | The single build gate, format-verify mechanics (log-parse workaround, excludes, escape hatches), analyzer policy (IDE + CA-Security surgical set), CI jobs (build-be, test-be MTP matrix, non-blocking build-fe) |

## Conventions

- Specs are English; requirements use RFC-style SHALL/MUST wording with
  `#### Scenario:` blocks (`WHEN`/`THEN`).
- One folder per capability under `specs/<capability>/spec.md`.
- Specs describe user-visible / API-visible behavior, not implementation
  gossip; cross-reference capabilities by name, not path.
- Repo-wide context and artifact rules live in `config.yaml`.
