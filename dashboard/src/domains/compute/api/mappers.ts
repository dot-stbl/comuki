import type {
  ComputePool,
  ComputeProvider,
  ComputeSnapshot,
  Constraint,
  ProviderKind,
  WorkerVersion,
} from "@/domains/compute/model/types"
import type { ComputeSnapshotView } from "@/shared/api/_generated/types/ComputeSnapshotView"

/**
 * The wire of `GET /api/v1/compute` — the host's read-only snapshot — onto
 * the registry the screen reads.
 *
 * The snapshot is honest about what this host can see, and so is the mapping:
 * the host composes no compute engine, so there is no provider endpoint, no
 * allocatable reading and no per-label fleet count. Each of those lands as
 * the null the model now carries, and the screen draws a dash (or "no
 * answer", the reading it already had for a silent capacity API) rather
 * than a number invented to fill a track.
 *
 * The wire shape is the kubb-generated `ComputeSnapshotView` (the spec
 * declares the response schema now); the spec types its counters as
 * `number | string` (the serializer may read numbers from strings), so the
 * numeric reads go through `Number()` at this edge and the domain keeps
 * plain numbers.
 */

function toProviderKind(provider: string): ProviderKind {
  return provider === "kubernetes" ? "kubernetes" : "docker"
}

/** A snapshot onto the registry's three shapes. */
export function computeSnapshotWireToSnapshot(
  wire: ComputeSnapshotView
): ComputeSnapshot {
  const kind = toProviderKind(wire.provider)

  // One provider row, and it is the configuration's own word: the host
  // selects exactly one compute provider at boot, and it is the one taking
  // new starts by definition. The endpoint column reads the honest dash —
  // the snapshot carries no dialled address — and `allocatable` stays null,
  // which is the reading the capacity track already knows how to draw ("no
  // answer") rather than a new one invented here.
  const provider: ComputeProvider = {
    id: wire.provider,
    kind,
    endpoint: "—",
    state: "active",
    takingWork: true,
    allocatable: null,
    note: "read-only snapshot — the provider key comes from configuration; this host composes no compute engine",
  }

  const pools: ComputePool[] = wire.pools.map((pool) => ({
    projectId: pool.projectId,
    providerId: wire.provider,
    minIdle: Number(pool.minIdle),
    // The snapshot's ceiling is the project's concurrency cap; the idle
    // ceiling is a knob it does not carry, and the card says the floor alone
    // rather than dressing one number as the other.
    maxIdle: null,
    // "Workers" on this screen reads as containers holding leases; the
    // snapshot counts exactly that (`running`), and the queued depth beside
    // it is the pool's own pressure reading.
    workers: Number(pool.running),
    idle: null,
    quota: {
      used: Number(pool.running),
      limit: Number(pool.maxConcurrent),
      source: "project concurrency cap",
    } satisfies Constraint,
    profiles: [pool.profileKey],
  }))

  // The snapshot knows the label new starts use and nothing about the fleet
  // on any other label — so there is exactly one row, the target, and its
  // counts read as unknown rather than zero.
  const versions: WorkerVersion[] = [
    {
      digest: wire.defaults.workerImage,
      profilesRef: wire.defaults.profilesGitRef,
      target: true,
      workers: null,
      idle: null,
      oldestUpSec: null,
      providerIds: [wire.provider],
    },
  ]

  return { providers: [provider], pools, versions }
}
