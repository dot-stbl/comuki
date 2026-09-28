## Why

Today the path a task takes through the swarm is implicit: the brain
decomposes a ticket into a plan, the platform executes it, and the
only durable artifacts of "how this project wants work done" are the
profiles and admission rules in the client's git. There is no
first-class object that says *this is how work enters, gets verified,
gets repaired, and gets accepted* — so every run re-invents the
procedure, operators cannot see which policy a run followed, and
nothing pins the two together when a question comes up months later.

The mission-cowork umbrella already reserves a `templates` capability
for *work shapes* (goal fields, task-link skeletons, completion
policy). This change adds the missing procedural half: the **project
procedure** — a versioned graph of how a project admits, executes,
verifies, repairs, and approves work — and the deterministic spine
that compiles it into something a run can be pinned to.

## What Changes

Introduce project procedures as an executable operating model:

- A procedure is a **layered graph definition**: platform defaults,
  project policy, and repository bindings merge into one compiled,
  immutable version. A procedure covers a project (one or more
  repositories bind to it by ref); it is not per-repository logic.
- Node kinds are **a separate typed catalog** (see the
  `procedure-node-kinds` capability): the graph references kinds by key,
  and each kind descriptor declares its outcome ports, parameter
  schema, evidence requirements, owning execution surface, and an
  optional editions feature key. The graph engine interprets wiring,
  never kind internals.
- **Brain proposes, the system disposes.** Chat/brain may draft a
  `GraphPatch` against a procedure; publication requires a deterministic
  compile gate (schema, DAG/cycle, capability and profile resolution,
  budgets, dry run) and an authorized human. The brain cannot publish
  and cannot widen its own policy.
- A run **pins an immutable compiled procedure version**. The runtime
  executes the compiled plan; it never asks a model whether a node may
  run. Loops exist only as bounded repair boundaries that unroll into
  generations; human gates surface as decisions; exhaustion escalates.
- Observability distinguishes **planned vs observed**: replay shows
  where the executed graph drifted from the compiled one and whether
  the drift stayed within the procedure's policy.

A Storybook-only prototype of the operator surface (Studio / Live
run / Replay) already exists on `feature/procedure-studio-storybook`
(commit `41a95268`); this change turns that validated UX contract into
platform behavior.

## Capabilities

### New Capabilities

- `project-procedures`: procedure definition, layering (platform /
  project / repository binding), versioned publication with the
  deterministic compile gate, run pinning, runtime execution
  semantics (wiring, bounded repair generations, human gates,
  escalation), GraphPatch proposal flow, and planned-vs-observed
  replay.
- `procedure-node-kinds`: the typed catalog of node kinds the graphs
  reference — descriptor shape (ports, parameters, evidence, owner
  binding, policy metadata, editions key), catalog validation and
  versioning, and per-kind editions enforcement.

### Modified Capabilities

None. The change deliberately does not alter `runs`, `tasks`, or
`chat` requirements yet: W1 slices bind procedures *beside* the
existing run admission path, and follow-up changes will retire the
legacy default-once the pinned path is proven. `templates`
(mission-cowork) stays a separate capability; a later change may add
a reference from a template to the procedure its tasks should ride.

## Impact

- **Backend**: new `Procedures` module (definition store, compiler,
  patch flow, pinning); orchestration gains a procedure-pinned
  materialization path and repair-generation semantics; decisions and
  approvals gain a gate origin. Uses the existing outbox/inbox spine
  from W1 execution-spine.
- **Dashboard**: the `procedures` Storybook domain becomes a real
  screen set (Studio / Live run / Replay) on generated contracts.
- **Chat / brain**: a new propose-patch operation routed through the
  capability surface — read + draft only, never publish.
- **Dependencies**: `@xyflow/react` (already added to dashboard in the
  prototype commit).
- **Non-goals**: no generic workflow engine, no user scripting on
  edges, no nested loops, no live editing of an active run's graph, no
  cross-project graphs, no template merge (separate capability).
