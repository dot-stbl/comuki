## Context

The platform already has the machinery a playbook needs: W1
execution-spine (dependency-gated claim, exactly-once finalization,
terminal Failed, generation fencing, cancel fencing, outbox/inbox),
deterministic Task/Run state machines from mission-cowork's
work-management spec, a `templates` capability reserved for work
shapes, profiles as the closed worker identity catalog, and a
Storybook-validated operator surface (commit `41a95268`) whose UX
contract — Studio / Live run / Replay with a deterministic-safety
vocabulary — is the input to this design. What does not exist: a
versioned procedural definition, a compile gate, run pinning, and a
patch proposal flow.

Positioning guardrail from PRODUCT.md: "LLM proposes — the system
disposes". Every decision below is an application of that one
sentence to graphs.

## Goals / Non-Goals

**Goals:**

- One durable, git-authored object that answers "how does this project
  want work done", layered platform → project → repository binding.
- A deterministic compiler whose output an active run can pin; identical
  inputs, identical version id.
- Runtime semantics (ordering, fan-out/join, repair generations, gates)
  computed by the platform — never by a model.
- A brain-facing GraphPatch flow that is useful and toothless: draft and
  diff, no publish, no policy widening.
- Replay that separates the compiled plan from the observed execution.

**Non-Goals:**

- No generic workflow engine or user scripting on edges (conditions are
  typed ports, not expressions).
- No template merge, no nested loops, no cross-project graphs.
- No live mutation of an active run's graph; no auto-publication of
  brain drafts; no per-repository control flow.
- No replacement of `runs`/`tasks` requirements in this change — binding
  happens beside the existing admission path; retirement of the
  unpinned default is a follow-up change once the pinned path is proven.

## Decisions

### 1. Playbook lives in the client's git; the compiled version lives in the platform

The authored artifact is a typed definition file per playbook in the
control-plane git (like profiles today). The platform compiles it into
an immutable content-addressed version stored with its resolution
snapshot (profiles, verifiers, capabilities, budgets). Runs pin the
compiled id, not the git ref — the ref is provenance metadata.

*Alternative rejected*: store authored graphs in platform DB with a UI
editor. That splits the "control plane versioned as code" promise and
makes a run's policy unreproducible from git alone.

### 2. Layering merges definitions; repository bindings select, not define

Platform defaults (allowed node kinds, floors: max generations, min
approvals), project playbook (the actual graph + policy), repository
binding (which playbook, which refs/profiles/verifier catalog slices).
Merge is structural with override-at-node granularity; conflicts are
compile errors, not last-writer-wins.

### 3. One `Playbooks` module; orchestration consumes compiled plans

New bounded context owning definitions, versions, the compile gate, and
patch proposals. Orchestration gains a materialization step: admitted
task + pinned version → executable work items per the compiled plan.
Node *ownership* stays with existing owners — an `agent` node is a
WorkItem, a `human-gate` is a Decision, a `capability` node is a Broker
operation — the playbook coordinator tracks position-in-procedure and
references owner state; it does not re-implement execution. This is the
owner-specific process-manager pattern from mission-cowork's design,
applied to procedure rather than to choreography.

### 4. Repair boundaries unroll; the materialized graph stays a DAG

A boundary declares body + re-entry port + max generations + budgets.
Each generation is a numbered materialization referencing the failing
evidence; there is never a runtime back-edge. Exhaustion or an
inconclusive port escalates to a human decision (retry / replace /
waive / fail). This keeps every existing DAG invariant (depth,
fencing, exactly-once) untouched.

### 5. GraphPatch is a durable proposal object, not an edit

`GraphPatch { baseVersion, operations[], rationale, draftedBy }`.
Brain drafts it through a capability with `Suggest`-class exposure;
the compile gate validates it against the base; Studio renders the
semantic diff; a human publishes (which recompiles from the patched
definition and pins a new immutable id). Patches touching publish
rights, autonomy ceilings, or budget maxima are refused before compile
by an allowlist check, mirroring the capability-broker's exposure
classes.

### 6. Port outcomes are the only conditions

Nodes declare typed outcome ports (`verify.passed`, `verify.failed`,
`verify.inconclusive`, `gate.approved`, `gate.rejected`, …). Edge
wiring connects ports, not free-form predicates. This is what makes
compilation checkable (every port wired or defaulted) and what keeps
"100 из 100" honest: the branch set is enumerable at compile time.

