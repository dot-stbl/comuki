import { beforeEach, describe, expect, it } from "vitest"

import { MODELS_SEED } from "@/shared/api/mock/models.seed"
import {
  readSeedModels,
  resetSeedModels,
  revokeSeedModelKey,
  setSeedProxyEnabled,
} from "@/shared/api/mock/models.store"

import {
  budgetHeat,
  budgetLeftUsd,
  budgetShare,
  expiredKeys,
  expiryReading,
  isLive,
  keyOrder,
  keyState,
  keysNearCap,
  proxySentence,
  routeLabel,
  scopeReading,
  wireLabel,
} from "./keys"
import type { VirtualKey } from "./types"

const DAY = 86_400

function key(id: string, overrides: Partial<VirtualKey> = {}): VirtualKey {
  return {
    id,
    prefix: `${id}…`,
    label: "a key",
    endpointId: "ep_a",
    models: ["worker-sm-4"],
    scope: { kind: "platform" },
    budgetUsd: 100,
    spentUsd: 10,
    expiresInSec: 30 * DAY,
    lastUsedAgoSec: DAY,
    revoked: false,
    ...overrides,
  }
}

describe("a key's state is a reading, not a field", () => {
  it("calls a key past its ttl expired without anyone revoking it", () => {
    // The whole argument for putting the TTL inside the key: it stops working
    // on its own, and the screen has to say so rather than showing a live key
    // that quietly fails upstream.
    const lapsed = key("vk_old", { expiresInSec: -3 * DAY })

    expect(keyState(lapsed)).toBe("expired")
    expect(isLive(lapsed)).toBe(false)
    expect(expiryReading(lapsed)).toBe("3 days ago")
  })

  it("keeps a key with time left live, and says how much", () => {
    const live = key("vk_new", { expiresInSec: 12 * DAY })

    expect(keyState(live)).toBe("live")
    expect(expiryReading(live)).toBe("in 12 days")
  })

  it("counts the last day and the first lapsed one apart", () => {
    expect(expiryReading(key("a", { expiresInSec: DAY }))).toBe("in 1 day")
    expect(expiryReading(key("b", { expiresInSec: 3_600 }))).toBe("today")
    expect(expiryReading(key("c", { expiresInSec: -3_600 }))).toBe(
      "expired today"
    )
    expect(expiryReading(key("d", { expiresInSec: -DAY }))).toBe("1 day ago")
  })

  it("lets a revocation win over an expiry", () => {
    // Both are true; only one of them is something a person went and did, and
    // that is the one the row has to show.
    const both = key("vk_x", { expiresInSec: -DAY, revoked: true })

    expect(keyState(both)).toBe("revoked")
  })
})

describe("a key's budget", () => {
  it("marks a key at ninety percent of its cap as one to decide about", () => {
    const near = key("vk_hot", { budgetUsd: 400, spentUsd: 361.4 })

    expect(budgetShare(near)).toBeCloseTo(0.9035, 4)
    expect(budgetHeat(near)).toBe("near")
    expect(budgetLeftUsd(near)).toBeCloseTo(38.6, 2)
  })

  it("leaves a key with real room alone", () => {
    // Three readings rather than a gradient: a screen that colours a key at
    // forty percent has taught the operator to ignore the colour by ninety.
    expect(budgetHeat(key("vk_cool", { spentUsd: 40 }))).toBe("ok")
    expect(budgetHeat(key("vk_edge", { spentUsd: 84.9 }))).toBe("ok")
    expect(budgetHeat(key("vk_warn", { spentUsd: 85 }))).toBe("near")
    expect(budgetHeat(key("vk_over", { spentUsd: 140 }))).toBe("over")
  })

  it("never reports a negative remainder", () => {
    expect(budgetLeftUsd(key("vk_over", { spentUsd: 140 }))).toBe(0)
  })

  it("treats a cap of zero as spent rather than as unlimited", () => {
    expect(budgetShare(key("vk_zero", { budgetUsd: 0, spentUsd: 0 }))).toBe(1)
  })
})

