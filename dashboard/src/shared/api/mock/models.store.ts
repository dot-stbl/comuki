import {
  MODELS_SEED,
  type SeedModelsSnapshot,
  type SeedVirtualKey,
} from "./models.seed"

/**
 * Mutable mock store for the model registry.
 *
 * Same reason as `compute.store.ts` and `runs.store.ts`: a query whose
 * `queryFn` maps a module constant cannot show the result of a decision,
 * because the refetch after a mutation restores the constant and the optimistic
 * write disappears about two hundred milliseconds later. Revoking a key has to
 * stay revoked, and turning the proxy on has to stay on.
 *
 * Session-scoped and in-memory by design: a reload is a fresh shift.
 */

function clone(snapshot: SeedModelsSnapshot): SeedModelsSnapshot {
  return {
    proxy: { ...snapshot.proxy },
    endpoints: snapshot.endpoints.map((endpoint) => ({
      ...endpoint,
      models: [...endpoint.models],
    })),
    keys: snapshot.keys.map((key) => ({
      ...key,
      models: [...key.models],
      scope: { ...key.scope },
    })),
    routes: snapshot.routes.map((route) => ({ ...route })),
  }
}

let state: SeedModelsSnapshot = clone(MODELS_SEED)

export function readSeedModels(): SeedModelsSnapshot {
  return state
}

/**
 * Revoke a virtual key.
 *
 * Irreversible, and modelled as such: the key stays in the list wearing its new
 * state rather than disappearing, because a registry that silently loses a row
 * cannot answer "what happened to the key that was here". The scope draft has
 * revoke travel with the lease — a worker holding one loses it mid-run — which
 * the mock cannot show, so the confirm says it instead.
 */
export function revokeSeedModelKey(keyId: string): void {
  state = {
    ...state,
    keys: state.keys.map((key) =>
      key.id === keyId ? { ...key, revoked: true } : key
    ),
  }
}

/**
 * Turn the thin proxy on or off.
 *
 * The one switch on this screen that changes what every other section *means*:
 * off, the keys are not checked, the budgets are not enforced and nothing is
 * metered. The clock resets with it, so the screen can say how long the current
 * arrangement has been in force.
 */
export function setSeedProxyEnabled(enabled: boolean): void {
  if (state.proxy.enabled === enabled) {
    return
  }
  state = {
    ...state,
    proxy: { ...state.proxy, enabled, changedAgoSec: 0 },
  }
}

/** Back to the seeded registry — used by tests and stories. */
export function resetSeedModels(): void {
  state = clone(MODELS_SEED)
}

export type { SeedModelsSnapshot, SeedVirtualKey }
