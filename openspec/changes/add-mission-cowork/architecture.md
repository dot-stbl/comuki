# Target Code Architecture

This document is the physical code and technology companion to `design.md`.
`design.md` owns behavioral architecture and trade-offs; this file owns project
placement, dependencies, storage, generated contracts, and technology choices.

## Repository Shape

```text
comuki/
├── package.json                    # root Bun workspace + canonical scripts
├── bun.lock                        # one TS lockfile
├── comuki.slnx
├── platform/
│   ├── generated/
│   │   └── ts/                     # @dot-stbl/comuki-client-contracts (committed)
│   └── src/
│       ├── shared/
│       │   ├── Comuki.Shared.Kernel
│       │   ├── Comuki.Shared.Contracts
│       │   ├── Comuki.Shared.Filtering
│       │   ├── Comuki.Shared.Messaging
│       │   ├── Comuki.Shared.Migrations
│       │   ├── Comuki.Shared.Bootstrap
│       │   ├── Comuki.Shared.Telemetry
│       │   └── Comuki.Shared.Caching       # create only with real backplane slice
│       ├── modules/
│       │   ├── Work/
│       │   │   ├── Comuki.Modules.Work.Domain
│       │   │   ├── Comuki.Modules.Work.Application
│       │   │   └── Comuki.Modules.Work.Infrastructure
│       │   ├── Missions/                   # same Domain/Application/Infrastructure
│       │   ├── Templates/                  # same
│       │   ├── Integrations/               # replaces Intake, same
│       │   ├── Identity/                   # existing, configurable roles added
│       │   ├── Projects/                   # existing
│       │   ├── Chat/                       # personal chat only
│       │   ├── Memory/                     # user/project/global claims
│       │   ├── Knowledge/                  # source document corpus
│       │   ├── Artifacts/                  # object metadata and run bundles
│       │   ├── Costs/ · Scheduler/ · Proxy/
│       │   └── ...
│       ├── engine/
│       │   ├── Comuki.Engine.Platform
│       │   │   ├── Capabilities/
│       │   │   ├── Context/
│       │   │   ├── Brain/
│       │   │   ├── Configuration/
│       │   │   ├── DataLifecycle/
│       │   │   └── Persistence/
│       │   ├── Comuki.Engine.Orchestration
│       │   └── Comuki.Engine.Compute
│       └── host/
│           ├── Comuki.Host
│           │   ├── Api/{Work,Missions,Integration,...}/
│           │   ├── Composition/{Work,Missions,Platform,...}/
│           │   ├── Realtime/
│           │   └── Program.cs
│           ├── Comuki.Host.Brain
│           ├── Comuki.Host.Translator
│           └── Comuki.Migrator
├── agents/                          # existing agent SDK packages, root workspace members
├── dashboard/                       # root workspace member
├── cli/                             # root workspace member; architecture deferred
├── control-plane/
└── tests/
```

Business bounded contexts keep the existing three-project
Domain/Application/Infrastructure shape because their dependencies and stores
are genuinely different. Platform runtime mechanisms share one
`Comuki.Engine.Platform` project to avoid fifteen shallow projects. Its five
folders are internal by default and expose narrow public facades; namespace
architecture tests forbid cycles and direct internal coupling.

## Dependency Direction

```text
Host ──────────────▶ Modules / Engines ─────────────▶ Shared
  │                       │
  │ composes adapters     ├─ business module ↛ sibling module
  │                       ├─ business module ↛ Engine.Platform implementation
  │                       └─ Engine.Platform ↛ business implementation
  └─ Shared.Contracts host seams connect both sides

Host.Brain ◀══ authenticated duplex gRPC ══▶ Host / Engine.Platform
Translator ◀══ worker gRPC + REST ═════════▶ Host / Orchestration
```

