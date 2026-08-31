/**
 * The model registry — what the swarm is allowed to think with, and what it
 * costs.
 *
 * Fictional, like every other seed in this folder: no base url, key prefix,
 * budget or model name below belongs to a real vendor or a real account. The
 * vendor handles follow the convention `settings.seed.ts` already set
 * (`provider-A`, `provider-B`) rather than naming anybody, and the model names
 * describe a class and a size because that is all the product ever needs to
 * say about one.
 *
 * The shape follows the v1 scope draft (§6 Proxy / модели):
 *
 *   - the wire is **OpenAI-compatible or Anthropic-compatible**, and nothing
 *     else; a self-hosted url is an ordinary row, not a special case.
 *   - the thin proxy is **optional** — a developer may turn it off entirely,
 *     and this seed has it off, because that is the state that otherwise never
 *     gets designed. With it off, workers get a url and a key injected
 *     directly: virtual keys stop being checked, budgets stop being enforced,
 *     and cost stops being metered. The registry still reads correctly; what
 *     changes is that nothing in it is being applied.
 *   - a **virtual key** carries its own route, budget, model list, scope and
 *     TTL, which is what makes a leaked one nearly useless.
 *
 * Times are relative rather than stamped, the way `queue.seed.ts` keeps ages
 * and leases relative: a seeded date is a mock that starts failing on a Tuesday
 * six months from now, and a test that has to freeze the clock to read it.
 */

/** The two wires v1 speaks. Everything upstream is one of these. */
export const MODEL_WIRES = ["openai", "anthropic"] as const

export type SeedModelWire = (typeof MODEL_WIRES)[number]

/** Whether an upstream is usable, usable-but-worse, or deliberately parked. */
export const ENDPOINT_STATES = ["ok", "degraded", "disabled"] as const

export type SeedEndpointState = (typeof ENDPOINT_STATES)[number]

export interface SeedModelEndpoint {
  id: string
  /** The handle an operator calls it by. A value, not prose. */
  name: string
  wire: SeedModelWire
  baseUrl: string
  state: SeedEndpointState
  /** Physical models reachable on this wire. */
  models: string[]
  note: string
}

/** Where a virtual key may be used. Platform keys are not project-scoped. */
export type SeedKeyScope =
  | { kind: "platform" }
  | { kind: "project"; projectId: string }

export interface SeedVirtualKey {
  id: string
  /**
   * All of the key that is ever displayed. The secret is shown once at
   * creation and never again — this is the handle that stands in for it
   * afterwards, everywhere.
   */
  prefix: string
  label: string
  /** The upstream this key routes to. One key, one route. */
  endpointId: string
  /** Physical models this key may reach — a subset of the endpoint's. */
  models: string[]
  scope: SeedKeyScope
  budgetUsd: number
  spentUsd: number
  /**
   * Seconds until the key stops working; negative once it already has.
   * Relative, so the seed does not expire on a calendar the tests cannot see.
   */
  expiresInSec: number
  /** Seconds since the key was last used upstream. `null` if it never was. */
  lastUsedAgoSec: number | null
  revoked: boolean
}

/**
 * A model role, resolved.
 *
 * The platform speaks in roles, never in vendors: the lead does the thinking —
 * plan, contract, review, repair — and the worker runs the profile steps. This
 * table is where a role becomes a physical model on a physical endpoint, and it
 * is the only place that mapping exists.
 */
export interface SeedModelRoute {
  role: "lead" | "worker"
  /** What the role is doing when this row applies. */
  duty: string
  model: string
  endpointId: string
  note: string
}

/**
 * The thin proxy: virtual key, budget, cost-per-run, revoke-with-lease.
 *
 * `enabled: false` here on purpose. The figures are what it last metered, kept
 * so the screen can say what turning it off costs in visibility rather than
 * showing an empty panel — and marked stale everywhere they appear, because a
 * six-day-old cost-per-run presented as current is worse than no figure.
 */
export interface SeedProxy {
  enabled: boolean
  /** Seconds since the switch was last thrown. */
  changedAgoSec: number
  /** The window the figures below cover. */
  windowLabel: string
  runs: number
  spendUsd: number
  costPerRunUsd: number
}

export interface SeedModelsSnapshot {
  proxy: SeedProxy
  endpoints: SeedModelEndpoint[]
  keys: SeedVirtualKey[]
  routes: SeedModelRoute[]
}

const DAY = 86_400

/* ---------------------------------------------------------------------------
 * Upstreams. Four rows across the two wires, and one of them is a url on the
 * cluster's own network — deliberately ordinary. A self-hosted endpoint is a
 * base url like any other; giving it a badge of its own would be inventing a
 * distinction the product does not make.
 * ------------------------------------------------------------------------- */

export const MODEL_ENDPOINTS_SEED: SeedModelEndpoint[] = [
  {
    id: "ep_vendor_a",
    name: "provider-A",
    wire: "anthropic",
    baseUrl: "https://api.provider-a.example/v1",
    state: "ok",
    models: ["lead-xl-2", "lead-mid-2"],
    note: "lead traffic",
  },
  {
    id: "ep_vendor_b",
    name: "provider-B",
    wire: "openai",
    baseUrl: "https://api.provider-b.example/v1",
    state: "ok",
    models: ["worker-sm-4", "judge-mid-1"],
    note: "worker steps and the diff gate",
  },
  {
    id: "ep_self_host",
    name: "self-hosted",
    wire: "openai",
    baseUrl: "http://vllm.comuki.internal:8000/v1",
    state: "degraded",
    models: ["worker-sm-oss"],
    note: "p95 four times the others since the node reboot",
  },
  {
    id: "ep_vendor_c",
    name: "provider-C",
    wire: "openai",
    baseUrl: "https://api.provider-c.example/v1",
    state: "disabled",
    models: ["lead-xl-1"],
    note: "kept configured for the rollback, refuses traffic",
  },
]

