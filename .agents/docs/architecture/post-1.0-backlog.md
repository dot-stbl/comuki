# Post-1.0 backlog (deferred to v2)

> 4 issues closed with a "v2 backlog" comment on `master` at `fa659fd`
> (2026-09-08): **#47**, **#48**, **#49**, **#50**. They were carved out of
> the `#11` Post-1.0 backlog slice at the close of v1.0 because their
> scope needs a v2-sized sprint, not a v1.1 patch.

## The four deferred issues

### #47 — Generic-command runner-container (Process.Start isolation)

**Why deferred.** Today's `GenericCommandVerifierWorker`
(`platform/src/modules/Verify/...`) launches verification commands via
`ProcessRunner` directly on the host — the same process the API is
running on. That works for read-only / lint / test commands but is a
real isolation boundary for anything that builds, runs services, or
touches the network. The v2 scope is a dedicated container image
(`ghcr.io/comuki/verifier:dev`) with the same shape as the Translator
worker image, plus a gRPC or HTTP contract for the host to enqueue
verifications on it.

**Touches.** `Comuki.Modules.Verify` · `Comuki.Engine.Compute`
(provider surface for ephemeral verifier containers) · worker
image + `deploy/worker.Dockerfile`.

### #48 — Fleet runner host-agent for bare-metal

**Why deferred.** Today every worker is a container the orchestrator
spawns (Docker dev, k8s prod via `KubernetesComputeProvider`). Bare-metal
hosts — the operator's own laptops, on-prem servers without a
container runtime — can't run workers without an agent. The v2 scope
is a long-running `comuki-runner` binary that registers itself with
the orchestrator over the same claim API the container workers use,
polls `host-class:baremetal` work items, and runs the Translator +
pi pipeline locally. Idempotent (one runner per host), authenticated
with the same API-key scheme the dashboard uses.

**Touches.** New package `agents/comuki-runner/` · new worker
claim filter in `Comuki.Engine.Orchestration` · runner registration
endpoint + heartbeat in `Comuki.Host`.

### #49 — Autonomy ratchet continuation (confidence scoring + daily decay)

**Why deferred.** v1 ships the passive `TrustClassRatchetSweeper` —
runs that succeed get auto-promoted `Supervised` → `Trusted` after a
configurable timeout; `Trusted` → `Autonomous` is gated on a human
approval flow. The v2 scope is **confidence scoring** (a Bayesian
update over the run's history: success rate, escalation rate, review
density) that drives auto-promotion / auto-demotion without a human
in the loop, plus a **daily decay** that demotes `Autonomous` back to
`Trusted` after 30 days of inactivity so an idle track doesn't keep
its autonomy indefinitely.

**Touches.** `Comuki.Engine.Orchestration` (status machine
extension) · `Comuki.Host` (sweep interval + decay config) · journal
events: `trust.promoted`, `trust.demoted`, `trust.confidence_changed`.

### #50 — Merge-queue multi-feature batch + dependency ordering

**Why deferred.** The `#11` Post-1.0 backlog shipped the
**`MergeQueue`** entity (`scheduler.merge_queues`) and the
`IMergeQueueStore` interface — a row that batches multiple PRs into a
single target-branch update. The v2 scope is the **dependency-graph
walker**: a merge queue can declare inter-PR dependencies (one PR's
test run is a precondition for another's merge), the queue orders
the entries by that graph (topological sort), and rolls back the
whole batch on the first failing entry. Today the merge queue is
FIFO — fine for a single linear pipeline, wrong once two PRs touch
the same file or the same feature.

**Touches.** `Comuki.Engine.Orchestration` (MergeQueue domain
expansion) · `Comuki.Host.Translator` (per-feature worktree
coordination) · `control-plane/profiles/merge-queue.md` (the merge
profile).

## What shipped as the `MergeQueue` entity

The first half of #50 landed in the `#11` Post-1.0 slice
(`6072dd9`): the entity, the store, and the dispatcher that consumes
the queue. The dependency-graph walker is the v2 follow-up. The shipped
surface:

```csharp
public sealed record MergeQueueEntry(...)
public interface IMergeQueueStore
{
    Task<MergeQueueEntry> EnqueueAsync(MergeQueueEntry entry, ...);
    Task<IReadOnlyList<MergeQueueEntry>> ListDueAsync(...);
    Task MarkMergedAsync(MergeQueueEntryId id, ...);
    Task MarkFailedAsync(MergeQueueEntryId id, string reason, ...);
}
```

The v2 patch layers `MergeQueueDependency` rows (entry_id → depends_on_entry_id)
and a walker that re-orders `ListDueAsync` output by topological order.

## Other v2 candidates (not yet tracked)

These came up in v1 design discussions but didn't get an issue number.
Pull them in when v2 scoping begins:

- **Confidence scoring** for TrustClass ratchet (subset of #49 above).
- **Fleet host-agent** for bare-metal deployment (subset of #48 above).
- **Generic-command container isolation** (subset of #47 above).
- **Merge-queue multi-feature batch** with dependency graph (subset of #50 above).
- **Onboarding doc refresh** (post-v1, since v1 is shipped).
- **Visual regression baselines** — Phase 3.3 deviation; SB 10-only addon.
- **Kubb Zod schemas** for FE request validation (replaces manual Zod).
- **Ladle catalog parity** — v1 used Ladle; if a future migration unifies
  on Storybook again, this becomes live.
- **Real-time contracts on FE side** — `RealtimeContractEmitter` exists
  on BE (C#→C# typed SignalR DTOs); the FE-side `.ts` generation has
  not been wired. Tracked in `Comuki.Shared.Contracts.Realtime`'s unit
  project; the slice is small once we decide where the generated TS
  lives (likely `dashboard/src/shared/api/_generated/realtime/`).

## Related

- [`STATE.md`](../../STATE.md) — slice cadence and where each deferred
  issue was carved out of `#11`.
- [`ROADMAP.md`](../../ROADMAP.md) §"v2 backlog" — same list in the
  planning surface.
- `openspec/specs/...` — v1 specs that the deferred issues would
  extend; review before scoping v2 work.