Modules declare application ports and integration message contracts in their
own layers or `Shared.Contracts` only when the seam crosses process/host
composition. Host feature composition folders adapt these ports into
`Engine.Platform` registries. `Shared.Contracts` remains one project, organized
by namespace and guarded against domain/implementation leakage; split only when
independent package versioning becomes real.

## Module Ownership

| Project/context | Owns | Does not own |
|---|---|---|
| Work | `WorkTask`, source refs, responsibility, dependencies, attempts, resolution | worker leases, Mission membership |
| Missions | Mission, revisions, configurable room roles, membership, Task links, stream, proposals, completion | Task lifecycle, foreign action execution |
| Templates | generic versioned template documents, publication/dry-run/promotion | Mission/Task entities or procedural prompts |
| Integrations | connections, providers, `InboundItem`, deliveries, admission, inbox, outbound subscriptions/sync | native Task creation, Task lifecycle |
| Identity | actors, credentials, permission catalog, configurable roles/grants, break-glass owner | Mission stream or capability execution |
| Memory | scoped semantic claims/candidates/conflicts | source documents, Context Packs |
| Knowledge | revisioned documents/chunks/search adapter | trusted procedures/instructions |
| Artifacts | artifact metadata/bundles/S3 adapter | Mission authorization decision |
| Engine.Platform | capability operations, Context Fabric metadata, Brain checkpoints, typed config, lifecycle policy/holds | business aggregates and owner policies |
| Orchestration | Run, WorkItem, queue, leases, run journal | user-facing Task or Mission goal |
| Compute | hosts, slots, placement/capacity planner, provider adapters | Task policy or Brain reasoning |

Templates store a generic document, schema id, requirements, and provenance.
Missions/Work supply validators and instantiators through Host composition; no
module references another module to deserialize its domain DTO.

## Public Module Interfaces

Synchronous requests enter an owner context as a typed capability and terminate
at one application handler. There is no second mediator/dispatcher hidden inside
the module. A module exposes commands, queries, and integration events, not its
DbContext, entities, or `IQueryable`.

```text
Transport adapter
  → Capability Broker
    → owner Application handler
      → owner Domain + Infrastructure ports
```

### Work

```text
Commands
  work.task.create
  work.task.revise
  work.task.assign
  work.task.link-source
  work.task.relate
  work.task.dispatch
  work.task.cancel-attempt
  work.task.resolve

Queries
  work.tasks.query
  work.task.read
  work.task.attempts
  work.task.dependencies

Publishes
  work.task.created
  work.task.readied
  work.task.blocked
  work.task.attempt-requested
  work.task.resolved
  work.task.cancelled

Consumes
  orchestration.run.started
  orchestration.run.terminated
  orchestration.run.cancelled
```

The Work execution process owns `Ready WorkTask → Run attempt → terminal →
verification → Task resolution`. It stores attempt/process state, issues
idempotent Run start/cancel commands, consumes Run terminal facts, and applies
the pinned completion policy. Orchestration never resolves a Task.

### Missions

```text
Commands
  mission.create / activate / revise / cancel / reopen / delete
  mission.member.invite / accept / remove
  mission.task.attach / revise-link / detach
  mission.message.send / edit / delete
  mission.proposal.decide
  mission.review.request / decide

Queries
  mission.query / read
  mission.stream.read
  mission.members
  mission.workspace.summary

Publishes
  mission.created / activated / revised / completed / cancelled
  mission.member.invited / joined / removed
  mission.task.attached / link-revised / detached
  mission.stream.appended
  mission.review.requested / completed

Consumes
  work.task.created / blocked / resolved / cancelled
  platform.brain.operation-completed
```

The Missions completion process owns `required links resolved → evidence
generation → Brain review → proposal/Decision → Completed`. It calls the Brain
operation capability and records Mission-owned review/proposal state. Brain does
not watch tables or close Missions independently.

### Integrations

