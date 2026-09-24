## Context

See `proposal.md` for motivation and `specs/` for normative behavior. Today the product has three overloaded roots: subject-owned `ChatSession`, ingress `IntakeTicket`, and `Run` as both user goal and technical execution. Cross-module actions are host-composed synchronous calls across separate DbContexts; run realtime is a best-effort post-save interceptor, not a durable integration bus. Brain has a small private toolbox, MCP has another catalog, chat has a third executor, and UI/CLI call HTTP directly. Context is a string digest plus recent messages, while workers are modeled as one identity/stream per container.

The design must preserve the repository's modular-monolith law: sibling modules do not reference implementations, domain state remains module-owned, and the host composes adapters. It must also preserve “LLM proposes, system disposes”: neither Brain nor subbrain becomes authority, durable state, filesystem runtime, or credential holder.

The physical target structure, project graph, schemas, API/codegen pipeline,
and technology choices are specified in `architecture.md`.

## Goals / Non-Goals

**Goals:**

- Separate collaborative intent, durable work, execution attempts, and worker invocations.
- Make all semantic commands and queries use one authorization/policy/idempotency plane.
- Give Brain effectively broad platform ability without raw HTTP, credentials, or system scope.
- Make product-scale context addressable, provenance-backed, visibility-safe, and bounded per invocation.
- Support durable Brain operations, bounded subbrains, and worker-backed research.
- Support arbitrary Mission participant counts and warm worker hosts with isolated slots.
- Define reliable cross-context delivery and an explicit migration from `IntakeTicket → Run`.

**Non-Goals:**

- A global event-sourcing rewrite.
- A distributed transaction across module stores.
- Exposing every HTTP route, worker protocol, bootstrap operation, or secret value as a model tool.
- One shared mutable prompt or memory store owned by agents.
- Immediate delivery as one release. This change is an architecture umbrella split into vertical changes before implementation.

## Decisions

### 1. Three business roots, not one Mission god-context

```text
Intake context            Work context                Missions context
IncomingTicket ─admit──▶ Task ◀── MissionTaskLink ─── Mission
                           │                           ├─ members
                           ├─ Run attempts             ├─ goal revisions
                           ├─ dependencies             ├─ room stream
                           └─ resolution               └─ proposals/decisions
                                │
                                ▼
                    Orchestration context
                       Run → WorkItems
```

**Choice:** create a Work bounded context owning standalone Task, source links, Task lifecycle/resolution, dependencies, Run-attempt ordering, and the one-active-attempt invariant. Missions owns Mission, membership, revisions, deliberation, stream, and `MissionTaskLink` with required/optional and Mission-local evidence semantics. Orchestration keeps Run/WorkItem mechanics.

**Why:** a Task remains the same object before and after promotion and is useful without collaboration. Making Missions own standalone Tasks would force every intake, scheduler, and execution path through an optional collaboration module. Making Orchestration own Tasks would mix user-visible work policy with lease mechanics.

**Rejected:** `MissionTask` as a Mission child; repurposing `WorkItem` as Task; keeping `Run` as goal.

### 2. State machines are deterministic; Brain proposes transitions

```text
Task status:
Draft → Ready → Active ↔ Blocked → Resolved
   └───────────────→ Cancelled

Task resolution outcome (only with Resolved):
Succeeded | Waived | Replaced | Failed

Mission:
Draft → Active → Review → Completed
  └──────────────→ Cancelled
Completed --approved new revision/reopen decision--> Active
```

Run terminal states are immutable. User-visible retry creates another Run attempt. Claim retries remain WorkItem mechanics. Exhausted attempts block the Task; a Decision chooses retry budget, replacement, waiver, or failed resolution. Completion review uses Mission evidence generations, not “all child statuses are terminal.”

**Why:** the LLM can assess evidence but must not invent legal transitions. Separate status and resolution avoid encoding waiver/replacement as ad hoc booleans.

### 3. Cross-context changes use local outbox/inbox, never dual writes

