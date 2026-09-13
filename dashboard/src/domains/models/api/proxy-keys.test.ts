import { describe, expect, it } from "vitest"

import { proxyKeysWireToMapping } from "@/domains/models/api/proxy-keys"
import {
  budgetShare,
  expiryReading,
  keyState,
} from "@/domains/models/model/keys"

/** Now, pinned — expiry is derived from instants. */
const NOW = Date.parse("2026-09-13T12:00:00Z")

const CATALOGUE = {
  items: [
    {
      id: "sha256:abc123",
      prefix: "ck_live_9f2a",
      projectId: "b3d8a402-1111-2222-3333-444444444444",
      provider: "openai",
      baseUrl: "https://api.openai.com",
      defaultModel: "gpt-4.1",
      allowedModels: ["gpt-4.1", "gpt-4.1-mini"],
      budgetUsd: 50,
      expiresAt: new Date(NOW + 3 * 86_400_000).toISOString(),
    },
    {
      id: "sha256:def456",
      prefix: "ck_live_c71d",
      projectId: "b3d8a402-1111-2222-3333-444444444444",
      provider: "anthropic",
      baseUrl: "https://api.anthropic.com",
      defaultModel: null,
      allowedModels: [],
      budgetUsd: null,
      expiresAt: null,
    },
  ],
} as const

describe("the proxy key catalogue onto the registry", () => {
  it("carries the fingerprint, the cap and the expiry; spend stays unread", () => {
    const { keys } = proxyKeysWireToMapping(CATALOGUE, NOW)

    const capped = keys[0]
    expect(capped?.id).toBe("sha256:abc123")
    expect(capped?.prefix).toBe("ck_live_9f2a")
    expect(capped?.label).toBe("default gpt-4.1")
    expect(capped?.budgetUsd).toBe(50)
    expect(capped?.models).toEqual(["gpt-4.1", "gpt-4.1-mini"])
    expect(capped?.scope).toEqual({
      kind: "project",
      projectId: "b3d8a402-1111-2222-3333-444444444444",
    })
    // The catalogue meters nothing; the meter says "not metered" rather than
    // a zero that would read as "nothing spent".
    expect(capped?.spentUsd).toBeNull()
    expect(capped?.revoked).toBe(false)
  })

  it("reads an unlimited, never-expiring key as what it is", () => {
    const { keys } = proxyKeysWireToMapping(CATALOGUE, NOW)

    const unlimited = keys[1]
    expect(unlimited?.budgetUsd).toBeNull()
    expect(unlimited?.expiresInSec).toBeNull()
    // "no cap" is not "full": the share answers 0 and the state stays live.
    expect(budgetShare(unlimited!)).toBe(0)
    expect(keyState(unlimited!)).toBe("live")
    expect(expiryReading(unlimited!)).toBe("never")
  })

  it("derives one endpoint row per distinct upstream", () => {
    const { endpoints } = proxyKeysWireToMapping(CATALOGUE, NOW)

    expect(endpoints).toHaveLength(2)
    expect(endpoints.map((endpoint) => endpoint.name)).toEqual([
      "openai",
      "anthropic",
    ])
    expect(endpoints[1]?.wire).toBe("anthropic")
    expect(endpoints[0]?.id).toBe("openai:https://api.openai.com")
  })

  it("keeps an empty allow-list empty — the table says 'all models'", () => {
    const { keys } = proxyKeysWireToMapping(CATALOGUE, NOW)

    // Empty means every model permitted on the wire; the column's words say
    // so, and the mapper must not fill the list with a symbol.
    expect(keys[1]?.models).toEqual([])
  })
})