```text
Commands
  integration.inbound.receive / admit / dismiss / claim
  integration.connection.create / test / revise / delete
  integration.subscription.create / revise / consent / replay

Queries
  integration.inbox.query
  integration.connections.query
  integration.deliveries.query
  integration.subscriptions.query

Publishes
  integration.inbound.admitted
  integration.inbound.dismissed
  integration.delivery.dead-lettered

Consumes
  work.task.resolved
  mission.completed / cancelled
  mission.stream attention/key-decision projections
```

The Integrations admission process owns the external envelope until Work returns
a confirmed Task identity. It emits an idempotent Work admission command and
records the resulting Task id. Work then owns dispatch and execution. Host does
not synchronously chain InboundItem → Task → Run as one pseudo-transaction.

### Engine.Platform

```text
Capabilities facade
  describe / preflight / invoke / decide / cancel / read-operation

Context facade
  plan-retrieval / compile-pack / read-manifest / resolve-source

Brain facade
  start / cancel / read-operation / append-evidence

Configuration facade
  describe / read-effective / preflight-change / apply-revision

DataLifecycle facade
  evaluate / hold / release / request-delete / read-plan
```

The Brain operation process owns child operations, research execution requests,
checkpoints, budgets, and evidence return. Research uses an internal execution
capability; it does not create a user-visible WorkTask unless a separate proposal
does so.

Orchestration accepts a general `ExecutionRequest` whose origin is explicit:
`WorkTask`, `BrainResearch`, `Verification`, `Discovery`, `ScheduledJob`, or
`Maintenance`. Work-owned attempts reference WorkTask; Brain research references
the Brain operation/delegation and appears in its trace, not the Task board.

### Composite reads

An API response that combines Mission, Tasks, Runs, participants, and operations
is assembled by a Host/API projection composer. It calls owner query ports in
parallel, applies the relevant object-access decisions, and returns a read-only
DTO. It stores no authoritative copy and contains no mutation path. Context
Fabric is for evidence retrieval and model packs, not a generic read-model API.

## Integration Event Contract

Integration event schemas are owned by the publisher and exposed from its
Application contract surface. They are not gathered into a central domain-event
catalog and consumers do not define private reinterpretations of the same JSON.
Names use stable lowercase dot.case:

```text
<context>.<aggregate>.<past-tense>

work.task.created
orchestration.run.terminated
mission.member.invited
integration.inbound.admitted
```

Internal events encode the major contract version in the stable type name, for
example `work.task.resolved.v1`. Within one major type, changes are additive and
readers tolerate absent/new optional fields. A breaking payload publishes a new
type such as `.v2`:

1. deploy consumers that understand both types;
2. switch or temporarily dual-publish according to the migration plan;
3. drain/reconcile old `.v1` outbox messages and dead letters;
4. remove `.v1` only after consumer watermarks prove it is unused.

The event name prevents incompatible payloads from being confused without a
second payload-version field. External webhooks and public API/stream schemas
retain their own explicit versioning contracts.

Most cross-context communication is event choreography. An owner-specific
process manager is introduced only for a durable multi-step flow with timeout,
retry, compensation, or a required result identity. There is no generic saga
engine and no central workflow orchestrator in Engine.Platform.

## Role Delegation Semantics

Permission definitions carry delegation metadata and scope constraints. A human
may publish a role or grant only from:

```text
grantor effective permissions
∩ permissions marked delegable
∩ target scope no wider than grantor scope
∩ platform/project policy ceiling
```

Protected permissions use dedicated step-up/break-glass flows. Existing grants
do not automatically gain permissions added by a new role version. Tightening
(removed permission or narrower constraint) applies immediately; widening
requires explicit regrant/approval. Authorization audit records the effective
role definition/version used for the decision.

## Engine.Platform Internal Modules

`Comuki.Engine.Platform` is one deep project, not a generic mediator framework.

