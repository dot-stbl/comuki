# Scan 5 — OpenSpec coverage + drift

**Scan tool:** Manual review of `openspec/specs/**/*.md` + cross-ref
with `git log` (recent code changes) + check `openspec/changes/**/*.md`
status.

## 5.1 Capability coverage matrix

There are **17 active specs** in `openspec/specs/`, all of which have
both a `spec.md` and (per `audit-wave-6`) a delta-spec row in
`openspec/README.md`. Latest spec update was 2026-09-05 (`costs/spec.md`)
and 2026-09-05 (`host/spec.md`).

| Spec | Path | Last updated | Lines |
|---|---|---|--:|
| `agents-sdk` | `openspec/specs/agents-sdk/spec.md` | 2026-09-01 (`f676b1b`) | 58 |
| `artifacts` | `…/artifacts/spec.md` | 2026-09-04 (`2735bdc`) | 129 *(new spec, S10 + audit-wave-6)* |
| `build-and-ci` | `…/build-and-ci/spec.md` | 2026-09-01 | 63 |
| `chat` | `…/chat/spec.md` | 2026-09-02 (`48fdf02`) | 53 |
| `compute` | `…/compute/spec.md` | 2026-09-02 | 84 |
| `control-plane` | `…/control-plane/spec.md` | 2026-09-01 | 49 |
| `costs` | `…/costs/spec.md` | 2026-09-05 (`f1d40c2`) | 57 |
| `filtering` | `…/filtering/spec.md` | 2026-09-02 | 35 |
| `host` | `…/host/spec.md` | 2026-09-05 (`9566546`) | **366** |
| `identity` | `…/identity/spec.md` | 2026-09-05 (`945f0d0`) | 161 |
| `intake` | `…/intake/spec.md` | 2026-09-05 (`ce3d618`) | 273 |
| `memory` | `…/memory/spec.md` | 2026-09-02 | 70 |
| `projects` | `…/projects/spec.md` | 2026-09-02 | 60 |
| `realtime` | `…/realtime/spec.md` | 2026-09-04 (`339fbaf`) | 63 |
| `runs` | `…/runs/spec.md` | 2026-09-04 (`03494a8`) | 268 |
| `work-queue` | `…/work-queue/spec.md` | 2026-09-01 | 50 |
| `worker-runtime` | `…/worker-runtime/spec.md` | 2026-09-01 | 85 |

**Coverage:** every spec has a spec.md. ✓

## 5.2 Active changes status

| Change | Created | Tasks status | Verdict |
|---|---|---|---|
| `add-chat-memory` | (older — design-only) | ~ partial: `memory.search` wired; write/forget/list tools partial | design contract, kept open as the original spec pin |
| `audit-wave-6` | 2026-09-03 | **all 15 complete** (`12f4fe7`) | ✅ applied |
| `backfill-chat-memory` | 2026-09-02 | all complete | ✅ applied (chat + memory main specs) |
| `backfill-costs` | 2026-09-02 | all complete | ✅ applied (costs/projects/runs specs) |
| `backfill-intake` | 2026-09-02 | all complete | ✅ applied (intake spec) |
| `backfill-realtime` | 2026-09-02 | all complete | ✅ applied (host/realtime specs) |
| `backfill-wave6-platform` | 2026-09-02 | all complete | ✅ applied (compute/filtering/host/identity/runs specs) |

All waves are **closed** against `master`. No drift in change-status.

## 5.3 Spec/code drift findings

I checked each spec's last update against the most recent code changes
in that module. Two areas need attention:

### 5.3.1 `proxy/spec.md` does not exist

There is **no standalone `openspec/specs/proxy/spec.md`**. Proxy
spec content lives in two places:

- `openspec/specs/costs/spec.md` — has a "Requirement: Proxy source"
  + "Requirement: Proxy monthly budget" — describes metering only.
- `openspec/specs/host/spec.md` — "Requirement: Optional OpenAI /
  Anthropic proxy" — describes the YARP surface, virtual-key auth,
  enable flag.

