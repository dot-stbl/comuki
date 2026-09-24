## 1. Architecture Delivery Gates

- [ ] 1.0 Review and approve `architecture.md` as the target project/schema/API/technology map; verify every subsequent child OpenSpec change names which target projects and schemas it creates or modifies.
- [ ] 1.1 Split this umbrella into separately reviewable OpenSpec changes for execution-spine, standalone-work, minimal-missions, mission-room, capability-broker, context-fabric, brain-operations, mission-completion, worker-pools, dashboard, and CLI; verify each change has a vertical user/system outcome and explicit dependency edges.
- [ ] 1.2 Create or update the main architecture context map and bounded-context ownership record for Intake, Work, Missions, Orchestration, Capability Broker, Context Fabric, Brain Operations, and Compute; verify architecture tests can name every allowed dependency direction.
- [ ] 1.3 Add a normative Knowledge spec or record Knowledge exclusion from the first Context Fabric slice; verify no implementation task depends on an unspecified Knowledge contract.
- [ ] 1.4 Produce the migration/cutover matrix for every IntakeTicket, Run, and sync-job state plus minimum rollback versions; verify a review covers upgrade and rollback without duplicate tracker effects.
- [ ] 1.5 Produce the capability inventory mapping semantic capability → owner context → HTTP/UI/CLI/MCP/Brain exposure → permission → object resolver → risk/approval → idempotency; verify every existing mutation has one owner and every hard-denied surface is identified.
- [ ] 1.6 Produce security/privacy decisions for retention, erasure, audit holds, signed URL invalidation, diagnostic reasoning capture, invite/audit TTLs, and multi-node presence backplane; verify production rollout gates no longer depend on an unresolved privacy or topology decision.
- [ ] 1.7 Produce configurable-role and typed-configuration catalogs, including immutable platform-owner recovery and hot-reload/restart/redeploy classification; verify every existing setting and permission maps to one owner and apply path.
- [ ] 1.8 Approve the public module interface and event map in `architecture.md`; verify every command/query/event has one owner, composite reads are projection-only, and no workflow is coordinated by Host sync-call chains.

## 2. Execution Spine Tracer Bullet

- [ ] 2.1 Enforce WorkItem dependency readiness from storage through claim and unblock; verify an integration test proves a dependent item cannot claim before its prerequisite policy is satisfied.
- [ ] 2.2 Reconcile WorkItem terminal outcomes into exactly one parent Run terminal state; verify concurrent final completions publish one terminal result.
- [ ] 2.3 Add execution generation fencing to cancel/supersede, heartbeat, complete, and fail; verify a late worker cannot authoritatively complete after replacement.
- [ ] 2.4 Separate the audit journal, durable terminal outbox, and SignalR delivery projection; verify a committed terminal event survives process failure and is eventually consumed once observably.
- [ ] 2.4a Add the internal event-contract compatibility gate and breaking deployment runbook: additive-reader tests for normal evolution plus drain/reconcile/no-mixed-generation checks for a breaking fixture.
- [ ] 2.5 Close the current Intake claim/admission orphan-Run race with durable idempotent messaging; verify concurrent claims produce one accepted work identity and no orphan execution.
- [ ] 2.6 Remove user-visible `Failed → Queued` Run retry semantics while retaining bounded WorkItem lease retries; verify transition tests and compatibility responses reflect immutable terminal Runs.
- [ ] 2.7 Run the orchestration/translator crown path and prove dependency, terminal, cancellation, outbox, and late-result invariants before enabling Work context.

## 3. Standalone Work Vertical Slice

- [ ] 3.0 Execute the dedicated pre-release hard rename from `Comuki.Modules.Intake.*`/`intake` schema to `Comuki.Modules.Integrations.*`/`integrations`, rename `IncomingTicket` to `InboundItem`, regenerate a clean migration baseline and nested `/api/v1/integration/*` contracts, and verify no compatibility aliases or stale generated clients remain.
- [ ] 3.1 Create the Work bounded context with Task state machine, versioned brief, source link, resolution outcomes, dependency graph, and Run-attempt ledger; verify unit tests cover every legal and illegal transition.
- [ ] 3.2 Connect Work to Orchestration through idempotent outbox/inbox commands and terminal events; verify one Task has at most one active Run under concurrent dispatch/replacement.
- [ ] 3.3 Change native and tracker admission/claim to create or resolve one standalone Task before first Run while retaining compatibility projections; verify webhook replay and claim retry return the same Task.
- [ ] 3.4 Move tracker synchronization to Task resolution with dedupe; verify a failed first attempt followed by successful replacement publishes one final outcome.
- [ ] 3.5 Implement Blocked handling for exhausted attempts and explicit retry, replacement, waiver, failed resolution, and cancellation Decisions; verify no LLM output bypasses the deterministic Task machine.
- [ ] 3.6 Backfill Claimed/active legacy Runs into attempt-1 Tasks and reconcile unsynced terminal Runs; verify the migration matrix against Testcontainers fixtures for every existing state.
- [ ] 3.7 Deliver standalone Task API/dashboard/CLI compatibility reads and commands; verify existing run consumers remain usable while Task is authoritative.