describe("the order the keys are read in", () => {
  it("puts what already stopped ahead of what is about to", () => {
    // A run failing on an expired key is happening now; a cap is a decision
    // somebody still has time to make. Revoked keys are history and sort last,
    // kept so the registry can answer what happened to the key that was here.
    const keys = [
      key("vk_ok", { spentUsd: 10 }),
      key("vk_revoked", { revoked: true }),
      key("vk_near", { spentUsd: 92 }),
      key("vk_expired", { expiresInSec: -DAY }),
    ]

    expect(keyOrder(keys).map((entry) => entry.id)).toEqual([
      "vk_expired",
      "vk_near",
      "vk_ok",
      "vk_revoked",
    ])
  })

  it("only counts a live key as near its cap", () => {
    const keys = [
      key("vk_dead_hot", { spentUsd: 95, expiresInSec: -DAY }),
      key("vk_live_hot", { spentUsd: 95 }),
    ]

    expect(keysNearCap(keys).map((entry) => entry.id)).toEqual(["vk_live_hot"])
    expect(expiredKeys(keys).map((entry) => entry.id)).toEqual(["vk_dead_hot"])
  })
})

describe("the words the screen says", () => {
  it("spells both wires the way the product says them out loud", () => {
    expect(wireLabel("openai")).toBe("openai-compatible")
    expect(wireLabel("anthropic")).toBe("anthropic-compatible")
  })

  it("names a key's scope with the handle an operator uses", () => {
    const projectKey = (id: string) => (id === "p_atlas" ? "atlas" : id)

    expect(scopeReading(key("a"), projectKey)).toBe("platform")
    expect(
      scopeReading(
        key("b", { scope: { kind: "project", projectId: "p_atlas" } }),
        projectKey
      )
    ).toBe("atlas")
  })

  it("reads a role together with what it is doing", () => {
    expect(
      routeLabel({
        role: "lead",
        duty: "repair",
        model: "lead-xl-2",
        endpointId: "ep_a",
        note: "",
      })
    ).toBe("lead · repair")
  })

  it("says what the proxy being off actually stops, not just that it is off", () => {
    const off = proxySentence(false)

    expect(off).toContain("virtual keys are not checked")
    expect(off).toContain("budgets are not enforced")
    expect(off).toContain("nothing is metered")
    expect(proxySentence(true)).toContain("every run is metered")
  })
})

describe("the models seed and its store", () => {
  beforeEach(() => {
    resetSeedModels()
  })

  it("seeds every state the screen has to be able to show", () => {
    expect(MODELS_SEED.proxy.enabled).toBe(false)
    expect(keysNearCap(MODELS_SEED.keys).length).toBeGreaterThan(0)
    expect(expiredKeys(MODELS_SEED.keys).length).toBeGreaterThan(0)
    expect(MODELS_SEED.keys.some((entry) => entry.revoked)).toBe(true)
    expect(
      MODELS_SEED.endpoints.some((entry) => entry.state === "degraded")
    ).toBe(true)
    // Both wires, and a self-hosted url that is an ordinary row.
    expect(new Set(MODELS_SEED.endpoints.map((entry) => entry.wire)).size).toBe(
      2
    )
    expect(
      MODELS_SEED.endpoints.some((entry) => entry.baseUrl.includes(".internal"))
    ).toBe(true)
    // The lead's four duties plus the worker's steps.
    expect(
      MODELS_SEED.routes.filter((route) => route.role === "lead")
    ).toHaveLength(4)
  })

  it("keeps a revoked key in the registry wearing its new state", () => {
    // A registry that loses a row cannot answer what happened to the key that
    // used to be here, and that is the question asked the morning after.
    const live = MODELS_SEED.keys.find((entry) => keyState(entry) === "live")!
    revokeSeedModelKey(live.id)

    const after = readSeedModels().keys.find((entry) => entry.id === live.id)
    expect(after).toBeDefined()
    expect(keyState(after!)).toBe("revoked")
  })

  it("resets the clock when the proxy switch is thrown", () => {
    setSeedProxyEnabled(true)

    expect(readSeedModels().proxy.enabled).toBe(true)
    expect(readSeedModels().proxy.changedAgoSec).toBe(0)
  })

  it("routes every key and every role at an endpoint that exists", () => {
    const ids = new Set(MODELS_SEED.endpoints.map((entry) => entry.id))

    for (const entry of MODELS_SEED.keys) {
      expect(ids.has(entry.endpointId)).toBe(true)
    }
    for (const route of MODELS_SEED.routes) {
      expect(ids.has(route.endpointId)).toBe(true)
      // A key may only name models its own endpoint actually serves.
      const endpoint = MODELS_SEED.endpoints.find(
        (candidate) => candidate.id === route.endpointId
      )!
      expect(endpoint.models).toContain(route.model)
    }
    for (const entry of MODELS_SEED.keys) {
      const endpoint = MODELS_SEED.endpoints.find(
        (candidate) => candidate.id === entry.endpointId
      )!
      for (const model of entry.models) {
        expect(endpoint.models).toContain(model)
      }
    }
  })
})