```text
Capabilities/
  public: catalog, invoke/query, operation/decision interfaces, typed descriptors
  internal: registry, idempotency ledger, policy orchestration, operation state

Context/
  public: SourceRef, ContextPack request/handle, source adapter and retrieval interfaces
  internal: planner, indexes, provenance, invalidation, pack compiler

Brain/
  public: start/cancel/read operation, child/evidence contracts
  internal: checkpoints, delegation coordinator, call ledger, synthesis state

Configuration/
  public: typed setting definition/read/preflight/change interfaces
  internal: revision store, source precedence, reload/restart/redeploy operations

DataLifecycle/
  public: retention resource adapter, hold/delete-plan interfaces
  internal: policy evaluator, sweep coordinator, crypto-shred orchestration
```

Capability queries and commands have different runtime weight. Ordinary
read/list queries pass authorization/object policy, execute, and emit bounded
telemetry without creating a durable Operation row. Commands, approvals,
asynchronous work, and explicitly sensitive/expensive reads use durable
operations and idempotency receipts.

`Comuki.Shared.Contracts` is restricted to cross-process and host-composition
seams: Brain/worker gRPC, realtime/wire unions, and genuinely shared primitives.
Module commands, queries, and integration events remain in the publisher's
Application assembly. Architecture tests reject module-specific DTO dumping
into Shared.Contracts.

One installer `AddPlatformEngine` exposes the composition facade. Explicit typed
registration is the starting point. Bounded reflection/source-generated
registration is permitted for homogeneous opt-in catalogs after repetition is
demonstrated; startup validation and an inspectable manifest are mandatory.

## Persistence

### PostgreSQL schemas

```text
platform       PlatformRuntimeDbContext / __comuki_platform
work           WorkDbContext            / __comuki_work
missions       MissionsDbContext         / __comuki_missions
templates      TemplatesDbContext        / __comuki_templates
integrations   IntegrationsDbContext     / __comuki_integrations
orchestration  existing engine context
identity · projects · memory · knowledge · artifacts · costs · scheduler · ...
```

`Integrations` is a hard pre-release replacement for `Intake`: projects,
namespaces, schema, migration baseline, generated clients, and routes are
renamed in one dedicated change. Development/staging data is reset; no runtime
compatibility aliases or dual write are carried. `IncomingTicket` becomes
`InboundItem`. Native Task creation calls Work directly.

Every context has local `outbox_messages` and `inbox_receipts` configured by
`Comuki.Shared.Messaging`; event payload schemas remain owner-defined. Hosted
dispatchers poll PostgreSQL with `FOR UPDATE SKIP LOCKED`, optional
LISTEN/NOTIFY wake hints, per-aggregate ordering, bounded retries, and visible
dead-letter state.

`PlatformRuntimeDbContext` owns capability operations/idempotency, context
manifests/index metadata, Brain checkpoints/call ledger, configuration
revisions, retention policies, and holds. One context/schema is intentional:
these are tightly coupled runtime mechanisms with one lifecycle.

All aggregates use explicit `bigint Version` optimistic concurrency surfaced as
expected-version guards. `xmin` and timestamps do not cross contracts.

### EF and PostgreSQL-specific operations

Application and Domain code see typed ports and query/filter objects. Inside
Infrastructure:

- ordinary list filters compose as `IQueryable` and never cross module
  boundaries;
- the existing `Comuki.Shared.Filtering` remains the canonical user-facing
  filter/sort DSL: neutral parser AST → typed expression → `IQueryable`;
- Filtered is not extended with FTS, vector-distance, ranking, locking, claim,
  transaction, or update semantics; those are different query/command shapes;
- Context Fabric exposes a dedicated typed `ContextSearchQuery` / provenance-hit
  result seam. Its Infrastructure adapter may compose supported Npgsql FTS and
  pgvector translations internally and uses a narrow parameterized query for
  rank fusion when ordinary LINQ cannot represent the plan honestly;
- locking, `SKIP LOCKED`, fencing, atomic update-returning, and advisory locks
  remain typed store operations with local parameterized SQL;
- no custom EF query provider is built until a repeated unsupported translation
  justifies its maintenance cost.