## 4. Minimal Mission Coordination Slice

- [ ] 4.1 Create Mission, goal revision, membership, invitation, audit grant, MissionTaskLink, and lifecycle persistence; verify arbitrary participant count, invitation TTL states, and reopen transitions.
- [ ] 4.2 Add the common Mission access decision for REST and capability reads; verify outsiders receive not-found and API-key actor/credential axes behave independently.
- [ ] 4.3 Implement standalone Task promotion and immutable Task-link required/optional/evidence semantics; verify prior Run/artifact history becomes Mission-private immediately.
- [ ] 4.4 Mediate legacy Task, Run, artifact, signed URL, search, and citation reads through Mission access; verify no project-only route leaks promoted work.
- [ ] 4.5 Implement Task-link dependency and resolution policy, including optional prerequisites and waiver/replacement edge decisions; verify readiness and completion inputs are deterministic.
- [ ] 4.6 Deliver Mission create/list/detail/member/link flows without room or Brain; verify three or more participants can coordinate the same Task set through API-level integration tests.

## 5. Capability Broker Vertical Slice

- [ ] 5.1 Introduce explicit typed capability registration, descriptor/version catalog, actor-bound invocation, stable error contract, and audit record without reflection auto-registration; verify one query and one mutation through direct tests.
- [ ] 5.1a Split Broker execution into lightweight query and durable operation paths; verify ordinary list/get creates no operation row while sensitive/long queries retain full lifecycle/audit.
- [ ] 5.2 Implement common object resolution, current permission checks, four exposure classes, and control-plane hard deny; verify models cannot represent protocol/bootstrap/plaintext/self-expansion calls.
- [ ] 5.3 Implement four-level autonomy resolution, context-owned preflight, approvals, step-up, and current-policy recheck; verify an active operation changes behavior after autonomy/permission revocation.
- [ ] 5.4 Implement durable operations and idempotency ledger with action digest, schema pin, status lifecycle, cancellation, and replay; verify same-key retries cannot duplicate effects and changed input conflicts.
- [ ] 5.5 Move one complete action lane, Task creation, behind the Broker across HTTP, dashboard contract, CLI JSON, MCP projection, and a Brain proposal; verify every adapter produces the same effect and authorization result.
- [ ] 5.6 Add OpenAPI metadata/projection and CI parity reporting without deriving authority from HTTP verbs/routes; verify drift fails when an exposed adapter and capability contract diverge.
- [ ] 5.7 Inventory and migrate further commands/queries in small context-owned slices, starting with Task/Run reads and dispatch/cancel; verify no old direct mutation path bypasses the Broker before marking each capability migrated.
- [ ] 5.8 Measure repeated capability metadata after the first migrated slices and, only if duplication is proven, add a bounded manifest/source generator for mechanical DI/schema/OpenAPI/MCP projections; verify generated output is inspectable and policy/execution remain owner-authored.

## 6. Durable Mission Room Slice

- [ ] 6.1 Implement the Mission single-writer append log with inbox dedupe, gapless committed sequence, UUIDv7 message idempotency, versioned envelopes, and cursor reads; verify concurrent writers yield one order and mismatch reuse conflicts.
- [ ] 6.2 Add participant messages, immutable edits, tombstones, version-bound citations, per-user read cursor, mentions, and pending actions; verify deleted content cannot reappear in ordinary retrieval.
- [ ] 6.3 Add SignalR Mission join/push plus REST catch-up and unknown-event compatibility; verify disconnect/reconnect has no gaps or duplicates.
- [ ] 6.4 Add invitation request/accept/reject/revoke/expire and reasoned read-only admin audit sessions; verify safe preview, TTL, step-up, participant notice, and access revocation.
- [ ] 6.5 Add the first proposal/Decision lane to the Mission stream using Broker Task creation; verify lane versions are independent of chat sequence and conflicting proposals re-evaluate serially.
- [ ] 6.6 Deliver a minimal Mission room in dashboard and `comuki mission <id>` TUI using the same envelope; verify at least three participants see identical ordered history.

