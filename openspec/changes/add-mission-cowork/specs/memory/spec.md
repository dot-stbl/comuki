## ADDED Requirements

### Requirement: Project memory candidate state
Automatically extracted reusable knowledge SHALL enter Project memory as a Candidate with structured claim, confidence, visibility, source references, extractor generation, and status. Accepted Decisions may establish trusted claims immediately; otherwise policy may require independent confirming provenance. Candidates SHALL NOT masquerade as trusted truth.

#### Scenario: Repeated independent evidence
- **WHEN** the same non-conflicting claim is supported by the configured number of independent sources
- **THEN** policy may promote it to trusted Project memory while preserving all provenance

### Requirement: Memory conflict state
A new claim that contradicts trusted memory SHALL create a conflict containing both claims and evidence. Retrieval and Brain answers SHALL surface the dispute; recency or model confidence alone SHALL NOT overwrite trusted memory.

#### Scenario: New evidence contradicts decision
- **WHEN** an extracted claim contradicts a trusted accepted Decision
- **THEN** both remain visible as intent/evidence conflict until resolved by policy or a new Decision

### Requirement: Private Mission declassification
Content derived from a private Mission SHALL remain Mission-visible. Publishing a reusable claim to Project memory requires a redacted declassification proposal that shows the exact future claim, provenance disclosure, and target audience and is approved under Mission and project policy.

#### Scenario: Declassification rejected
- **WHEN** participants reject publication of a Mission-derived claim
- **THEN** it remains available only within Mission-scoped memory and cannot appear in project digests

### Requirement: Mission-scoped memory facts
Memory facts SHALL admit a fourth scope `Mission` alongside `User`, `Project`, and `Global`. A Mission-scoped fact is readable by every active Mission participant of that Mission and by the Mission's Brain operation; it SHALL NOT be visible to Project memory (no upgrade by scope), Project members who are not Mission participants, or any cross-project consumer. The scope check applies before visibility filtering, before Context Pack compilation, and before any public search surface. A Mission-scoped fact SHALL be deleted when its parent Mission becomes terminal (Completed/Cancelled) under the same retention policy as Mission evidence generations.

#### Scenario: Mission participant reads a Mission-scoped fact
- **WHEN** an active Mission participant calls `IMemoryStore.Search` with `Scope = Mission, SubjectId = <missionId>`
- **THEN** only facts whose `scope = Mission` and `subject_id = <missionId>` are returned; facts under any other scope are excluded.

#### Scenario: Non-participant cannot read a Mission-scoped fact
- **WHEN** a Project member who is not a participant of `<missionId>` calls `IMemoryStore.Search` with `Scope = Mission, SubjectId = <missionId>`
- **THEN** the call returns an empty result (NOT 403/404 — avoids confirming Mission existence) and a structured log entry notes the out-of-scope attempt.

### Requirement: Swarm blackboard — durable side
Workers MAY publish **findings** to a Mission-scoped durable channel that other workers of the same Mission can read. The durable side lives in Memory as `Mission` scope facts of a dedicated kind `BlackboardFinding` carrying the worker worker's key, payload hash, generation marker, and an optional TTL. The durable side SHALL record **append-only** — a finding is keyed by `(MissionId, workerKey, fingerprint)`; a second write with the same key SHALL supersede (stamp `superseded_at`) rather than append, so retrieval does not double-count. Visibility filtering on the blackboard is the same as `Mission` scope (participants + Brain).

#### Scenario: Worker publishes a finding
- **WHEN** worker `W` writes a finding `F` for Mission `M`
- **THEN** the durable side stores one row `(scope=Mission, subject_id=M, kind=BlackboardFinding, worker_key=W, fingerprint=F, payload_hash=…)`; the realtime transport emits a `BlackboardWrite` event with the same fingerprint to the Mission's swarm subscribers.

#### Scenario: Supersede re-publish
- **WHEN** worker `W` writes the same fingerprint `F` again with new text
- **THEN** the prior row is stamped `superseded_at`; the new row carries the new text and hash; retrieval returns the new row only by default (audit reads see both).

#### Scenario: Cross-Mission isolation
- **WHEN** a worker in Mission `M1` publishes a finding and a worker in Mission `M2` subscribes
- **THEN** `M2`'s worker receives zero hits and the realtime transport delivers nothing — the channel is Mission-scoped.

