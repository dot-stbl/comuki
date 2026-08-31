import {
  COMPUTE_SEED,
  type SeedComputePool,
  type SeedComputeProvider,
  type SeedComputeSnapshot,
  type SeedWorkerVersion,
} from "./compute.seed"

/**
 * Mutable mock store for the compute registry.
 *
 * The seed is a constant, and a query whose `queryFn` maps a constant can never
 * show the result of a decision: the refetch that follows a mutation restores
 * the constant and the optimistic write disappears about 200ms later, which
 * looks exactly like a bug and is one. This holds the registry's live state for
 * the session so switching which provider takes work, or retiring a stale idle
 * pool, actually sticks — the same thing the real endpoint will do, minus the
 * wire. Same pattern as `runs.store.ts`.
 *
 * Session-scoped and in-memory by design: a reload is a fresh shift.
 */

function clone(snapshot: SeedComputeSnapshot): SeedComputeSnapshot {
  return {
    providers: snapshot.providers.map((provider) => ({
      ...provider,
      allocatable: provider.allocatable ? { ...provider.allocatable } : null,
    })),
    pools: snapshot.pools.map((pool) => ({
      ...pool,
      quota: { ...pool.quota },
      profiles: [...pool.profiles],
    })),
    versions: snapshot.versions.map((version) => ({
      ...version,
      providerIds: [...version.providerIds],
    })),
  }
}

let state: SeedComputeSnapshot = clone(COMPUTE_SEED)

export function readSeedCompute(): SeedComputeSnapshot {
  return state
}

/**
 * Hand new starts to one provider.
 *
 * The switch is a pair, never a single write: whichever provider was taking
 * work stops and goes to `draining`, because it still holds the leases it
 * handed out and refusing new starts is the only thing that changed. A registry
 * that let two providers take work at once would be describing a system the
 * orchestrator cannot be in.
 */
export function takeSeedComputeWork(providerId: string): void {
  const next = state.providers.find((provider) => provider.id === providerId)
  if (!next || next.state === "unreachable") {
    return
  }

  state = {
    ...state,
    providers: state.providers.map((provider) => {
      if (provider.id === providerId) {
        return { ...provider, takingWork: true, state: "active" }
      }
      if (provider.takingWork) {
        return { ...provider, takingWork: false, state: "draining" }
      }
      return provider
    }),
  }
}

/**
 * Tear down the idle containers on one worker label.
 *
 * Only the idle ones: a container holding a lease is doing work it claimed
 * before the roll, and killing it would fail an item that was going to land.
 * The pools lose exactly the containers the label lost, so the two readings
 * stay the same arithmetic — which is the property the seed-shape test guards.
 */
export function retireSeedComputeIdle(
  digest: string,
  profilesRef: string
): void {
  const version = state.versions.find(
    (entry) => entry.digest === digest && entry.profilesRef === profilesRef
  )
  if (!version || version.target || version.idle === 0) {
    return
  }

  const pools = drainPools(state.pools, new Set(version.providerIds), version.idle)

  state = {
    ...state,
    versions: state.versions.map((entry) =>
      entry === version
        ? { ...entry, workers: entry.workers - version.idle, idle: 0 }
        : entry
    ),
    pools,
    // Derived rather than adjusted: a provider's allocatable `used` *is* the
    // containers its pools are holding, which is the invariant the seed-shape
    // test guards. Recomputing it is one line and cannot drift; subtracting a
    // share of the teardown from it would be two numbers to keep in step.
    providers: state.providers.map((provider) =>
      provider.allocatable
        ? {
            ...provider,
            allocatable: {
              ...provider.allocatable,
              used: workersOn(pools, provider.id),
            },
          }
        : provider
    ),
  }
}

/**
 * Spread a teardown across the pools that could have been holding those
 * containers, idle-first and never below zero. The mock does not record which
 * pool each container belongs to — the real provider does — so this keeps the
 * sums honest rather than inventing an ownership the seed never claimed.
 */
function drainPools(
  pools: SeedComputePool[],
  providers: Set<string>,
  freed: number
): SeedComputePool[] {
  let left = freed
  return pools.map((pool) => {
    if (left === 0 || !providers.has(pool.providerId) || pool.idle === 0) {
      return pool
    }
    const take = Math.min(pool.idle, left)
    left -= take
    return {
      ...pool,
      workers: pool.workers - take,
      idle: pool.idle - take,
      quota: { ...pool.quota, used: Math.max(0, pool.quota.used - take) },
    }
  })
}

function workersOn(pools: SeedComputePool[], providerId: string): number {
  return pools
    .filter((pool) => pool.providerId === providerId)
    .reduce((total, pool) => total + pool.workers, 0)
}

/** Back to the seeded registry — used by tests and stories. */
export function resetSeedCompute(): void {
  state = clone(COMPUTE_SEED)
}

export type {
  SeedComputePool,
  SeedComputeProvider,
  SeedComputeSnapshot,
  SeedWorkerVersion,
}
