import type {
  KeyState,
  ModelEndpoint,
  ModelRoute,
  ModelWire,
  VirtualKey,
} from "./types"

/**
 * What a spend key is worth to whoever is holding it.
 *
 * The product's own idea, and the reason it matters is arithmetic rather than
 * cryptography: the route, the cap, the model list, the scope and the TTL all
 * live *inside* the key. A leaked key therefore buys one endpoint, a handful of
 * named models, whatever is left of a budget, until a date the holder does not
 * control. These functions are that sentence, made checkable.
 */

const DAY = 86_400

/**
 * A key's real state, which is not a field.
 *
 * Two of the three ways a key stops working need nobody to do anything: it runs
 * out of time, or somebody revokes it. Revocation wins over expiry when both
 * are true, because it is the deliberate act and it is what an operator went
 * and did.
 */
export function keyState(key: VirtualKey): KeyState {
  if (key.revoked) {
    return "revoked"
  }
  if (key.expiresInSec <= 0) {
    return "expired"
  }
  return "live"
}

/** True only for a key upstream would still accept. */
export function isLive(key: VirtualKey): boolean {
  return keyState(key) === "live"
}

/** How much of the cap is spent, 0–1 and uncapped above 1. */
export function budgetShare(key: VirtualKey): number {
  if (key.budgetUsd <= 0) {
    return 1
  }
  return Math.max(0, key.spentUsd / key.budgetUsd)
}

/**
 * Three readings, not a gradient.
 *
 * `near` starts at 85% because that is where a key stops being a fact and
 * starts being a thing somebody has to decide about — either raise the cap or
 * let the traffic stop. Below it there is nothing to do, and a screen that
 * colours a key at 40% has taught the operator to ignore the colour by the time
 * one reaches 90%.
 */
export function budgetHeat(key: VirtualKey): "ok" | "near" | "over" {
  const share = budgetShare(key)
  if (share >= 1) {
    return "over"
  }
  if (share >= 0.85) {
    return "near"
  }
  return "ok"
}

/** What is left under the cap, in dollars. Never negative. */
export function budgetLeftUsd(key: VirtualKey): number {
  return Math.max(0, key.budgetUsd - key.spentUsd)
}

/**
 * The TTL, in the words the column uses.
 *
 * Relative on purpose: a key's TTL is only ever read as "is this about to
 * stop", and a stamped date makes the reader do the subtraction. Past tense
 * once it has lapsed, because a lapsed key is a different thing from a key with
 * a day left and the two must not look alike.
 */
export function expiryReading(key: VirtualKey): string {
  const days = Math.round(key.expiresInSec / DAY)
  if (days === 0) {
    return key.expiresInSec > 0 ? "today" : "expired today"
  }
  if (days > 0) {
    return `in ${days} ${days === 1 ? "day" : "days"}`
  }
  const past = Math.abs(days)
  return `${past} ${past === 1 ? "day" : "days"} ago`
}

/** Where a key may be used, in the words the operator uses for it. */
export function scopeReading(
  key: VirtualKey,
  projectKey: (projectId: string) => string
): string {
  return key.scope.kind === "platform"
    ? "platform"
    : projectKey(key.scope.projectId)
}

/** The endpoint a key routes to. `undefined` only if a registry is partial. */
export function endpointOf(
  endpoints: ModelEndpoint[],
  endpointId: string
): ModelEndpoint | undefined {
  return endpoints.find((endpoint) => endpoint.id === endpointId)
}

/** The wire, spelled the way the product says it out loud. */
export function wireLabel(wire: ModelWire): string {
  return wire === "openai" ? "openai-compatible" : "anthropic-compatible"
}

/** A role and what it is doing, as one reading: `lead · plan`. */
export function routeLabel(route: ModelRoute): string {
  return `${route.role} · ${route.duty}`
}

/**
 * What the proxy being off actually costs, as a sentence.
 *
 * The switch is the one control on this screen that changes what every other
 * section means, and "proxy: off" alone does not say so. Naming the three
 * things that stop happening is the difference between a status and a warning.
 */
export function proxySentence(enabled: boolean): string {
  return enabled
    ? "Spend keys are checked, budgets are enforced and every run is metered"
    : "workers get a url and a key injected directly — Spend keys are not checked, budgets are not enforced and nothing is metered"
}

/** An hour of the burn series, in the clock's own spelling (`15:00`). */
export function hourLabel(hour: number): string {
  return `${String(hour).padStart(2, "0")}:00`
}

/** The highest hour of a burn series, and what it cost. */
export function burnPeak(
  hourly: number[]
): { hour: number; usd: number } | null {
  if (hourly.length === 0) {
    return null
  }
  let peak = { hour: 0, usd: hourly[0] ?? 0 }
  hourly.forEach((usd, hour) => {
    if (usd > peak.usd) {
      peak = { hour, usd }
    }
  })
  return peak
}

/** Keys worth looking at first: the ones about to stop, or already stopped. */
export function keyOrder(keys: VirtualKey[]): VirtualKey[] {
  const rank = (key: VirtualKey) => {
    const state = keyState(key)
    if (state === "revoked") {
      return 3
    }
    if (state === "expired") {
      return 0
    }
    return budgetHeat(key) === "ok" ? 2 : 1
  }
  return [...keys].sort((a, b) => {
    const byRank = rank(a) - rank(b)
    return byRank !== 0 ? byRank : budgetShare(b) - budgetShare(a)
  })
}

/** Live keys standing at or past 85% of their cap. */
export function keysNearCap(keys: VirtualKey[]): VirtualKey[] {
  return keys.filter((key) => isLive(key) && budgetHeat(key) !== "ok")
}

/** Keys that stopped working without anybody revoking them. */
export function expiredKeys(keys: VirtualKey[]): VirtualKey[] {
  return keys.filter((key) => keyState(key) === "expired")
}