/* ---------------------------------------------------------------------------
 * Virtual keys. The product's own idea, and the seed is arranged so the screen
 * has to show why it matters: everything that constrains a key — its route, its
 * cap, its model list, its scope and its TTL — is inside the key, so a leaked
 * one buys an attacker one endpoint, a handful of models and whatever is left
 * of a budget that expires on a date it does not control.
 *
 *   vk_7f2c  at 90% of its cap, and nothing is enforcing it
 *   vk_be04  three days past its TTL — dead without anyone revoking it
 *   vk_11ab  already revoked, so the resting state after the act is visible
 * ------------------------------------------------------------------------- */

export const VIRTUAL_KEYS_SEED: SeedVirtualKey[] = [
  {
    id: "vk_7f2c",
    prefix: "vk_7f2c…",
    label: "platform lead traffic",
    endpointId: "ep_vendor_a",
    models: ["lead-xl-2", "lead-mid-2"],
    scope: { kind: "platform" },
    budgetUsd: 400,
    spentUsd: 361.4,
    expiresInSec: 12 * DAY,
    lastUsedAgoSec: 6 * DAY,
    revoked: false,
  },
  {
    id: "vk_3a91",
    prefix: "vk_3a91…",
    label: "comuki workers",
    endpointId: "ep_vendor_b",
    models: ["worker-sm-4"],
    scope: { kind: "project", projectId: "p_comuki" },
    budgetUsd: 250,
    spentUsd: 88.1,
    expiresInSec: 46 * DAY,
    lastUsedAgoSec: 6 * DAY,
    revoked: false,
  },
  {
    // Past its TTL and nobody had to do anything: the key stopped working on
    // its own, which is the entire argument for putting a TTL inside the key.
    id: "vk_be04",
    prefix: "vk_be04…",
    label: "plexor self-hosted trial",
    endpointId: "ep_self_host",
    models: ["worker-sm-oss"],
    scope: { kind: "project", projectId: "p_plexor" },
    budgetUsd: 60,
    spentUsd: 12.05,
    expiresInSec: -3 * DAY,
    lastUsedAgoSec: 4 * DAY,
    revoked: false,
  },
  {
    id: "vk_d55e",
    prefix: "vk_d55e…",
    label: "atlas diff gate",
    endpointId: "ep_vendor_b",
    models: ["judge-mid-1"],
    scope: { kind: "project", projectId: "p_atlas" },
    budgetUsd: 120,
    spentUsd: 41.2,
    expiresInSec: 27 * DAY,
    lastUsedAgoSec: 7 * DAY,
    revoked: false,
  },
  {
    id: "vk_11ab",
    prefix: "vk_11ab…",
    label: "ci smoke",
    endpointId: "ep_vendor_b",
    models: ["worker-sm-4"],
    scope: { kind: "platform" },
    budgetUsd: 25,
    spentUsd: 3.9,
    expiresInSec: 61 * DAY,
    lastUsedAgoSec: null,
    revoked: true,
  },
]

/* ---------------------------------------------------------------------------
 * Role → model. Five rows, and the lead's four duties are four rows rather than
 * one, because they do not all resolve the same way: review is cheaper work
 * than planning, and the whole point of routing by role is that somebody gets
 * to make that call in one table instead of in four prompts.
 * ------------------------------------------------------------------------- */

export const MODEL_ROUTES_SEED: SeedModelRoute[] = [
  {
    role: "lead",
    duty: "plan",
    model: "lead-xl-2",
    endpointId: "ep_vendor_a",
    note: "decomposes a ticket into a work-item graph",
  },
  {
    role: "lead",
    duty: "contract",
    model: "lead-xl-2",
    endpointId: "ep_vendor_a",
    note: "writes the brief each profile is handed",
  },
  {
    role: "lead",
    duty: "review",
    model: "lead-mid-2",
    endpointId: "ep_vendor_a",
    note: "reads results back; cheaper than planning on purpose",
  },
  {
    role: "lead",
    duty: "repair",
    model: "lead-xl-2",
    endpointId: "ep_vendor_a",
    note: "picks up a run after two failed worker retries",
  },
  {
    role: "worker",
    duty: "steps",
    model: "worker-sm-4",
    endpointId: "ep_vendor_b",
    note: "every profile step in a container",
  },
]

/* ---------------------------------------------------------------------------
 * The proxy. Off, and off for six days — which is why a key at ninety percent
 * of its cap is still being spent against.
 * ------------------------------------------------------------------------- */

export const PROXY_SEED: SeedProxy = {
  enabled: false,
  changedAgoSec: 6 * DAY,
  windowLabel: "the seven days before it was turned off",
  runs: 412,
  spendUsd: 168.6,
  costPerRunUsd: 0.41,
}

export const MODELS_SEED: SeedModelsSnapshot = {
  proxy: PROXY_SEED,
  endpoints: MODEL_ENDPOINTS_SEED,
  keys: VIRTUAL_KEYS_SEED,
  routes: MODEL_ROUTES_SEED,
}
