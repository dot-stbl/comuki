# Wave-6 + Wire-up Audit

## Summary

The previous OpenSpec backfill (`backfill-wave6-platform`) closed the
gap on Wave 6 platform seams (K8s compute provider, OTel + Grafana,
runs list + filter DSL, subject-scope ambient filters). Since that
change landed, **12 issues closed** that did NOT receive an OpenSpec
delta. This audit documents the spec surface for those seams and
points out gaps in the brief's premise so remediation is targeted.

**One caveat up front:** the brief's issue-numbering is off by ten
for two of the rows. The canonical issue numbers from `gh issue list
--repo dot-stbl/comuki --state all` are #18 / #19 / #20 / #21 / #22 /
#23 / #24 / #25 / #26 / #27 / #28 / #29; the brief's "WHAT LANDED"
list says #17 / #18 / #19 / ... — `Inbound PR-review` is **#27** and
`Run artifact bundle` is **#28**, not #17 and #18. The audit table
below uses the canonical numbers.

## Closed since last backfill (issues #18–#29)

| # | Issue | Verdict | Existing OpenSpec coverage |
|---|-------|---------|---------------------------|
| 18 | Split multi-type files (one type per file) | Code-only; docs do not need an OpenSpec delta | n/a |
| 19 | SignalR `EnableDetailedErrors` leaks stack traces | Gated by 4 env-var conditions; **no OpenSpec entry** | `host/spec.md` says "MAY be enabled in development" only |
| 20 | Six endpoints hand-roll ProblemDetails | All converted to `TypedResults.Problem`; **no OpenSpec entry** | `host/spec.md` does not document the convention |
| 21 | Dev DB password committed in `appsettings.json` | `COMUKI_MIGRATOR_DB_PASSWORD` env var + Production gate; **no OpenSpec entry** | `host/spec.md` does not name the env var |
| 22 | `CostsModuleEndpoints` lacks `RequiresPermission` | `cost:read` on `GetCostsAsync`; **already covered** by the backfill-wave6-platform `costs` capability | `costs/spec.md` |
| 23 | `CostsModuleEndpoints` uses literal route instead of `ApiRoutes.ProjectCosts` | The literal route bug was fixed by adding a NEW `RunArtifacts` constant; `ProjectCosts` is unchanged | `host/spec.md` does not mention the constant split |
| 24 | Drop `*Dto` suffix from 12 Intake wire records | Code-only; covered by the `naming-and-types` rule | n/a |
| 25 | Cap folder file count at 3 | Code-only; covered by the `folder-organization` rule | n/a |
| 26 | Introduce real Postgres schemas per DbContext | 8 schemas now exist (orchestration / identity / projects / memory / chat / intake / costs / **artifacts**) | Partially in `runs` (orchestration) + `intake`; **`artifacts` is brand-new** |
| 27 | Inbound PR-review (GH/GL pull requests + `pr-review` profile) | `InboundTicketKind` + `IIntakeProfileRouter` + `pr-review` profile; `intake/spec.md` mentions `PullRequest`-kind but **does not name the discriminator or the port** | partial |
| 28 | Run artifact bundle in MinIO | Brand-new `Comuki.Modules.Artifacts` module + host driver + journal event + read API; **no `openspec/specs/artifacts/spec.md`** | `host/spec.md` carries the run + endpoint + event but the module contract (`IRunArtifactStore`, packager, schema, retention) is undocumented |
| 29 | OpenAPI emission + kubb alignment | Fully wired in `Comuki.Host.csproj` + `HostComposer.cs`; **already in `host/spec.md`** (Requirement "OpenAPI document emission") | covered |