This preserves an EF/LINQ-native application shape without pretending
transactional commands are ordinary query decorations.

Semantic retrieval uses PostgreSQL FTS plus pgvector generations behind the
dedicated search seam. No external vector database, custom general-purpose LINQ
provider, or Postgres queue extension is introduced.

`Comuki.Shared.Filtering` stays a local adapted project rather than referencing
`Hybrid.Sdk.Shared.Core` or a neighboring checkout. Selective backports from the
upstream SDK are allowed when a Comuki consumer needs them: null-safe string
predicates, invariant case folding, nullable-date fixes, range/negated
operators, typed Comuki identifiers, and boundary date functions. Comuki keeps
its injected `TimeProvider`. An architecture/integration gate compares the
filterable registry with the actual EF model so a newly added sensitive or
ignored property cannot silently become a public filter/sort field.

## Object Storage and Encryption

The object interface is S3-compatible; MinIO is the default self-hosted adapter,
AWS/compatible endpoints are supported, and filesystem is a dev/test adapter.

```text
encrypted/<random-object-id>                    immutable encrypted payloads
projects/<project>/missions/<mission>/...       manifests/references only
```

Security is enforced by PostgreSQL object metadata and Mission access, never by
object path. Each deletable body/blob has a random UUIDv7 object id and DEK;
PostgreSQL stores wrapped DEK, key version, plaintext hash for integrity and
scoped dedupe, visibility/lifecycle scope, and object metadata. Equal private
files in different lifecycle scopes use independent ciphertext and keys. Global
convergent encryption is forbidden. Deployment KEK is loaded through the
secrets/KMS adapter. Crypto-shred deletes the wrapped DEK and materialized
derivatives; future KMS providers do not change object identity.

## Caching and Realtime

Do not create an empty caching project. When the realtime/cache slice lands,
create `Comuki.Shared.Caching` containing concrete InMemory/Redis plumbing and
role-specific adapters. Use:

- `Microsoft.Extensions.Caching.StackExchangeRedis` for distributed cache;
- `Microsoft.AspNetCore.SignalR.StackExchangeRedis` for SignalR backplane;
- direct `ConnectionMultiplexer` only inside this infrastructure project for
  presence TTL/pub-sub primitives not represented by `IDistributedCache`.

Deployment configuration selects InMemory single-node or Redis multi-node and
validates replica topology. Durable stream data never depends on Redis.

## API and Generated Contracts

### Routes and versioning

- `Asp.Versioning.Mvc` + API Explorer;
- URL segment `/api/v{version}`;
- nested resource grammar: singular category + plural collection;
- examples: `/api/v1/work/tasks`, `/api/v1/mission/rooms`,
  `/api/v1/integration/connections`, `/api/v1/capability/operations`;
- action leaves remain singular, e.g. `/cancel`;
- no hyphenated resource segments;
- dashboard routes remain product-oriented (`/tasks`, `/missions/$id`).

API controllers/minimal adapters remain in `Comuki.Host`, organized by bounded
feature folders. `HostComposer` delegates to explicit feature composition
installers; no separate API projects until a second deployable or reusable API
assembly exists.

### Description surface

- built-in `Microsoft.AspNetCore.OpenApi` remains the generator;
- `Scalar.AspNetCore` renders branded reference at `/api/description`;
- raw version documents live at `/api/description/{version}/openapi.json`;
- XML docs + transformers + typed examples provide descriptions, ProblemDetails,
  security, operation ids, tags, and `x-comuki-*` capability metadata;
- operator HTTP API only; internal/runtime/bootstrap catalogs are separate and
  authenticated;
- public by default for self-hosted, configurable auth/disable;
- API Explorer groups and Sunset headers describe deprecation windows.

### TypeScript pipeline

Root Bun workspace members include `agents/comuki-*`, `dashboard`, `cli`, and
`platform/generated/ts`. One root `bun.lock` replaces nested lockfiles.