Each context commits its aggregate and local outbox atomically. Consumers have inbox dedupe keyed by message id, apply expected-state/version guards, and publish their own next event. Delivery is at least once; effects are exactly-once-observable through idempotent state transitions. Reconciliation detects stuck outbox, poison/dead-letter messages, and missing projections.

Example:

```text
Intake claim commit
  ticket admitted + Work.AdmitTask command in intake outbox
       ↓ at-least-once
Work inbox
  create/find Task + TaskAdmitted event
       ↓
dispatch policy → Orchestration.StartRun command
       ↓
Run terminal outbox → Work resolution → Mission stream/completion trigger
```

The commit point belongs to the context owning the changed fact. Synchronous ports may optimize request/response but cannot be the durability contract.

**Rejected:** ambient distributed PostgreSQL transaction across DbContexts; compensating a chain of synchronous host calls; treating `run_events` or SignalR interceptor as an outbox.

Integration event types belong to the publisher's Application contract and use
`<context>.<aggregate>.<past-tense>.v<major>` names. Evolution within a type is
additive; breaking evolution publishes a new major event name and migrates
consumers across both types because queued outbox payloads outlive process
binaries. Public/webhook schemas remain independently versioned.

Choreography is the default. Durable owner-specific process managers coordinate
only flows that need a returned identity, timeout, retries, compensation, or
multi-step state. Integrations owns admission until Task identity is confirmed;
Work owns Task execution attempts; Missions owns completion review; Engine
Platform Brain owns subbrain/research operations. No generic saga engine is
introduced.

### 4. Capability Broker is a shared platform capability and mandatory semantic seam

```text
HTTP / UI / CLI / MCP / Brain / automation
                     │ adapters
                     ▼
             Capability Broker
   catalog · actor · object policy · autonomy
   approval · idempotency · operation · audit
                     │
           typed context capability
                     ▼
     Work / Mission / Compute / Identity / ...
```

Every semantic command and query has a versioned descriptor, typed input/output, exposure class, target resolver, and context-owned policy/executor. The broker owns only common invocation machinery. HTTP/OpenAPI is a projection and schema/parity source, not execution authority. Curated composite capabilities orchestrate ordinary capabilities without privileged handlers.

Registration starts as explicit typed module declarations. Bounded
auto-registration is allowed when it preserves an inspectable manifest and an
explicit opt-in contract. Source generation may later project stable
declarations into DI registrations, JSON contexts, OpenAPI extensions, Brain
schemas, MCP catalogs, and TypeScript metadata. Reflection may inspect a known
module at startup, but it does not infer authorization, policy, lifetime, or
execution from naming conventions. The threshold is demonstrated repetition,
not a blanket ban or a generator-first framework.

Capability exposure classes:

1. `operator`: ordinary human/Brain-exposable semantics;
2. `internal-allowlisted`: platform allowlist, project-admin may enable a subset;
3. `runtime-protocol`: worker lease/heartbeat/stream plumbing, never Brain-exposed;
4. `bootstrap-secret`: bootstrap, policy/grant mutation, and plaintext secret reveal, never Brain-exposed.

Brain cannot change its autonomy maximum, exposure allowlist, approval policy, or grants. Those are non-Brain control-plane actions.

**Why:** direct OpenAPI tools are transport-shaped, too broad, and inherit current endpoint authorization bugs. Another manual Brain catalog would drift. A broker creates one deep interface while each context keeps domain rules local.

### 5. Actor identity has four axes

Invocation records separate:

- `ActorId`: human or service account responsible for authorship/membership;
- `CredentialId`: optional API key/session credential;
- `AuthorizationSubject`: user, key, or service account whose project grants apply;
- `ActorKind`: human, service-account, brain, worker, system.

Mission membership checks ActorId. Capability permission checks AuthorizationSubject. Audit stores all axes. Multi-person approvals count distinct human ActorIds, never credentials or service accounts.

Mission access is a dedicated object policy used by all REST, SignalR, retrieval, notification, Task, Run, and artifact adapters. It is not added to project-only ambient `SubjectScope`. Project-admin audit access is a short read-only grant with reason, step-up, TTL, participant notification, and audit.