## 7. Context Fabric Foundation Slice

- [ ] 7.1 Introduce versioned SourceRef, derived-item provenance, visibility labels, authority dimensions, and content hashes; verify exact message, Decision, Task, Run event, artifact, and procedure sources resolve.
- [ ] 7.2 Add narrow read adapters for Missions, Work, Runs, Artifacts, Memory, and control-plane procedures without moving source ownership; verify module dependency tests preserve sibling isolation.
- [ ] 7.3 Implement Context Pack manifest/sections, access-aware cache key, adaptive token budgeting, omissions, handles, and PostgreSQL metadata plus MinIO payload storage; verify simple queries stop below max while hard research expands without irrelevant padding.
- [ ] 7.4 Implement deterministic exact/current-state retrieval, relationship expansion, source diversity, and authority-aware ranking before semantic retrieval; verify golden retrieval includes current authoritative state and excludes superseded/private sources.
- [ ] 7.5 Add derivation invalidation, source watermarks, active embedding/extractor generations, and rebuild scheduling; verify changed sources stale all dependent projections without deleting audit history.
- [ ] 7.6 Add structured working, episodic, semantic, and procedural projections; verify intent/reality conflicts, disputed claims, and source citations remain distinct.
- [ ] 7.7 Add Project memory Candidates, independent-evidence promotion, conflicts, and Mission declassification proposals; verify private Mission content cannot enter Project memory without approved redaction.
- [ ] 7.8 Add retrieval/compaction/security eval suites for recall, authority, staleness, citation precision, visibility leaks, budget compliance, and repeated-summary drift; verify CI records the quality/cost frontier.
- [ ] 7.9 Keep Context search behind a dedicated typed query/result seam and implement PostgreSQL FTS + pgvector generation/rank fusion inside Infrastructure; verify Filtered operators cannot masquerade as semantic search and real Postgres plans use the intended indexes.

## 7A. Filtering DSL Maintenance

- [ ] 7A.1 Keep `Comuki.Shared.Filtering` as the local adapted canonical list-filter/sort DSL and document that it excludes locks, claims, FTS, vectors, and ranking; verify no cross-repository or broad SDK package dependency is introduced.
- [ ] 7A.2 Compare the current Comuki port with `Hybrid.Sdk.Shared.Filtering` and selectively backport only consumer-needed null safety, invariant case folding, nullable date, range/negated operators, typed-id, or boundary-date fixes while preserving injected `TimeProvider`; verify unit and real PostgreSQL suites cover each adopted behavior.
- [ ] 7A.3 Add an architecture/integration test matching every exposed filterable field to the EF model and an explicit sensitive-field policy; verify ignored, credential, token, hash, and unsupported properties cannot become filterable/sortable by adding a public property.

## 8. Durable Brain Operations Slice

- [ ] 8.1 Introduce Brain operation/checkpoint persistence, typed operation stream parts, usage budgets, catalog pins, and tool-call ledger while preserving current chat final-result compatibility; verify restart resumes without repeated committed calls.
- [ ] 8.2 Move context assembly to one Context Fabric compiler and replace caller-built duplicate digests with Context Pack handles; verify every invocation records one reproducible manifest.
- [ ] 8.3 Add answer-only Mission Brain using visibility-scoped context, manifest, citations, and expandable concise rationale; verify `@comuki` reads the room but ordinary team messages do not invoke it.
- [ ] 8.4 Add proposal staging through Capability Broker under Observe/Suggest/ActSafe/Autopilot; verify Brain never receives credentials/system scope and every effect rechecks current policy.
- [ ] 8.5 Implement child Brain operations as a depth-two, fan-out-four budgeted DAG with dedupe, cancellation, deadlines, and reserved synthesis budget; verify cycles, ancestor waits, and grant widening are rejected.
- [ ] 8.6 Implement worker-backed read-only research intents and evidence return without automatic Mission Task creation; verify filesystem research occurs only under a worker lease and isolated workspace.
- [ ] 8.6a Add Orchestration `ExecutionRequest` origins for WorkTask, BrainResearch, Verification, Discovery, ScheduledJob, and Maintenance; verify Brain research links to operation/delegation trace and never creates a hidden WorkTask.
- [ ] 8.7 Implement claim/evidence synthesis with authority/freshness/conflict handling; verify model majority cannot override current state or accepted intent.
- [ ] 8.8 Add deployment-only diagnostic reasoning capture with encryption, allowlist, retention, redaction, audit, and exclusion from future retrieval; verify default operation retains no hidden chain-of-thought.