`platform/generated/ts` is a full committed generated package named
`@dot-stbl/comuki-client-contracts`:

```text
C# Host API + Shared.Contracts stream unions
          ↓ build-time OpenAPI / JSON Schema
root `bun run codegen`
          ↓ Kubb once + schema generation + formatting
platform/generated/ts
          ├─ dashboard (TanStack adapters/hooks)
          └─ CLI after its separately planned rewrite
```

CI `codegen:check` regenerates and fails on diff. Generated code carries DTOs,
REST client, operation ids, ProblemDetails codes, Mission stream unions,
forward-compatible decoders, and non-authoritative capability metadata. It
never embeds permission decisions.

The CLI is explicitly not restructured in this architecture change because a
larger rewrite is pending. It must consume the common package when rewritten;
current manual contracts are compatibility debt, not the target design.

## Dashboard Structure

```text
dashboard/src/domains/
├── missions/       Workspace board/graph + Room controllers/views
├── work/           standalone/shared Task projections and forms
├── operations/     Brain/capability live cards, proposals, traces
├── integrations/   connections, inbox, deliveries, subscriptions
└── settings/       typed configuration, roles, templates, lifecycle policy

dashboard/src/shared/conversation/   render/composer/parts/viewport kit
dashboard/src/shared/realtime/       connection and transport primitives
```

Personal Chat and Mission Room reuse the conversation kit but keep separate
domain state/controllers. TanStack Query owns all durable server state. A
sequence-aware Mission projection controller performs REST catch-up and applies
SignalR patches/invalidation to Query cache. Zustand owns only ephemeral UI:
selected view, layout, local drafts, composer mode, and presence/typing. It does
not duplicate Tasks, messages, or operations. Drafts may persist locally but are
never sent automatically after reconnect.

Board uses dnd-kit with keyboard/menu alternatives; a move invokes capability
preflight rather than mutating client state optimistically as authority. Task
dependency graph uses React Flow as a projection styled through the existing
design system; graph mutations still use capabilities.

## Background Work

No Hangfire or Quartz. Outbox, retention, summary, review, and reconciliation
workers are `BackgroundService` loops over DB leases/`SKIP LOCKED`, use
`TimeProvider`, expose bounded metrics, and fail individual passes safely. The
existing Scheduler module remains the product capability for user-created
scheduled jobs, not infrastructure dispatch.

## Worker Isolation Topology

Self-hosted Projects are one trusted deployment zone by default. Warm capacity
and sandbox strength are separate concepts:

```text
WorkerHost advertises N slots
├── trusted-process Execution → fresh process + workspace + credentials
└── strong Execution          → provider sandbox (OCI container / Pod / Job)
```

- Docker/Podman trusted mode: one warm worker container with N process slots.
- Docker/Podman strong mode: provider starts a sibling execution container; the
  worker container never mounts a raw Docker socket.
- Kubernetes trusted mode: one warm worker Pod with N process slots.
- Kubernetes strong mode: separate execution Pod/Job.
- Standalone runner: pluggable sandbox driver; without a strong driver it can
  claim only trusted-process work.

Profiles and Project policy specify minimum isolation. Provider/runner advertises
supported classes; placement cannot downgrade. Every Execution still has a
fresh agent session, isolated mutable workspace, execution-only secret material,
lease/fencing, logs, cancellation, and cleanup. Suspicion of compromise
quarantines and fences the entire host.

## Migration and Project Creation Discipline

All new `.csproj` files are added to `comuki.slnx`, wired through explicit
ProjectReferences, and covered by architecture tests. `Comuki.Migrator` remains
the single one-shot migrator with an explicit ordered context registry; Host
never auto-migrates. Pre-release schema resets and the hard Intake→Integrations
rename occur in dedicated changes, not mixed with Mission behavior.

The custom source generator question remains deliberately deferred: use STJ
source generation and OpenAPI/Kubb immediately; introduce a capability manifest
generator only after real repeated metadata appears across several delivered
slices.
