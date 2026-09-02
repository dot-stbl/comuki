/**
 * The model registry as the screen sees it.
 *
 * Four records, and the first one changes what the other three mean. The
 * **proxy** is optional in v1 — a developer may turn it off entirely — and with
 * it off the keys are not checked, the budgets are not enforced and nothing is
 * metered. An **endpoint** is an upstream on one of two wires. A **virtual
 * key** carries its own route, cap, model list, scope and TTL. A **route** is
 * the resolution of a model role to a physical model, which is the only place
 * that mapping exists.
 */

/** The two wires v1 speaks. A self-hosted url is one of these like any other. */
export type ModelWire = "openai" | "anthropic"

export type EndpointState = "ok" | "degraded" | "disabled"

export interface ModelEndpoint {
  id: string
  name: string
  wire: ModelWire
  baseUrl: string
  state: EndpointState
  models: string[]
  note: string
}

export type KeyScope =
  { kind: "platform" } | { kind: "project"; projectId: string }

/** What a key is, as opposed to what it was configured to be. Derived. */
export type KeyState = "live" | "expired" | "revoked"

export interface VirtualKey {
  id: string
  /** All of the key that is ever displayed after it is stored. */
  prefix: string
  label: string
  endpointId: string
  models: string[]
  scope: KeyScope
  budgetUsd: number
  spentUsd: number
  /** Seconds until it stops working; negative once it already has. */
  expiresInSec: number
  lastUsedAgoSec: number | null
  revoked: boolean
}

/**
 * A model role resolved to a physical model.
 *
 * The platform speaks in roles, never in vendors: the lead thinks — plan,
 * contract, review, repair — and the worker runs profile steps. The four lead
 * duties are four rows because they do not all resolve the same way.
 */
export interface ModelRoute {
  role: "lead" | "worker"
  duty: string
  model: string
  endpointId: string
  note: string
}

export interface Proxy {
  enabled: boolean
  changedAgoSec: number
  windowLabel: string
  runs: number
  spendUsd: number
  costPerRunUsd: number
  /**
   * Spend by hour across the last metered day, midnight-first. With the proxy
   * off nothing is being metered now, and the series is the shape of the day
   * before the switch — marked with the same staleness the figures carry.
   */
  burnHourlyUsd: number[]
}

export interface ModelsSnapshot {
  proxy: Proxy
  endpoints: ModelEndpoint[]
  keys: VirtualKey[]
  routes: ModelRoute[]
}