## 9. Mission Completion Slice

- [ ] 9.1 Add evidence generations, completion-review dedupe key, durable coalescing lease, and explicit request for zero-required Missions; verify concurrent resolution events enqueue one review.
- [ ] 9.2 Build completion Context Packs from current goal, criteria, Task-link resolutions, Decisions, Run reports, and artifacts; verify stale evidence watermarks invalidate effect but preserve findings.
- [ ] 9.3 Add Brain proposals for Completed, questions, or follow-up Tasks with citations and optional-work disposition; verify completion never silently cancels or continues optional Runs.
- [ ] 9.4 Add authorized completion, disagreement/recalculation, new revision, and reopen Decisions; verify prior completion history remains immutable and new work returns Mission to Active.
- [ ] 9.5 Add concise accepted-decision/tracker synchronization through Task/Mission resolution outbox; verify private transcript is never mirrored and retries do not duplicate comments.

## 10. Realtime Presence and Notification Slice

- [ ] 10.1 Implement `IRealtimeBackplane` with InMemory single-node and Redis multi-node adapters, topology startup validation, and durable-only degradation; verify unsafe multi-node InMemory fails readiness and Redis outage never returns partial roster as complete.
- [ ] 10.2 Implement multi-connection online roster and expiring typing state outside Mission sequence; verify dashboard and CLI connections aggregate as one actor and crash cleanup occurs by TTL.
- [ ] 10.3 Implement attention inbox for mentions, invitations, approvals, blocking failures, and completion reviews; verify ordinary messages/worker progress do not fan out notifications by default.
- [ ] 10.4 Add service-account presence/audit behavior without counting automation as human approvals; verify human and automation actors render distinctly.

## 11. Warm Worker Pool Slice

- [ ] 11.1 Evolve contracts to WorkerHostId, SlotId, ExecutionId, AgentSessionId, and fencing generation while maintaining a single-slot compatibility mode; verify two slot streams no longer replace one another.
- [ ] 11.2 Implement host capability/resource advertisement and provider hard slot maximum; verify placement rejects incompatible profile/model/tool/resource combinations.
- [ ] 11.3 Implement isolated slot workspaces, credentials, processes, logs, cancellation, and cleanup; verify concurrent Tasks against one repository cannot see each other's mutable files.
- [ ] 11.3a Implement trusted cross-Project process slots plus profile/Project-selected strong sandbox drivers (Docker/Podman sibling container, Kubernetes separate Pod/Job, standalone advertised equivalent); verify strong requests never downgrade, mutable state/credentials do not cross slots, and compromise quarantines all host executions.
- [ ] 11.4 Implement slot-level claim/heartbeat/complete/fail and host-failure lease recovery; verify a crashed 20-slot host reaps each WorkItem independently.
- [ ] 11.5 Replace create-per-task scale policy with deterministic Capacity Planner over queue, idle slots, quotas, resources, budgets, priority, and deadline; verify hard limits override desired parallelism.
- [ ] 11.6 Add Brain/operator desired-parallelism capability under Compute-owned risk/autonomy policy; verify in-quota Autopilot scaling and out-of-quota proposal behavior.
- [ ] 11.7 Validate Docker and Kubernetes adapters against the same host/slot semantics and drain behavior; verify provider switching does not change Work/Brain contracts.

## 12. Dashboard and CLI Product Parity

- [ ] 12.1 Deliver Mission list/detail with goal revisions, participants, Task links/graph, Decisions, artifacts, operations, and unified stream; verify permission-denied objects remain undiscoverable.
- [ ] 12.2 Deliver Team/Comuki/Propose composer, citations, Context Pack manifest, expandable subbrain/research trace, proposal diff/risk/approval cards, and completion UX; verify clients render server policy rather than maintaining a duplicate permission map.
- [ ] 12.3 Deliver `comuki mission <id>` TUI with the same durable stream, attention inbox, presence, reconnect catch-up, cancellation, and typed high-risk confirmation; verify snapshot/reducer tests cover offline and stale-operation states.
- [ ] 12.4 Deliver non-interactive Mission list/show/create/send/ask/proposal/member/task/events commands with stable JSON and exit codes and no prompts; verify contract tests against generated server schemas.
- [ ] 12.5 Preserve bare `comuki` personal ChatSession behavior while migrating personal actions to Capability Broker/Brain operations; verify existing session restore and approval compatibility.
- [ ] 12.6 Create the generated `comuki-client-contracts` TypeScript package from the shared OpenAPI/stream/capability manifests and migrate dashboard Kubb adapters plus CLI REST/SignalR models onto it; verify a contract drift fixture fails both client builds together.
- [ ] 12.7 Migrate TS projects into one root Bun workspace and lockfile with `platform/generated/ts` as a full generated package; verify one frozen install and root typecheck/test/codegen scripts cover agents, dashboard, generated contracts, and the current CLI without starting its deferred rewrite.

