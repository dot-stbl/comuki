## Why

Comuki can execute isolated goals, but it cannot yet host a durable shared objective where any number of people and the Brain discuss intent, coordinate multiple Tasks, revise work, and use the same capabilities from chat, dashboard, CLI, and automation. The current `IntakeTicket → Run` shortcut, subject-owned chat, fragmented tool catalogs, and prompt-sized Brain context cannot safely scale into that product.

## What Changes

- Introduce project-scoped **Missions**: private membership, one durable room, goal revisions, acceptance criteria, Task links, proposals, decisions, completion reviews, presence, and one ordered stream.
- Introduce standalone **Tasks** between intake and execution. A Task optionally joins one Mission and owns sequential Run attempts; `Run` becomes one execution attempt.
- Introduce a shared **Capability Broker** as the semantic command/query chokepoint for UI, CLI, HTTP, MCP, Brain, and automation, with authorization, policy, autonomy, approvals, idempotency, operations, and audit.
- Introduce **Context Fabric**: provenance-aware sources, visibility-filtered retrieval, adaptive Context Packs, structured memory, invalidation, durable Brain operations, and bounded subbrain delegation.
- Evolve compute from one ephemeral worker per execution into capability-advertising warm worker hosts with independently leased, isolated execution slots; Brain requests desired parallelism while a deterministic planner enforces hard limits.
- Add first-class Mission support to dashboard and CLI, including `comuki mission <id>` and non-interactive JSON commands.
- **BREAKING:** admitted tickets no longer make a Run the durable user-facing goal; they create a Task whose Runs are attempts. Compatibility projections and an explicit cutover protect existing clients.

## Capabilities

### New Capabilities

- `work-management`: standalone Tasks, Task lifecycle/resolution, source links, dependencies, and sequential Run attempts.
- `missions`: collaborative Missions, membership, room stream, proposals/decisions, completion, and Task associations.
- `capability-broker`: typed semantic commands/queries, autonomy policy, durable operations, approvals, idempotency, and audit.
- `context-fabric`: canonical source references, Context Packs, retrieval, provenance, memory promotion, and invalidation.
- `brain-operations`: durable Brain/subbrain execution, delegation DAGs, checkpoints, synthesis, budgets, and cancellation.
- `worker-pools`: warm worker hosts, advertised capabilities, isolated slots, capacity intents, and placement.
- `configuration`: typed platform/project/Mission settings, validation, reload classes, impact planning, and Brain-managed changes.
- `templates`: versioned Mission and Task work-shape templates with publication, binding, dry-run, and migration.
- `data-lifecycle`: class-aware retention, holds, evidence pinning, crypto-shred, and deletion audit.
- `outbound-webhooks`: scoped, signed, redacted, replayable Project integration subscriptions for Mission attention and full events.
- `api-contracts`: versioned nested HTTP routes, documented OpenAPI/Scalar reference, and one generated TypeScript contract package for clients.
- `code-graph`: repository-symbol graph (symbols, files, call-edges, impact paths) for the client code base, first iteration TypeScript and C# via tree-sitter, surfaced as a Context Fabric source so Brain and workers see code without re-deriving it.

### Modified Capabilities

- `intake`: replaced before first release by the broader Integrations context; admission creates Tasks and tracker sync follows Task/Mission resolution.
- `runs`: Runs become fenced Task attempts with reliable terminal publication.
- `worker-runtime`: leases and streams bind to independent execution slots rather than one host-wide worker identity; workers also expose an ephemeral tier (local scratch + flush to platform memory on completion).
- `compute`: capacity planning supports warm multi-slot hosts and desired-parallelism intents.
- `identity`: actor, credential, service account, Mission access, audit grants, configurable roles, and capability permissions become explicit.
- `realtime`: add ordered Mission delivery plus multi-node presence and typing; the same `IRealtimeBackplane` carries Mission-scoped swarm-blackboard pub/sub for worker findings.
- `memory`: project knowledge promotion gains candidate, conflict, provenance, visibility, and declassification semantics; add a Mission scope, a swarm-blackboard durable side, outcome-reinforced candidate ranking, and an ephemeral flush contract for the worker tier.
- `artifacts`: Task/Mission visibility mediates Run and artifact reads.
- `chat`: personal chat remains separate but uses the common capability and Brain-operation planes.
- `control-plane`: skills carry managed-asset metadata (`trigger_when`, `validate_against`, version) on top of today's pure markdown; the catalog exposes them so the Brain and Worker SDK can decide when to apply a skill and whether it is still valid for the current control-plane generation.

## Impact

This is a post-v1 platform evolution touching orchestration, intake, identity, memory/knowledge, artifacts, realtime, compute, Brain, Translator, dashboard, CLI, OpenAPI/MCP projections, PostgreSQL schemas, MinIO context payloads, and operational deployment. Delivery requires an architecture/spec convergence gate and multiple vertical releases; issue #70 remains the umbrella epic.

## Non-goals

- Replacing personal chat with Missions.
- Tasks belonging to multiple Missions. (Cross-project Missions are IN scope since 2026-09-25: a Mission has a home Project plus participating Projects — see add-multi-repo-projects, decisions R7/R15.)
- Giving models credentials, raw system authority, runtime/bootstrap protocols, or plaintext secret reveal.
- Treating the model context window as storage, or persisting hidden chain-of-thought outside deployment-only diagnostics.
