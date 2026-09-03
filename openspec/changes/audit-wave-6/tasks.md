## 1. Spec backfill
- [ ] **T1** Add `run.artifacts_bundled` event + `GET /api/v1/projects/{projectId}/runs/{runId}/artifacts` to `openspec/specs/runs/spec.md` (Requirement + 4 Scenarios).
- [ ] **T2** Add the seven `DeliveryOutcomes` labels to `openspec/specs/runs/spec.md` (Requirement + 7 Scenarios: replay / rejected / admitted / pending / filtered / duplicate / skipped).
- [ ] **T3** Add `InboundTicketKind` (Issue / PullRequest) discriminator to `openspec/specs/intake/spec.md` (Requirement + 2 Scenarios).
- [ ] **T4** Add `IIntakeProfileRouter` port + `pr-review` profile behavior to `openspec/specs/intake/spec.md` (Requirement + 4 Scenarios).
- [ ] **T5** Add SignalR `EnableDetailedErrors` env-var gates to `openspec/specs/host/spec.md` (Requirement + 3 Scenarios: production-off / dev-on / integration-opt-in).
- [ ] **T6** Add `TypedResults.Problem` convention to `openspec/specs/host/spec.md` (Requirement + 2 Scenarios: typed exception surfaces RFC 9457 / controller does not hand-roll).
- [ ] **T7** Add `COMUKI_MIGRATOR_DB_PASSWORD` + Production blank-password gate to `openspec/specs/host/spec.md` (Requirement + 2 Scenarios: dev reads from env / Production refuses).
- [ ] **T8** Add `ProjectCosts` / `RunArtifacts` route constant split to `openspec/specs/host/spec.md` (Requirement + 2 Scenarios).
- [ ] **T9** Add `run.artifacts_bundled` broadcast on the existing `IRunEventsBroadcaster` to `openspec/specs/realtime/spec.md` (Requirement + 1 Scenario).
- [ ] **T10** Create `openspec/specs/artifacts/spec.md` (new capability — `IRunArtifactStore`, `RunArtifactPackager`, host driver, brief/result/pins objects, `Artifacts:*` config, `artifacts` schema, v1 retention).

## 2. README + change apply
- [ ] **T11** Add an `artifacts` row to the capability map table in `openspec/README.md` (with a one-line description: "Run-bundle MinIO store, host driver, `run.artifacts_bundled` event, read API").
- [ ] **T12** Apply the deltas from this change to the main specs (sync `specs/runs/spec.md`, `specs/intake/spec.md`, `specs/host/spec.md`, `specs/realtime/spec.md`).

## 3. Verification
- [ ] **T13** Cross-check every requirement in the audit document's "Closed since last backfill" table against the corresponding spec — confirm each row now has either a delta in this change or a main-spec entry that already covered it.
- [ ] **T14** Run a content grep over the four touched main specs to confirm no duplicate Requirement titles after the merge (delta `### Requirement:` + main `### Requirement:` may collide on import).
- [ ] **T15** Confirm the brief's misnumbering (`#17`, `#18` → `#27`, `#28`) is captured in the audit document so future agents don't trip on the same wrong number.