## 12A. Versioned API Reference

- [ ] 12A.1 Add Asp.Versioning.Mvc + ApiExplorer and migrate operator routes to `/api/v1/{singular-category}/{plural-resource}` nested grammar; verify no hyphenated resource segments remain and route constraints/operation ids are stable.
- [ ] 12A.2 Keep Microsoft.AspNetCore.OpenApi and add XML docs, versioned documents, security/ProblemDetails/capability transformers, and typed examples; verify strict OpenAPI contains every operator endpoint and excludes runtime/internal/bootstrap catalogs.
- [ ] 12A.3 Add Scalar.AspNetCore at `/api/description` with Comuki styling, version selector, raw `/api/description/{version}/openapi.json`, public-by-default typed configuration, and auth/disable modes; verify deprecated versions render sunset metadata.

## 13. Crown Verification and Rollout

- [ ] 13.1 Build the crown E2E with at least three human participants: create Mission, invite/accept, concurrent chat, `@comuki`, cited Task proposal, approval, Run/worker execution, completion review, and accepted completion; verify dashboard and CLI contracts observe one stream.
- [ ] 13.2 Extend crown E2E with CLI disconnect, intervening events, REST sequence catch-up, group rejoin, and idempotent retry; verify no gaps or duplicates.
- [ ] 13.3 Add adversarial authorization tests for outsider Run/artifact/search access, API-key actor/credential split, service account, admin audit TTL, distinct-human approvals, autonomy lowering, and Brain hard deny; verify all denial paths are stable and non-disclosing.
- [ ] 13.4 Add concurrency/load tests for arbitrary participant counts, stream writer contention, outbox lag, proposal lanes, summary coalescing, subbrain fan-out, reconnect storms, and warm slot placement; publish measured limits and alerts.
- [ ] 13.5 Add operational dashboards/runbooks for outbox/inbox lag, reconciliation/dead letters, Brain operation budgets, Context Fabric stale generations, presence backplane, worker-host slots, and declassification audit; verify incident drills have recovery steps.
- [ ] 13.6 Roll out behind project feature flags by epoch, record minimum rollback version at each gate, and remove compatibility projections only after telemetry shows no legacy consumers.

## 14. Configurable Identity and Platform Settings

- [ ] 14.1 Replace fixed non-break-glass roles with versioned configurable definitions over stable permission keys; verify historical grants/Decisions retain their role-definition version.
- [ ] 14.1a Implement permission delegation metadata and `own ∩ delegable ∩ scope ∩ policy` validation plus immediate tightening/explicit widening migration; verify privilege escalation and silent grant widening are rejected.
- [ ] 14.2 Add immutable human `platform-owner` recovery, last-owner and orphaned-Mission guards, step-up, and audited owner recovery; verify role edits cannot lock out the deployment.
- [ ] 14.3 Add configurable Mission role bundles and service-account automation roles while preserving a human owner-capability invariant; verify service actors never satisfy human approvals.
- [ ] 14.4 Build the typed configuration catalog with scope, source precedence, validation, sensitivity, impact, and hot-reload/restart/redeploy classes; verify every catalog item has an owner capability.
- [ ] 14.5 Route typed configuration reads/diffs/preflight/apply/rollback through Capability Broker and Brain proposals; verify live options reload only for declared hot-reload settings and other changes remain staged until applied.

## 15. Templates and Verification Contracts