The audit-document tally: **3 issues with no OpenSpec entry at all**
(#19, #20, #21), **5 issues with partial OpenSpec entry** (#22, #23,
#26, #27, #28), **3 issues that are code-only or already covered**
(#18, #24, #25, #29).

## Current OpenSpec coverage vs code

| Capability | Main spec | Last delta | Coverage |
|---|---|---|---|
| `runs` | `openspec/specs/runs/spec.md` | `backfill-wave6-platform/specs/runs` (filter DSL) | **Incomplete**: missing `run.artifacts_bundled` event + artifacts read API + the 7 `DeliveryOutcomes` labels |
| `intake` | `openspec/specs/intake/spec.md` | `backfill-intake` (initial) + later PR-review delta | **Partial**: `PullRequest`-kind mentioned but `InboundTicketKind` enum and `IIntakeProfileRouter` port are not named |
| `host` | `openspec/specs/host/spec.md` | multiple deltas (telemetry, realtime, OpenAPI, artifacts) | **Incomplete**: missing SignalR `EnableDetailedErrors` gates, `TypedResults.Problem` convention, Migrator password env var + Production gate, `ProjectCosts` / `RunArtifacts` constant split |
| `realtime` | `openspec/specs/realtime/spec.md` | `backfill-realtime` | **Incomplete**: missing `run.artifacts_bundled` event broadcast |
| `costs` | `openspec/specs/costs/spec.md` | `backfill-costs` | covered |
| `artifacts` | **absent** | n/a | **Missing entirely**: brand-new module has no `openspec/specs/artifacts/spec.md` |
| `chat` / `memory` | present | `backfill-chat-memory` | covered |
| `compute` / `filtering` / `identity` / `projects` / `control-plane` / `worker-runtime` / `agents-sdk` / `build-and-ci` | present | various | covered (none of the closed Wave 6 issues touch these) |

## Gaps

| # | Gap | Where it should land | Severity |
|---|-----|----------------------|----------|
| G1 | `run.artifacts_bundled` event + `GET /api/v1/projects/{projectId}/runs/{runId}/artifacts` | `specs/runs/spec.md` | High — the journal event is a platform contract; downstream consumers (dashboard Artifacts tab, SignalR) read it |
| G2 | Seven `DeliveryOutcomes` labels (`admitted` / `pending` / `filtered` / `skipped` / `duplicate` / `rejected` / `replay`) | `specs/runs/spec.md` (or `specs/intake/spec.md`) | High — the `WebhookIntakeService.HandleAsync` pipeline uses every label |
| G3 | `InboundTicketKind` discriminator (Issue / PullRequest) | `specs/intake/spec.md` | High — the kind is the input the profile router reads |
| G4 | `IIntakeProfileRouter` port | `specs/intake/spec.md` | High — the seam that keeps a foreign PR from landing on `implement` |
| G5 | SignalR `EnableDetailedErrors` env-var gates | `specs/host/spec.md` (current entry is a one-liner "MAY be enabled in development") | High — security-relevant; production must NOT have it on |
| G6 | `TypedResults.Problem` convention | `specs/host/spec.md` | Medium — the convention is enforced in code; the spec should record it |
| G7 | `COMUKI_MIGRATOR_DB_PASSWORD` + Production blank-password gate | `specs/host/spec.md` | High — deploy-facing env var |
| G8 | `ProjectCosts` / `RunArtifacts` constant split | `specs/host/spec.md` | Low — the literal-route bug is fixed in code |
| G9 | `run.artifacts_bundled` broadcast via `IRunEventsBroadcaster` | `specs/realtime/spec.md` | Medium — keeps realtime and runs in lock-step |
| G10 | `artifacts` capability | `specs/artifacts/spec.md` (new file) | **Highest** — the module is in code but the contract is undocumented |

10 gaps; 1 brand-new capability (`artifacts`); 4 modified capabilities
(`runs`, `intake`, `host`, `realtime`); 1 README map row to add.

## Per-spec drift notes

### `runs/spec.md`

The spec already documents the `run_events` journal and the
`work_item.status_changed` event family. Two event types are missing:

- `run.artifacts_bundled` — the host's packager appends this row after
  every successful bundle; the payload carries the canonical
  `ArtifactPointer` list. Downstream consumers (SignalR broadcast,
  dashboard Artifacts tab) read it.
- The read API at
  `GET /api/v1/projects/{projectId}/runs/{runId}/artifacts` is NOT in
  the `runs` spec. It IS in `host/spec.md` (under the "Run artifact
  bundle in MinIO" Requirement) but a runs-level contract belongs in
  the runs spec, not the host spec — the runs spec owns the run,
  the host spec owns the wiring.

The `DeliveryOutcomes` labels (the seven strings the intake webhook
pipeline records on `intake_deliveries`) are also missing from the
runs spec. The intake spec has a partial mention
("`replay`, `skip`, `filtered`, `duplicate` SHALL answer 200") but
the full list (`admitted`, `pending`, `filtered`, `skipped`,
`duplicate`, `rejected`, `replay`) and the `rejected` → 401 mapping
need a canonical home. The runs spec is the better fit because the
labels are part of the journal contract.

### `intake/spec.md`

The spec has a "Pull-request / merge-request ingress (issue #27)"
Requirement, but the implementation uses the `InboundTicketKind`
discriminator (an enum on the domain entity) and the
`IIntakeProfileRouter` port (in `Comuki.Modules.Intake.Application.Ports.Admission`).
Neither is named in the spec — the spec only describes the behaviour
("Admitted PRs SHALL be stamped as `PullRequest`-kind tickets"),
not the type or the seam.

The profile-routing Requirement (also issue #27) describes the
behavior but not the port. A reader who goes looking for
`IIntakeProfileRouter` in the codebase would find the type but no
spec entry explaining its place in the architecture.

### `host/spec.md`

The spec has grown with each backfill — artifacts, OpenAPI, telemetry
all live here. The four Wave 6 seams that landed in the host are
undocumented:

- `EnableDetailedErrors` env-var gates: the spec currently says
  "Detailed errors MAY be enabled in development; production still
  requires authenticated hub access" — the four env-var conditions
  (`ASPNETCORE_ENVIRONMENT=Development` /
  `DOTNET_ENVIRONMENT=Development` / `DOTNET_RUNNING_IN_CONTAINER=true`
  / `COMUKI_REALTIME_DETAILED_ERRORS=true`) are not enumerated.
- `TypedResults.Problem` convention: the spec mentions
  `application/problem+json` but does not document the build path
  (`TypedResults.Problem(... extensions: { "code" = ... })`) or
  the single `IExceptionHandler` (`ProviderExceptionHandler`) that
  is the canonical mapper.
- Migrator password: the spec describes the migrator's connection-
  string resolution order but does not name
  `COMUKI_MIGRATOR_DB_PASSWORD` or the Production blank-password
  gate.
- `ProjectCosts` / `RunArtifacts` route constants: the spec lists
  the artifacts endpoint URL but does not call out the
  `ApiRoutes.RunArtifacts` constant or the fact that the
  `ArtifactsOptions` configuration section is
  `Artifacts:Endpoint/AccessKey/SecretKey/Bucket/UseSSL/AutoCreateBucket`
  (the existing spec text says `Artifacts:Minio:*` — that is a typo,
  the actual section is `Artifacts:*`).

### `realtime/spec.md`

The hub / broadcast / attention model is well-documented. One
gap: the spec never says that `run.artifacts_bundled` events flow
through the same `IRunEventsBroadcaster` to the `run:{id}` group.
A consumer of the realtime spec would assume only the listed
attention-worthy transitions are broadcast; the bundle event
should be called out so dashboard authors wire to the right
client method.

## Missing specs

### `artifacts/spec.md` (brand new)

The `Comuki.Modules.Artifacts` module exists in
`platform/src/modules/Artifacts/` with:

- **Domain**: `RunArtifactBundle`, `ArtifactPackageTriggers`
- **Application**: `ArtifactsApplicationExtensions`, `RunArtifactPackager`,
  `RunArtifactPackagerService`, `IRunArtifactBundleStore`,
  `IRunArtifactJournalSource`, `IRunArtifactRunSource`,
  `NullRunArtifactJournalSource`, `NullRunArtifactRunSource`,
  `NullRunArtifactStore`
- **Infrastructure**: `ArtifactsPersistenceExtensions`,
  `ArtifactsDatabase` (`Schema = "artifacts"`, `RunBundles`),
  `ArtifactsDbContext`, `EfRunArtifactBundleStore`,
  `RunBundleConfiguration`, `ArtifactsOptions`, `MinioClientFactory`,
  `MinioRunArtifactStore`, initial migration
  `20260903141326_InitialArtifactsSchema` plus a `UseSchemas` migration
- **Host (cross-module)**: `RunArtifactPackagerHostService` (host
  driver), `ArtifactEventTypes` (`RunArtifactsBundled = "run.artifacts_bundled"`),
  `ArtifactEventPayloads`, `RunArtifactsController` (read API)
- **Shared contracts**: `IRunArtifactStore`, `ArtifactPointer`,
  `IRunArtifactJournalSource`, `RunTerminalSnapshot`

The new `openspec/specs/artifacts/spec.md` (in `specs/artifacts/`
under this change) documents the contract: the `IRunArtifactStore`
surface, the packager driver, the host driver wrapping the journal
event, the three bundle objects (`brief.json` / `result.json` /
`pins.json`), the `Artifacts:*` config section, the
`artifacts` Postgres schema, and the v1 retention policy
("never delete").

## What the brief got right vs wrong

Right:

- Inbound PR-review is closed (issue #27, not #17).
- Run-artifact bundle is closed (issue #28, not #18).
- SignalR `EnableDetailedErrors` is gated.
- `TypedResults.Problem` is the convention.
- Migrator password env var exists.
- OpenAPI emission + kubb alignment landed.
- `Artifacts:Minio:*` requirement is in `host/spec.md`.

Wrong:

- The brief's #17 should be #27, the brief's #18 should be #28.
- The brief says "`host/spec.md` doesn't have the `Artifacts:Minio:*`
  requirement documented" — it DOES, in the "Run artifact bundle in
  MinIO" Requirement (host spec line 119). The brief may have been
  checking against an earlier snapshot.
- The brief says "`host/spec.md` doesn't have the OpenAPI emission
  requirement — wait, this was added in #29 (`f54f9a7`) but might
  not have made it through merge cleanly" — it did, the
  "OpenAPI document emission" Requirement is at host spec line 143.
  The brief is correct that it's worth verifying; the audit
  confirms it landed.
- The brief says "ApiRoutes.ProjectCosts was changed" — it was NOT
  changed. The literal-route bug was fixed by adding a NEW
  `ApiRoutes.RunArtifacts` constant. `ProjectCosts` is still
  `/api/v1/projects/{projectId:guid}/costs`. The brief's framing
  is misleading; the only real `ApiRoutes` change is the addition
  of `RunArtifacts`.

## Suggested follow-ups (NOT in this change)

- Bump `Microsoft.AspNetCore.OpenApi` 10.0.9 → 3.x when the source
  generator + transformer migration to the 3.x immutable AST lands
  (the `NU1903` NU1903 GHSA-v5pm-xwqc-g5wc high-severity advisory
  on transitive `Microsoft.OpenApi 2.0.0` is the only reason
  `Release=false` is set explicitly).
- v1.x retention feature for run bundles (configurable TTL,
  lifecycle sweeps). The `artifacts` spec calls this out as
  explicitly out of scope.
- Per-run artifact ACLs (the current `RunArtifactsController`
  shares `run:read` with the parent RunsController). A future
  `requires_recently_bundled` permission or per-object ACL would
  belong in a follow-up change.