### 6. Four-level autonomy is policy input, not model authority

```text
Observe    answer/read only
Suggest    answer + proposals
ActSafe    auto-execute context-classified low-risk actions
Autopilot  execute Brain-exposable actions under configured per-capability policy
```

Project policy defines the maximum. Mission chooses a default no higher than it. An authorized owner can approve a scoped/expiring increase; any participant can lower one request. Context-owned policy sets approvals `0/1/2`, step-up, budgets, and risk conditions for Brain-exposable capabilities. Policy and permission are rechecked before every effect, so lowering autonomy or revoking a grant affects active operations.

Classes 3–4 remain hard-denied regardless of Autopilot. This is the irreducible platform safety boundary; “project-admin configures everything” applies only inside the Brain-exposable catalog.

### 7. Mission stream is a single-writer projection, not global event sourcing

Mission owns one append log. Cross-context events arrive through an idempotent inbox. The writer dedupes first, allocates `nextSequence`, and appends in one transaction; committed stream sequences have no gaps. Sequence is transport ordering only.

Business guards are separate:

- Mission goal revision;
- Mission Task-link set version;
- Task execution generation;
- proposal lane version;
- membership/policy version;
- completion evidence watermark.

An ordinary message changes stream sequence but does not stale a Task proposal. Payloads are versioned discriminated envelopes; unknown kinds are safely ignored/fetched by older clients.

### 8. Context Fabric is a shared capability with narrow source adapters

Context Fabric owns universal `SourceRef`, provenance/derivation graph, Context Pack manifests, retrieval plans, indexes/projections, cache/invalidation metadata, and Brain invocation evidence handles. It does not own source aggregates. Chat, Missions, Work, Runs, Artifacts, Knowledge, Memory, control-plane git, and capability queries expose narrow versioned read adapters.

```text
canonical sources
      ↓ adapters
Context Fabric
  source catalog
  working/episodic/semantic/procedural projections
  provenance + invalidation
      ↓
Retrieval Planner
      ↓
adaptive immutable Context Pack
      ↓
Brain operation / worker brief
```

Context Packs are adaptive up to the model limit. Planner stops when evidence is sufficient and expands when gaps remain; it never fills unused capacity with irrelevant corpus. Large evidence is a content-hashed handle with preview and range reads. Manifest, access hash, watermarks, policy/retrieval/model generations, omissions, and token use make packs reproducible and safe to cache.

Metadata and searchable manifests live in PostgreSQL; immutable pack sections and large payloads live in MinIO by content hash.

**Rejected:** always fill a 1M prompt; Memory module as owner of all sources; Brain assembling arbitrary prompts itself; global event log rewrite.

### 9. Memory is layered and declassification is explicit

- Working context: current Mission goal, criteria, active work, blockers, questions.
- Episodic: causal Mission/Task/Run episodes derived from canonical events.
- Semantic: structured claims with validity, confidence, provenance, current/superseded/disputed state.
- Procedural: versioned profiles, rules, skills, capability contracts, and eval links.
- Audit source: canonical immutable/versioned records.

Mission-private derived content stays private. A classifier may produce a redacted Project-memory Candidate, but publication is a declassification proposal showing exact text, provenance disclosure, and audience. Accepted Decision or independent confirming sources may promote non-conflicting Candidates under policy. Contradictions create conflict nodes; they never overwrite silently.

Accepted human intent and observed runtime/code reality have different authority dimensions. Brain reports divergence rather than selecting one “truth.”

### 10. Brain work is durable and subbrains are bounded evidence producers

Brain operation states include context planning/packing, model waiting, staged/committed tool calls, delegating, synthesis, validation, and terminal outcomes. A call ledger makes effects replay-safe. Operation schemas are pinned; policy and exposure are current.

Subbrains are child Brain operations:

