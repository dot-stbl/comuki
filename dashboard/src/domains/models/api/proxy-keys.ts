import type {
  ModelEndpoint,
  VirtualKey,
} from "@/domains/models/model/types"

/**
 * The wire of `GET /api/v1/proxy/keys` — the admin catalogue over the
 * proxy's virtual keys — onto the registry the screen reads.
 *
 * The catalogue is fingerprinted by design: raw tokens never cross the wire,
 * the fingerprint is the id revoke addresses, and the display prefix is all
 * a human ever sees again. What the catalogue does *not* carry is spend
 * (nothing meters it into this view) — that degrades to the meter's own
 * "not metered here" reading rather than a zero that would read as "nothing
 * spent".
 */

/** One row of the catalogue — the host's `ProxyKeyView`. */
export interface ProxyKeyWire {
  readonly id: string
  readonly prefix: string
  readonly projectId: string
  readonly provider: string
  readonly baseUrl: string
  readonly defaultModel: string | null
  readonly allowedModels: readonly string[]
  readonly budgetUsd: number | null
  readonly expiresAt: string | null
}

/** The catalogue envelope — the host's `ProxyKeysResponse`. */
export interface ProxyKeysResponseWire {
  readonly items: readonly ProxyKeyWire[]
}

/** The registry additions the catalogue implies: one endpoint per upstream. */
export interface ProxyKeysMapping {
  keys: VirtualKey[]
  endpoints: ModelEndpoint[]
}

function toWireKind(provider: string): "openai" | "anthropic" {
  return provider === "anthropic" ? "anthropic" : "openai"
}

/** Distinct upstreams as endpoint rows, so the route column names a real
 *  destination rather than a placeholder. */
function toEndpoints(keys: readonly ProxyKeyWire[]): ModelEndpoint[] {
  const byId = new Map<string, ModelEndpoint>()
  keys.forEach((key) => {
    const id = `${key.provider}:${key.baseUrl}`
    if (byId.has(id)) {
      return
    }
    byId.set(id, {
      id,
      name: key.provider,
      wire: toWireKind(key.provider),
      baseUrl: key.baseUrl,
      state: "ok",
      models: [...key.allowedModels],
      note: `virtual-key upstream · ${key.baseUrl}`,
    })
  })
  return [...byId.values()]
}

function toExpiresInSec(
  expiresAt: string | null,
  nowMs: number
): number | null {
  if (!expiresAt) {
    return null
  }
  const at = Date.parse(expiresAt)
  return Number.isNaN(at) ? null : Math.round((at - nowMs) / 1000)
}

/**
 * A catalogue row onto the registry's key.
 *
 * The label states what the row is for in the wire's own terms — the default
 * model, when there is one — and spend stays unread. The store the host
 * seeds is configuration-backed, so `revoked` is always false here: what was
 * revoked is simply absent from the next listing.
 */
export function proxyKeysWireToMapping(
  response: ProxyKeysResponseWire,
  nowMs: number = Date.now()
): ProxyKeysMapping {
  const keys = response.items.map(
    (key): VirtualKey => ({
      id: key.id,
      prefix: key.prefix,
      label: key.defaultModel ? `default ${key.defaultModel}` : "no default model",
      endpointId: `${key.provider}:${key.baseUrl}`,
      models: [...key.allowedModels],
      scope: { kind: "project", projectId: key.projectId },
      budgetUsd: key.budgetUsd,
      spentUsd: null,
      expiresInSec: toExpiresInSec(key.expiresAt, nowMs),
      lastUsedAgoSec: null,
      revoked: false,
    })
  )

  return { keys, endpoints: toEndpoints(response.items) }
}
