## 1. Node kind catalog (pure, no runtime)

- [ ] 1.1 Create the `ProcedureNodeKinds` catalog model: typed descriptor (key, parameter schema, outcome ports, evidence requirements, owner surface, policy metadata, optional editions feature key), catalog version root, and control-plane git loading; verify a unit suite loads the baseline v1 set (intake, classify, plan, agent, fan-out, join, verify, review, human-gate, repair-boundary, capability, complete, escalate) and refuses a draft descriptor missing an owner surface
- [ ] 1.2 Implement catalog validation (schema, owner resolvability, port contracts) and versioning rules (add/alter bumps the version; retraction refused while any published procedure references the kind); verify retraction refusal names the referencing procedures and that a new kind does not affect an existing catalog pin
- [ ] 1.3 Add editions enforcement hooks at kind granularity (feature key on the descriptor; compile-time refusal path with node and key named); verify a `procedures.two-approval-gates`-carrying kind is refused under a community composition and allowed under an enterprise fixture

## 2. Procedure definition, layering, compile gate (pure)

- [ ] 2.1 Create the `Procedures` module skeleton (Domain / Application / Infrastructure, installer, Migrator loop entry, slnx registration, arch tests) with the definition model: nodes reference catalog keys, edges connect typed outcome ports; verify the module builds with 0/0 and architecture tests name the allowed dependency directions
- [ ] 2.2 Implement layering (platform defaults → project policy → repository binding) with structural merge and conflict-as-error; verify a repository binding cannot introduce control flow and one procedure serves several repositories of one project
- [ ] 2.3 Implement the deterministic compile gate: schema validation, acyclicity outside repair boundaries, catalog/profile/verifier resolution against pinned catalogs, budget ceilings, idempotency declarations on state-changing nodes, editions kind-keys check; verify rejection cases (unknown kind, cycle outside a boundary, missing verifier, paid kind under community) each name the offending node, and that identical inputs produce an identical content-addressed version id
- [ ] 2.4 Add the immutable version store (compiled plan + resolution snapshot, content-deduplicated); verify republishing a procedure never mutates an existing version and a pinned version is byte-identical on re-read

## 3. Publication and GraphPatch flow

- [ ] 3.1 Add the durable GraphPatch proposal (base version, typed operations, rationale, drafted-by) with a semantic diff computation; verify a patch's diff renders add/remove/rewire/re-parameterize against its base version
- [ ] 3.2 Wire publication: patch or git-authored draft → compile gate → authorized human approval → new immutable version; publish `procedures.procedure.published.v1` through the existing outbox; verify the brain's drafts cannot publish and patches touching publish rights, autonomy ceilings, or budget maxima are refused before compile
- [ ] 3.3 Add retry re-pin semantics at the task boundary; verify a new attempt pins the then-current version and the attempt ledger records the version change between attempts

## 4. Run pinning and materialization

- [ ] 4.1 Bind admission to a pinned compiled version beside the existing run path (procedure-pinned materialization is additive; the unpinned default remains until a follow-up change retires it); verify an admitted task records its pin and survives a concurrent republish
- [ ] 4.2 Implement materialization: compiled plan → executable instances dispatched to kind owners (agent → work item, human-gate → decision, capability → broker operation) with a coordinator that tracks position-in-procedure and owner references only; verify the runtime never asks a model whether a node may run and plan-node output violating the procedure's allowlist or fan-out ceiling is rejected and recorded
- [ ] 4.3 Add late/non-authoritative result handling for pinned runs; verify a late completion after generation fencing changes no outcome and is retained as evidence

## 5. Repair boundaries and human gates

- [ ] 5.1 Implement generation unrolling (numbered generations referencing failing evidence, budget slices, idempotent keys); verify verify-fails-twice-then-passes records both generations, exhaustion escalates instead of looping, and an inconclusive or infrastructure-error port never opens a semantic generation
- [ ] 5.2 Integrate human gates with decisions: approval counts under the procedure policy (community floor 0/1, two-approval behind the feature key), expiry routing by declared port, attention-surface surfacing with triggering evidence; verify one person approving twice does not satisfy a two-approval gate
- [ ] 5.3 Record the planned-vs-observed trace (generations, gate decisions, dynamic work items from the plan node, drift classified within/outside the declared tolerance); verify the three-implement-lanes-vs-declared-four divergence classifies within policy inside the fan-out range

## 6. Editions gating and degradation

- [ ] 6.1 Apply `[EnforceLimit]` for `procedures.published-per-project` (community 3) and `procedures.concurrent-pinned-runs` (community 5), and `[RequiresFeature]` for `procedures.multi-repo` on cross-repository bindings; verify cap breaches are refused with the limit named and paid compositions lift them
- [ ] 6.2 Define expiry degradation as read-only (definitions and replay visible, no new compilations or runs); verify an expired community composition keeps pins and traces queryable and starts nothing new

## 7. Dashboard surfaces

- [ ] 7.1 Extend the OpenAPI contract and regenerate kubb types; verify `dashboard generate-api` round-trips with no hand-written drift
- [ ] 7.2 Promote the Storybook `procedures` domain to real screens: Studio (draft/compiled read, validation findings, patch diff, propose-patch and request-publication actions on React Flow with the semantic linear fallback), Live run (attention panel, pinned-version chrome, path), Replay (planned-vs-observed, drift classification); verify the storybook batch covers all three modes in both themes and the linear fallback carries the graph for narrow widths and screen readers
- [ ] 7.3 Keep the run screens honest to the existing invariants: profile as identity, brain labels as prose, permission as a row question; verify the procedures pages reuse the session/permission patterns of the runs domain

## 8. Chat / brain proposal flow

- [ ] 8.1 Add the propose-patch operation to the chat surface routed through a `Suggest`-class capability (read + draft only); verify an operator prompt yields a draft GraphPatch with rendered diff and no publication path
- [ ] 8.2 Extend the crown e2e: draft patch in chat → compile gate → human publish → admitted task pins the version → verify failure → repair generation → human gate decision → completion with planned-vs-observed replay; verify dashboard and CLI observe one consistent trace