- delegation graph depth ≤ 2;
- default fan-out ≤ 4;
- narrow Context Pack and delegated capability/source grant;
- reserved token/cost/time budgets and protected synthesis reserve;
- children cannot commit mutations, wait on ancestors/siblings, or own shared memory;
- duplicate delegation fingerprints collapse;
- final synthesis merges claims/evidence, not answer strings or majority votes.

Brain and subbrain have no filesystem/process access. Repository or tool research creates a bounded read-only research WorkItem. Its evidence returns to the child/parent operation and may create selective memory Candidates; it does not automatically become a visible Mission Task.

Operation cancellation is cooperative and preserves trace/evidence while blocking new effects. Context watermarks are checked before final synthesis and every mutation; stale analysis becomes a new invocation revision or evidence only.

Normal audit stores evidence, tool/delegation trace, concise rationale, Decisions, and usage. Full hidden reasoning capture is deployment-config-only diagnostics with explicit retention/redaction; it is not product memory.

### 11. Warm worker hosts contain independently fenced slots

```text
WorkerHost
  advertised profiles/models/tools/resources/hardMaxSlots
  ├─ Slot A → Execution generation → AgentSession → isolated workspace
  ├─ Slot B → Execution generation → AgentSession → isolated workspace
  └─ Slot N → ...
```

The lease owner is execution/slot generation, not host alone. Each slot has separate workspace, checkout/worktree, context handles, execution-only credentials, process, stream, logs, cancellation, and cleanup. Shared immutable caches are permitted. Host failure expires each slot independently; reaper decisions and retries remain per WorkItem.

Self-hosted Projects belong to one trusted deployment zone by default. A Docker
worker container, Kubernetes worker Pod, or standalone runner may therefore host
process slots from several Projects. Strong isolation is a profile/Project
requirement, not the universal baseline: Docker/Podman uses a sibling execution
container, Kubernetes uses a separate execution Pod/Job, and a standalone runner
must advertise an equivalent driver. A runner without one can serve only
`trusted-process` work. The worker never receives a raw provider/Docker socket.

Brain submits desired-parallelism intent with priority/deadline/cost preference. Deterministic Capacity Planner uses queue, advertised compatibility, project quotas, provider resource limits, and budget to choose placement and hosts. Brain never calls a provider directly. Effective autonomy and Compute policy decide automatic scale versus proposal.

### 12. Realtime presence is ephemeral but multi-node-correct

Durable Mission stream remains REST-authoritative with at-most-once SignalR push and cursor catch-up. Presence and typing use `IRealtimeBackplane`, aggregate multiple connections per actor, expire through TTL, and never consume Mission sequence. Deployment config selects `InMemory` for declared single-node topology or `Redis` for multi-node topology; unsafe multi-node InMemory fails startup/readiness. Redis outage degrades to durable-only polling and marks presence unavailable instead of returning partial truth.

Cross-context durability remains PostgreSQL-only for the first release: local outbox/inbox dispatchers claim with `FOR UPDATE SKIP LOCKED`, target sub-second ordinary latency, and order causally per aggregate. Bounded retries lead to visible dead-letter state/manual replay without blocking unrelated aggregate partitions. No message bus is introduced until measured throughput or external-consumer requirements justify a second transport adapter.

### 13. Dashboard and CLI are equal adapters

Dashboard presents two projections: Mission Workspace (Task board by default, dependency graph/table/timeline alternatives) and Mission Room as a distinct shared conversation alongside personal chats. Both consume the same Mission stream and capability descriptors. Bare `comuki` remains personal chat. `comuki mission <id>` opens the shared conversation with Team/Comuki/Propose composer modes, linked Task summary, expandable operation trace, citations, proposal cards, presence, reconnect catch-up, and typed confirmation. Non-interactive commands expose stable JSON and exit codes and never prompt.

Neither client owns act-to-permission mapping. They render server policy/preflight; the broker remains authoritative.

One generated TypeScript contract package is the source for both clients:

```text
C# HTTP + stream + capability contracts
                  ↓ build-time OpenAPI/manifest emit
agents/comuki-client-contracts (generated TS, no runtime policy)
                  ├─ dashboard generated API/query adapters
                  └─ cli typed REST/stream/capability client
```