### 7. Studio on React Flow; Live run and Replay stay layered projections

The validated prototype contract carries over: canvas for authoring,
diff, and replay topology; dense layered views for live operations;
semantic linear fallback for narrow widths and screen readers. Studio
reads drafts and compiled versions through generated contracts
(kubb), never mutating git.

### 8. Editions split — mechanism is community, scale and governance are paid

The moat is the deterministic spine (compile, pinning, evidence), not
the canvas — n8n/Temporal/GitHub Actions have taught the market that
drawing graphs is free. So the mechanism ships to everyone and creates
the operating-model lock-in; teams pay when they outgrow scale or need
governance, which is exactly when budget exists.

| Surface | Community | Paid |
| --- | --- | --- |
| Definition, layering, compile gate, run pinning | ✅ | ✅ |
| Studio / Live run / Replay | ✅ | ✅ |
| Repair boundaries, replay, planned-vs-observed | ✅ | ✅ |
| Brain GraphPatch drafting | ✅ | ✅ |
| Published playbooks per project | cap 3 (`EnforceLimit`) | unlimited |
| Concurrent playbook-pinned runs | cap 5 (`EnforceLimit`) | unlimited |
| Multi-repo bindings per playbook | ❌ | ✅ |
| Two-approval gates / custom approval policies | 0/1 only | ✅ |
| Audit-grade replay retention + export | ❌ | ✅ |
| Policy simulation / impact diff before publish | ❌ | ✅ |

Feature keys: `playbooks.multi-repo`, `playbooks.two-approval-gates`;
limits: `playbooks.published-per-project`,
`playbooks.concurrent-pinned-runs`. Cap numbers (3 / 5) are owner
defaults, tunable without a spec change.

Two hard lines: the compile gate's correctness is never paid-only
(no "community graphs validate worse" tier — safety is a platform
property), and expiry degrades to read-only replay (consistent with
the editions line's read-only degrade), never to lost pins or
silently skipped validation.

This matches the owner-fixed editions decisions: open-core flags +
license in one code, white-label paid only, multi-repo/scale/isolation
as enterprise candidates. Implementation cost is annotations at module
birth (`RequiresFeature` / `EnforceLimit`), which is why the split is
fixed before W1 lands rather than retrofitted.

### 9. Node kinds are a typed catalog, not engine internals

The set of things a node can *be* is its own functionality: a
descriptor catalog in the control-plane git, versioned like profiles.
The engine interprets wiring only — ports, ordering, generations — and
dispatches instances to the kind's owning surface (work item /
decision / broker operation / brain operation), reading owner state
through a typed projection. This is the typed-registry shape (the
data picks the branch, the compiler checks it), and it buys three
things: catalog versioning pinned per compiled playbook; per-kind
editions enforcement at compile time (never mid-run — a pinned
version stays executable under the edition that compiled it, expiry
degrades new compilations only); and the closed-set rule restated as
"the baseline catalog is spec'd; a new kind is a catalog version plus
a spec delta to `playbook-node-kinds`, not an engine change".

*Alternative rejected*: kinds as engine enums. That couples catalog
evolution to engine releases and leaves no place for editions keys or
evidence contracts to live.

## Risks / Trade-offs

- **Compile gate becomes a bottleneck for iteration.** Mitigation: the
  gate runs on every draft push (cheap, pure) and Studio shows its
  findings live; dry-run uses synthetic tasks and costs nothing.
- **Two sources of procedure (playbook + brain's plan node) can fight.**
  The split is explicit: the playbook constrains the plan node's output
  (allowlists, fan-out ceilings, required evidence); the plan node
  shapes work *inside* an execute boundary. Drift is replay's
  within/outside-policy classification, not a silent takeover.
- **Content-addressed pinning doubles storage.** Versions are small
  (KB); the resolution snapshot is the expensive part and is
  content-deduplicated.
- **React Flow canvas accessibility.** The linear fallback is a
  contract, not a nicety — it ships with the first screen and the
  storybook batch pins it.
- **Scope creep toward BPMN.** The closed node-kind set and typed ports
  are load-bearing; any new kind requires a spec delta to this
  capability.