- [ ] 15.1 Create the Templates capability with Project/Platform scopes, immutable versions, lifecycle, provenance, and promotion; verify module boundaries remain independent from Missions/Work implementations.
- [ ] 15.2 Define Mission and Task template schemas that separate work shape from pinned profiles/rules/skills and declare capabilities/resources/secret metadata; verify plaintext values cannot be stored.
- [ ] 15.3 Implement template schema/DAG/compatibility validation, no-side-effect dry-run, and eval publication gate; verify invalid dependencies or missing capabilities block publication.
- [ ] 15.4 Add instantiation binding and Brain/human template selection under autonomy policy; verify instances pin versions and missing requirements produce actionable preflight.
- [ ] 15.5 Add semantic template migration proposals with goal/criteria/Task impact maps; verify active instances never auto-update.

## 16. Retention, Holds, and Crypto-Shred

- [ ] 16.1 Implement class-aware retention policy/profiles and conservative self-hosted defaults; verify Mission/chat/operations, audit, artifacts, reasoning, and presence calculate distinct outcomes.
- [ ] 16.2 Introduce per-object data-key encryption for deletable bodies/artifacts and propagate deletion to embeddings, summaries, caches, Context Packs, and object storage; verify backup ciphertext is unreadable after key destruction.
- [ ] 16.2a Store encrypted objects by random UUIDv7 id and allow dedupe only inside one visibility/lifecycle scope using plaintext hash metadata; verify equal content in two private Missions has independent ciphertext/DEKs and deleting one does not affect the other.
- [ ] 16.3 Implement terminal eligibility plus explicit delete/automation Decision, active-work guards, minimal audit tombstones, and all-Mission-artifact preservation until Mission deletion; verify active Mission/Task cannot be shredded.
- [ ] 16.4 Implement platform-owner holds with scope, reason, reviewAt, critical overdue attention, release/extension, and no read-access widening; verify retention sweeps skip held data.
- [ ] 16.5 Implement deployment-only diagnostic reasoning capture with allowlist, encryption, redaction, seven-day default TTL, and exclusion from retrieval; verify disabled default stores no hidden reasoning.

## 17. Project Webhook Integrations

- [ ] 17.1 Add Project-scoped subscription management with event/Mission/payload/version scope, opaque signing refs, HMAC timestamp/id, rotation, and safe destination validation; verify SSRF/rebinding/redirect protections.
- [ ] 17.2 Add safe metadata payloads and dual-consent full-content payloads with policy redaction and stable artifact ids; verify plaintext secrets and transferable signed URLs never leave Comuki.
- [ ] 17.3 Deliver webhooks from durable outbox with per-subscription+Mission ordering, dedupe, bounded retry, dead-letter/manual replay, schema majors, and delivery audit; verify a poison event releases only its partition with explicit gap.
- [ ] 17.4 Enforce consent revocation by cancelling and crypto-shredding pending/retry full-content payloads; verify already-delivered history remains honest and future events downgrade/stop.
- [ ] 17.5 Add causation chains, hop limits, own-origin exclusion, and duplicate fingerprints across webhooks and service-account messages; verify feedback loops are suppressed.

## 18. Memory Scope and Global Learning

- [ ] 18.1 Add user-private memory and explicit personal remember/share semantics; verify private claims never enter shared Mission Context Packs without consent.
- [ ] 18.2 Integrate revisioned Knowledge as untrusted evidence and control-plane git as canonical procedural knowledge; verify documents cannot inject instructions and template/procedure roles remain distinct.
- [ ] 18.3 Add explicit Project opt-in for cross-project learning, redacted procedure/failure candidates, independent-Project threshold, privacy checks, eval, and platform curation; verify default deployments share nothing across Projects.
- [ ] 18.4 Implement provenance withdrawal and confidence recomputation for global claims; verify one Project cannot delete independently supported knowledge and under-supported claims become disputed/withdrawn.

## 19. Extended Work Coordination

- [ ] 19.1 Add multiple Task source refs, Decision-controlled primary change, primary lifecycle sync, and related-source key-decision/final summaries; verify no source receives duplicate intermediate spam.
- [ ] 19.2 Add responsible human/service actors as attention metadata without authorization effects and Brain assignment rules; verify human assignments require proposals and service assignments honor grants/capacity.
- [ ] 19.3 Add cross-Mission blocking edges with dual-access creation and redacted dependency stubs; verify private upstream content does not leak.
- [ ] 19.4 Add Mission/Task creation, activation, cancellation, completion, deletion, orphan recovery, and goal-revision impact maps; verify every active Task receives explicit disposition.
- [ ] 19.5 Add Task completion policies with declared evidence contracts, deterministic/Brain-assisted/human reviewers, reviewer separation, and repair proposals; verify successful Run plus failed verification blocks Task without rewriting Run history.