The generated package contains request/response types, operation ids, Mission
stream unions, ProblemDetails codes, capability metadata, and safe decoders for
unknown future event kinds. Dashboard may continue generating TanStack hooks
with Kubb; CLI consumes the same base types/client and adds command/TUI logic.
No generated client embeds permissions or makes policy decisions.

### 14. Typed configuration makes the platform Brain-operable

Every manageable setting is a typed capability with schema, scope, source precedence, sensitivity, validation, impact, and apply class:

- `hot-reload`: live through the supported options monitor/configuration source;
- `restart-required`: staged desired state plus controlled process restart;
- `redeploy-required`: staged plan executed by deployment adapter or emitted as exact operator instructions.

Brain may read/explain/validate/propose the whole non-secret catalog and apply changes under autonomy/policy. It cannot pretend all .NET options hot-reload: DI topology, transports, connection strings, replicas, and external resources remain restart/redeploy operations. Config history is versioned; rollback is another revision.

Effective precedence is `compiled defaults < configuration file < managed DB
revision < environment variables < CLI startup overrides`. Brain changes the DB
desired revision. If env/CLI wins, clients show the overriding source and the
restart/redeploy plan rather than claiming the DB value is active.

Permissions are canonical capability contracts; roles are self-hosted configurable versioned bundles. A minimal built-in human `platform-owner` remains immutable for break-glass recovery. Mission room roles are configurable bundles initialized from owner/editor/commenter defaults while the invariant “at least one active human with owner capability” stays fixed. Brain can draft role/grant diffs but Identity requires explicit human approval/step-up and never allows self-expansion.

### 15. Templates describe work shape, control-plane describes execution

A dedicated Templates capability stores Project and curated Platform Mission/Task templates. Mission templates define goal/criteria/Task-link skeleton/completion/autonomy/resource requirements; Task templates define brief schema, compatible profiles, evidence/verifier/resource defaults. Profiles/rules/skills remain versioned procedural authority in control-plane git/content hashes.

Templates declare required capabilities/secrets but bind opaque references at instantiation. Draft → review → published → deprecated publication requires schema/DAG checks, no-side-effect dry-run, compatibility checks, and configured evals. Instances pin versions; updates become semantic migration proposals with impact maps, never automatic mutation.

### 16. Retention is a class-aware policy and deletion is cryptographic

`RetentionPolicy.Evaluate(metadata)` computes eligibility from resource class, terminal time, deployment profile, pins, and hold. Conservative defaults: Mission/chat/operations 365 days after terminal, audit/Decisions seven years, all Mission artifacts until Mission deletion, diagnostic reasoning seven days, presence never durable. Terminal eligibility plus explicit authorized delete/automation Decision triggers deletion.

Bodies/artifacts/derived indexes use per-object data keys. Crypto-shred destroys keys and materialized content while retaining minimal audit metadata; backup ciphertext becomes unreadable immediately and physical blocks age out normally. Platform-owner holds carry scope, reason, creator, reviewAt, do not expand read access, and require explicit release/extension. Active work cannot be deleted; cancellation/completion impact comes first.

### 17. Outbound webhooks are scoped Project integrations

Project-admin subscriptions define destination/event/Mission/payload/schema scope and opaque signing secret. Safe metadata is default; full private bodies require both project-admin setup and Mission-owner consent. Payload is redacted before HMAC signing; artifact references are stable ids, not signed URLs. SSRF protection validates redirects and DNS/CIDRs with explicit internal allowlists.

Delivery uses outbox at-least-once ordering per subscription+Mission, dedupe ids, bounded retries, dead-letter/manual replay, and correlation/hop-loop suppression. Revoking consent cancels and crypto-shreds pending/retry full-content payloads. External notification channels remain consumers of the durable attention/integration surface rather than Mission-owned transport logic.

### 18. Knowledge, memory scopes, and global learning remain distinct