This split is **acceptable** but inconsistent with the rest of the
codebase where each module has a single home. Recommend adding
`openspec/specs/proxy/spec.md` as a thin pointer + cross-spec, with
the metering-vocabulary moved out of `costs/spec.md` (Costs owns
*receiving* usage events; Proxy owns *emitting* them) and the
optional-proxy behavior consolidated.

Verdict: **mild drift, low priority** — not a violation, just a
housekeeping issue.

### 5.3.2 `knowledge/spec.md` does not exist (similar to Proxy)

There is **no standalone `openspec/specs/knowledge/spec.md`**. Knowledge
content is split between:

- `openspec/specs/host/spec.md` — has an entry for
  `POST /api/v1/knowledge/ingest` (the host endpoint).
- `openspec/specs/memory/spec.md` — pgvector embedding schema + cosine
  search are described in **memory**'s spec.

This isn't a violation — Knowledge is genuinely an opt-in feature
that overlays Memory — but it would read better to have a
`knowledge/spec.md` that points to the MEMORY spec for pgvector and
to the HOST spec for the endpoint, with the cross-module interface
documented in one place.

Verdict: **mild drift, low priority**.

### 5.3.3 `intake/spec.md` mentions fewer sources than code ships

Recent code added **Jira, Yandex Tracker** as intake sources (per
`.agents/STATE.md` S6 — `GH/GL/Yandex Tracker/Jira + sync-back +
PR-review`). The intake/spec.md has 273 lines covering GH + GL
thoroughly, but searching for `Yandex` / `Yandex Tracker` / `Jira`
in `openspec/specs/intake/spec.md`:

```
$ grep -i 'yandex\|jira' openspec/specs/intake/spec.md
(no hits)
```

This is a **drift candidate** — code has Yandex + Jira providers
intact, but the spec only documents GH + GL. The user-facing
"provider set" is a stability contract: a new provider may have
different webhook semantics and different sync-back rules. The
spec should at minimum list the supported providers, even if
Yandex/Jira are documented elsewhere.

Verdict: **real drift, medium priority** — recommend a small
follow-up openspec change to add a "Requirement: Supported source
providers" enumerating GH/GL/Yandex/Jira with sync semantics for
each (the S6 sync-back story lives in `tasks.md`, but the spec
list of providers is missing).

### 5.3.4 Spec is current on admin endpoints (good)

- `identity/spec.md:161` — "Identity admin surface (issues #31-#37)"
  with 6 requirements + scenarios for invite / grant / revoke /
  issue key / revoke key / link OIDC / toggle disabled. ✓
- `intake/spec.md` (after 2026-09-05 update) — covers "Sources
  SecretReference" + probe/connect/update/test-draft/test-connection
  (issues #38-#42). ✓
- `host/spec.md` — OIDC state sweep, SignalR detailed-errors,
  TypedResults.Problem, Migrator env-var gate, route constant
  split. ✓

### 5.3.5 `realtime/spec.md` updated this round (`339fbaf`)

The SignalR realtime spec was updated as part of audit-wave-6 to
include `run.artifacts_bundled` broadcast. ✓

## 5.4 Spec/openspec validate status

The repo doesn't ship an `openspec validate` step in CI (per
`.agents/STATE.md` gates, the FE build pulls spec via `bun run
generate-api`, but the spec validates manually). Worth noting that
the spec markdown is well-formed (consistent `### Requirement` /
`#### Scenario` headers).

## 5.5 Recommended spec work

| Priority | Action |
|---|---|
| low | Add `openspec/specs/proxy/spec.md` as a thin indexer over `costs` + `host`. |
| low | Add `openspec/specs/knowledge/spec.md` similarly indexer over `memory` + `host`. |
| medium | Add a "Supported source providers" requirement to `intake/spec.md` covering GH + GL + Yandex Tracker + Jira with per-provider semantics. |
| low | Track `add-chat-memory` debt explicitly in the change status table (currently still `[~]` partial). |
| low | Update `openspec/specs/work-queue/spec.md` to mention the OIDC Idle-flip impact (the SKIP LOCKED claim contract was extended by `b2d1d6d` post-`48fdf02`). |