### Requirement: Swarm blackboard — realtime transport (IRealtimeBackplane)
The realtime side of the swarm blackboard SHALL ride on the existing `IRealtimeBackplane` (single-node InMemory or multi-node Redis adapter, per design.md decision #12). The channel key SHALL be `mission:swarm:<missionId>`; payloads carry `(fingerprint, workerKey, generation, op=write|supersede|expire)`. The realtime transport SHALL NOT consume Mission stream sequence (`IRealtimeBackplane` never publishes Mission series either). On Redis outage the durable side remains queryable through `Synchronous` polling — same degradation rule as presence.

#### Scenario: Subscribed worker receives a write
- **WHEN** worker `W` is subscribed to `mission:swarm:<missionId>` and another worker publishes a finding with fingerprint `F`
- **THEN** `W` receives one `BlackboardWrite` event with `op=write`, `fingerprint=F`, `workerKey=<publisher>`, `generation=<gen>` within the configured push latency hint; the event is NOT part of Mission sequence.

#### Scenario: Realtime push lost
- **WHEN** a `BlackboardWrite` event is published while `W`'s realtime connection is down
- **THEN** `W` falls back to `IMemoryStore.Search` for `kind=BlackboardFinding` under `(Scope=Mission, SubjectId=missionId)`, dedupes by fingerprint, and observes no gap or duplicate.

### Requirement: Outcome-reinforced candidate ranking
Candidate ranking SHALL admit outcome signals alongside the existing read/repeat counters. Each candidate SHALL carry, in addition to the existing `confidence`, `extractor_generation`, and provenance:

- `ReadCount` — total retrieval hits (existing).
- `RepeatCount` — independent episodes supporting an agent (existing).
- `VerifyPassCount` / `VerifyFailCount` — counts of Mission verification verdicts that confirmed or contradicted the claim since the candidate was created.
- `BuildGreenCount` / `BuildRedCount` — counts of orchestrator build outcomes observed by the platform that were consistent / inconsistent with the claim's prescription.
- `TaskSucceededCount` / `TaskFailedCount` — counts of Task resolution outcomes whose effect touched the candidate.

The fused rank SHALL NOT be a simple sum: outcome counts are signed contributions (`+VerifyPass`, `−VerifyFail`, `+BuildGreen`, `−BuildRed`, `+TaskSucceeded`, `−TaskFailed`), each capped at a configurable `outcome_cap_per_signal` (default 5), and combined with `confidence` via the existing policy. Read/repeat counts remain unsigned priors. The platform SHALL NOT silently overwrite a trusted accepted Decision with an outcome-signal winner — a candidate that downgrades a Decision becomes a conflict (per the existing conflict requirement), not a supersede.

#### Scenario: Verified claim outranks an unverified twin
- **WHEN** two candidates carry the same `confidence` and one has `VerifyPassCount = 3` while the other has `VerifyPassCount = 0`
- **THEN** the verified candidate ranks above the unverified one in the fused order; both stay queryable and both keep their provenance intact.

#### Scenario: Mixed signals stay bounded
- **WHEN** a candidate has `VerifyPassCount = 10` and `outcome_cap_per_signal = 5`
- **THEN** the contribution caps at `+5`; the candidate's rank does not exceed the cap simply because of an extreme tail.

#### Scenario: Conflicting outcomes create a conflict node
- **WHEN** a candidate has `VerifyPassCount = 2` and `VerifyFailCount = 2`
- **THEN** the candidate IS NOT silently upgraded; instead the conflict surface flags it for policy resolution, and Brain sees both signals via the conflict node (existing conflict requirement applies unchanged).

### Requirement: Planning memory — Brain priors of past decompositions
The platform SHALL store, as a Project- or Mission-scoped memory record, the durable decomposition footprint of a completed Mission: the DAG of stages/Workers, the worker skills/controls invoked at each node, and the final outcome (`Succeeded | Failed | Waived | Replaced`). When Brain plans a new Mission and `decomposition_priors_enabled` is set on the Project, the planner SHALL query this memory and surface the prior DAG and skills as an **advisory prior**, never an automatic replay. Priors carry the same provenance / visibility / declassification rules as any other memory fact — a private Mission's prior stays private unless a declassification proposal is approved.

#### Scenario: Prior surfaces in planner output
- **WHEN** Brain plans a new Mission under Project `P` with `decomposition_priors_enabled = true`
- **THEN** the planner output records the top-K priors (DAG + skills + outcome) with citations; Brain may consult or ignore them, but the planner MUST NOT auto-replay any prior DAG.

#### Scenario: Private Mission prior is not visible
- **WHEN** Brain plans a new Mission in Project `P'` (different Project) and a prior decomposition lives in a private Mission of Project `P`
- **THEN** that prior is invisible to `P'`'s planner — it stays Mission-private until declassified per the existing rule.

### Requirement: Ephemeral tier flush to platform memory
A worker that runs in the **ephemeral tier** (Worker-tier `ephemeral`, per the modified `worker-runtime` capability) keeps a local scratch space in its container. On successful WorkItem completion the worker SHALL flush a designated set of `EphemeralNote` records into the durable Memory module under the WorkItem's Mission scope (`Scope = Mission, SubjectId = missionId, kind = EphemeralNote`). On lease-lost / reaper-cancelled completion the worker SHALL still attempt the flush if its container is reachable, but the platform SHALL NOT block terminal WorkItem publication on flush failure. The flushed notes carry the WorkItem ID, the worker worker key, the scratch space path, and a content hash; their text body is uploaded to MinIO under the same per-object data-key envelope as other Memory facts (per `data-lifecycle` capability).

#### Scenario: Worker completes and flushes
- **WHEN** an ephemeral-tier worker completes a WorkItem with one or more `EphemeralNote` records in its scratch space
- **THEN** the durable Memory layer records each note as a `Mission`-scope fact under the parent Mission, and the WorkItem's terminal outcome is published with a `flushSummary` listing the recorded fingerprints.

#### Scenario: Lease lost before flush
- **WHEN** an ephemeral-tier worker loses its lease before it can flush
- **THEN** the WorkItem is reported `cancelled` (existing semantics) and the scratch space is wiped on container teardown; no `EphemeralNote` rows are written. A structured log entry names the lost WorkItem so operators can re-mission the notes if needed.