Knowledge remains a source-owned revisioned document/chunk corpus and enters Context Fabric as untrusted evidence, never instructions. Control-plane remains procedural authority. User memory is private and excluded from shared Mission packs absent explicit one-shot/share consent. Mission knowledge stays private until declassification; Project memory is project-RBAC-visible.

Cross-project learning is opt-in per Project and limited to redacted procedures/failure patterns. A global Candidate requires evidence from a configurable minimum of independent opted-in Projects, privacy checks, eval, and platform curation. Source withdrawal removes that contribution and recalculates support; it does not let one Project delete independently supported global knowledge.

### 19. Work coordination remains separate from authorization

Tasks may aggregate multiple source refs with one Decision-selected primary. Primary gets lifecycle sync; related sources get links, accepted key Decisions, and final summary. Responsible human/service actors drive attention/accountability but receive no permissions from assignment. Brain may auto-assign eligible service actors under policy; human assignment is a proposal.

Explicit blocking edges may cross private Missions in one Project when creator can access both. Inaccessible participants see a redacted dependency stub. Goal revision/cancellation/completion proposals carry a per-Task impact map. Task success follows a predeclared completion policy/evidence contract; verifier failure blocks the Task without rewriting the successful execution Run.

Internal Brain research does not create hidden WorkTasks. Orchestration accepts
an `ExecutionRequest` with an explicit origin. Work execution uses `WorkTask`;
subbrain research uses `BrainResearch` and links to operation/delegation ids,
appearing in operation trace rather than user Task boards.

### 20. Brain transport uses bounded execution sessions

`Comuki.Host.Brain` remains a model runtime, not an authorization principal.
Host creates a short-lived opaque Brain execution session bound to invocation,
expiry, Context Pack, and filtered catalog hash. Remote Brain gRPC requires
mTLS; colocated deployment may use loopback/Unix socket plus the session token.
User/API credentials never cross to Brain. Tool intents return to Host and the
Capability Broker re-authorizes every effect.

## Risks / Trade-offs

- **[Risk] Mega-epic produces a big-bang rewrite** → Treat this artifact as architecture; create and archive smaller OpenSpec changes per vertical slice. No implementation branch spans the whole plan.
- **[Risk] Capability Broker becomes a hidden mediator framework** → Keep typed owner declarations and direct handlers; bounded generated/reflection manifests may remove mechanical repetition, but no naming-convention discovery or generic policy pipeline. Context owners retain policy/execution.
- **[Risk] Context Fabric becomes a god-context** → It stores references/projections, not source aggregates; adapters are read-only and narrow; writes return through owner capabilities.
- **[Risk] Private Mission leaks through legacy Run/artifact/search APIs** → Common MissionAccessDecision is mandatory at every projection; promotion triggers visibility change and cache/signed-URL invalidation.
- **[Risk] Gapless Mission sequence serializes a busy room** → Serialize only stream append per Mission; worker progress may be coalesced before append; presence is outside the log. Revisit sharding only with measured contention.
- **[Risk] Outbox backlog makes views stale** → Surface lag/watermarks, monitor dead letters, and provide deterministic reconciliation/replay.
- **[Risk] Autopilot project policy permits dangerous exposed action** → Exposure hard-deny remains platform-owned; every effect is current-policy checked and audited; capability owner defines safe preconditions and optional step-up.
- **[Risk] Reasoning diagnostics capture sensitive data** → Deployment-only opt-in, bounded allowlist, encryption, strict TTL, redaction, and no retrieval into future context.
- **[Risk] Warm hosts weaken isolation** → Separate slot credentials/workspaces/processes and fencing; profiles can require single-slot/ephemeral hosts when stronger isolation is necessary.
- **[Risk] Trusted multi-project warm hosts widen compromise radius** → Make the trust-zone assumption explicit, isolate process/workspace/credentials, support policy-required strong provider sandboxes, apply profile∩project egress, and quarantine the whole host on suspicion. Hostile tenant deployments must require strong mode.
- **[Risk] Adaptive max context becomes expensive and noisy** → Planner uses evidence sufficiency, per-source budgets, handles, quality/cost evals, and a reserved synthesis budget.
- **[Risk] Configurable roles lock out operators** → Immutable platform-owner break-glass, version guards, impact simulation, last-owner invariants, and step-up approval.
- **[Risk] Full-content webhooks exfiltrate private Missions** → Dual consent, scoped subscriptions, redaction preview, HMAC, SSRF controls, consent-revocation cancellation, and delivery audit.
- **[Risk] Retention promises deletion while backups remain** → Per-object encryption keys make backup copies unreadable immediately; physical backup rotation is documented separately.
- **[Trade-off] Eventual consistency replaces immediate cross-module calls** → Commands may return accepted operations rather than completed effects; users gain reliable recovery and honest progress.
- **[Trade-off] Promotion makes old Task history private immediately** → This is required for a coherent privacy boundary; UI must preview affected links and participants before approval.

## Migration Plan

### Gate A: spec and architecture convergence

Archive smaller approved deltas into main specs before code. Add main specs for Work, Missions, Capability Broker, Context Fabric, Brain Operations, and Worker Pools; update Runs, Intake, Identity, Realtime, Memory, Artifacts, Chat, Compute, and Worker Runtime. Add a Knowledge spec or explicitly exclude generic Knowledge from the first Context Fabric slice.

### Gate B: execution spine

Before Task/Mission automation, land WorkItem dependency readiness, Run reconciliation, cancellation fencing, terminal outbox, and intake claim idempotency. Existing behavior remains externally compatible.

### Epoch 1: standalone Tasks

Introduce Work context additively. New admissions create Task then Run; existing APIs return compatibility projections. Backfill active/claimed Runs into Tasks. Historical terminal Runs may remain legacy. Tracker sync moves to Task resolution with dedupe.

### Epoch 2: minimal Missions

Create Mission/membership/revisions/Task links and promotion without Brain, room, or presence. Promotion previews and applies privacy mediation to legacy Run/artifact reads.

### Epoch 3: room and one proposal lane

Add Mission stream, messages, cursor catch-up, and one vertical proposal action (Task creation) through the Broker. Validate the seams before generalizing.

### Epoch 4: Context Fabric and durable Brain

Start with exact/current-state retrieval and answer-only Brain. Add provenance/invalidation, operation ledger, worker-backed research, then bounded children. Effects remain staged until Broker slices prove idempotency.

### Epoch 5: completion, realtime, clients, pools

Add completion review, notifications, multi-node presence, dashboard/CLI parity, then warm multi-slot worker hosts and capacity intents. Each is independently reversible behind an opt-in project feature flag.

### Epoch 6: platform configurability and integrations

Add configurable roles, typed settings/reload operations, templates, retention/crypto-shred/holds, Project webhooks, and opt-in global learning as separate vertical changes after the core broker and Context Fabric prove their contracts.

### Existing-state cutover matrix

| Existing state | Upgrade treatment |
|---|---|
| Pending IntakeTicket | Create Task idempotently on claim/admission |
| Claimed + active Run | Mandatory Task backfill; current Run becomes attempt 1 |
| Terminal Run not yet synced | Reconcile one Task outcome and emit one deduped sync job |
| Historical terminal Run | May remain legacy/unattached/read-only |
| Pending/failed sync job | Drain or translate before enabling Task-based bridge |
| Existing ChatSession | Remains personal chat unchanged |

Rollback after Epoch 1 cannot safely return to a binary that writes only `ticket.run_id` unless dual-write compatibility is active and no Task has multiple attempts. Every epoch therefore declares its minimum rollback version and data compatibility window.

## Open Questions

No product or architecture branch remains unresolved. Delivery changes may select interchangeable adapters/formats and tune documented defaults without changing this design:

- Redis client/backplane implementation and Context Pack compression/serialization.
- Exact numeric invitation/audit TTL, retry schedules, global-learning source threshold, and per-provider slot ceilings.
- The initial internal allowlist is fixed to discovery and diagnostic reads; the concrete capability ids follow the inventory.
- Legal deployments may override retention periods/holds while preserving class-aware policy and crypto-shred behavior.
