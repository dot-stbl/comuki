import { afterEach, describe, expect, it } from "vitest"

import { PROJECTS_SEED } from "@/shared/api/mock/session.seed"
import { SOURCES_SEED } from "@/shared/api/mock/sources.seed"
import {
  connectSeedSource,
  createSeedNativeTicket,
  disconnectSeedSource,
  probeSeedConnection,
  probeSeedSourceDraft,
  readSeedSources,
  resetSeedSources,
  updateSeedWatch,
  type SeedSourceDraft,
} from "@/shared/api/mock/sources.store"

/**
 * The seeded connections, as a contract.
 *
 * The screen's whole job is to make the awkward states legible, so the seed has
 * to actually contain them. A mock that quietly settled into eight healthy
 * GitHub connections would let the error copy, the idle-watch copy and the
 * self-hosted column rot while every gate still ran green.
 */

afterEach(() => {
  resetSeedSources()
})

const PROJECT_IDS = new Set(PROJECTS_SEED.map((project) => project.id))

describe("the seeded connections", () => {
  it("puts every connection in a project the shift can name", () => {
    for (const connection of SOURCES_SEED.connections) {
      expect(PROJECT_IDS.has(connection.projectId)).toBe(true)
    }
  })

  it("gives every project exactly one native intake, and marks it unremovable", () => {
    const native = SOURCES_SEED.connections.filter(
      (connection) => connection.kind === "native"
    )

    expect(native).toHaveLength(PROJECTS_SEED.length)
    expect(new Set(native.map((entry) => entry.projectId)).size).toBe(
      PROJECTS_SEED.length
    )
    for (const entry of native) {
      expect(entry.removable).toBe(false)
      // No watch at all, rather than a watch that is off: there is no remote
      // system to watch, and "off" would be a lie about a switch that does not
      // exist.
      expect(entry.watch).toBeNull()
      expect(entry.auth).toBe("none")
    }
  })

  it("carries all five provider kinds", () => {
    const kinds = new Set(
      SOURCES_SEED.connections.map((connection) => connection.kind)
    )
    expect(kinds).toEqual(
      new Set(["github", "gitlab", "yandex-tracker", "jira", "native"])
    )
  })

  it("holds one broken connection, and it says why in a sentence", () => {
    const broken = SOURCES_SEED.connections.filter(
      (connection) => connection.state === "error"
    )

    expect(broken.length).toBeGreaterThan(0)
    for (const connection of broken) {
      // Not a bare code: "401" tells an operator nothing they can act on, and
      // the sentence is the whole reason the state is worth showing.
      expect(connection.reason).toBeTruthy()
      expect((connection.reason ?? "").length).toBeGreaterThan(24)
    }
  })

  it("holds a self-hosted instance with a base url, and cloud ones without", () => {
    const selfHosted = SOURCES_SEED.connections.filter(
      (connection) => connection.selfHosted
    )
    expect(selfHosted.length).toBeGreaterThan(0)
    for (const connection of selfHosted) {
      expect(connection.baseUrl?.startsWith("https://")).toBe(true)
    }

    const github = SOURCES_SEED.connections.filter(
      (connection) => connection.kind === "github"
    )
    for (const connection of github) {
      expect(connection.selfHosted).toBe(false)
      expect(connection.baseUrl).toBeUndefined()
    }
  })

  it("holds a healthy watch that is admitting nothing", () => {
    // The state that looks like a broken screen and is not one. If this stops
    // being in the seed, the copy that explains it stops being exercised.
    const idle = SOURCES_SEED.connections.filter(
      (connection) =>
        connection.state === "connected" &&
        connection.watch?.enabled === true &&
        connection.watch.matched === 0
    )
    expect(idle.length).toBeGreaterThan(0)
  })

  it("holds a connection somebody turned off on purpose", () => {
    expect(
      SOURCES_SEED.connections.some(
        (connection) => connection.state === "disabled"
      )
    ).toBe(true)
  })

  it("reaches all three admission modes across the seed", () => {
    const modes = new Set(
      SOURCES_SEED.connections
        .map((connection) => connection.watch?.mode)
        .filter(Boolean)
    )
    expect(modes).toEqual(new Set(["watch", "inbox-only", "both"]))
  })

  it("never writes a secret down", () => {
    // Structural, not stylistic: there is no field on a connection that could
    // hold one. What survives a save is a date.
    for (const connection of SOURCES_SEED.connections) {
      expect(Object.keys(connection)).not.toContain("secret")
      expect(Object.keys(connection)).not.toContain("token")
    }
  })
})

describe("native intake refuses to be disconnected", () => {
  it("says no in the store, not only on the button", () => {
    const native = readSeedSources().connections.find(
      (connection) => connection.kind === "native"
    )

    expect(disconnectSeedSource(native!.id)).toBe(false)
    expect(
      readSeedSources().connections.some((entry) => entry.id === native!.id)
    ).toBe(true)
  })

  it("lets an ordinary connection go", () => {
    const removable = readSeedSources().connections.find(
      (connection) => connection.removable
    )

    expect(disconnectSeedSource(removable!.id)).toBe(true)
    expect(
      readSeedSources().connections.some((entry) => entry.id === removable!.id)
    ).toBe(false)
  })
})

describe("a decision sticks", () => {
  it("keeps a new connection across a re-read", () => {
    const draft: SeedSourceDraft = {
      projectId: "p_atlas",
      kind: "gitlab",
      name: "atlas/ledger-core",
      auth: "pat",
      account: "svc-comuki",
      baseUrl: "https://git.atlas.internal",
    }

    const before = readSeedSources().connections.length
    const created = connectSeedSource(draft)

    // The point of the store: a query that mapped the seed constant would
    // restore it on the refetch and the row would vanish 200 ms later.
    const after = readSeedSources()
    expect(after.connections).toHaveLength(before + 1)
    expect(after.connections.some((entry) => entry.id === created.id)).toBe(
      true
    )
    expect(created.selfHosted).toBe(true)
    // It arrives with its watch off: admitting tickets is a separate decision.
    expect(created.watch?.enabled).toBe(false)
  })

  it("stores a filter expression exactly as it was typed", () => {
    const connection = readSeedSources().connections.find(
      (entry) => entry.watch !== null
    )
    const messy = '  labels: swarm, area/x\nprojects = web-app,  "half'

    updateSeedWatch(connection!.id, {
      enabled: true,
      filter: messy,
      mode: "both",
    })

    const saved = readSeedSources().connections.find(
      (entry) => entry.id === connection!.id
    )
    // Nothing trims, tokenises or normalises it. The language is undecided, so
    // the store's only job is not to have an opinion.
    expect(saved?.watch?.filter).toBe(messy)
    expect(saved?.watch?.mode).toBe("both")
  })

  it("zeroes the admitted count when the watch goes off", () => {
    const connection = readSeedSources().connections.find(
      (entry) => entry.watch?.enabled && entry.watch.matched > 0
    )

    updateSeedWatch(connection!.id, {
      enabled: false,
      filter: connection!.watch!.filter,
      mode: connection!.watch!.mode,
    })

    const saved = readSeedSources().connections.find(
      (entry) => entry.id === connection!.id
    )
    // Yesterday's count under a switch that is off would be the screen's most
    // confusing number.
    expect(saved?.watch?.matched).toBe(0)
  })

  it("keeps a filed ticket across a re-read", () => {
    const before = readSeedSources().tickets.length
    createSeedNativeTicket({
      projectId: "p_comuki",
      title: "the rail forgets its width on reload",
      body: "",
      labels: ["web-app"],
      straightToWork: false,
    })

    expect(readSeedSources().tickets).toHaveLength(before + 1)
  })
})

describe("testing a connection that already exists", () => {
  it("fails again, with the same sentence, while the token is still revoked", () => {
    const broken = readSeedSources().connections.find(
      (entry) => entry.state === "error"
    )

    const result = probeSeedConnection(broken!.id)

    expect(result.ok).toBe(false)
    // Pressing test does not un-revoke a token, and an answer that changed
    // would be the mock lying about what the provider said.
    expect(result.message).toBe(broken!.reason)
  })

  it("moves the last sync forward when it succeeds", () => {
    const healthy = readSeedSources().connections.find(
      (entry) => entry.state === "connected" && entry.kind !== "native"
    )

    expect(probeSeedConnection(healthy!.id).ok).toBe(true)
    expect(
      readSeedSources().connections.find((entry) => entry.id === healthy!.id)
        ?.lastSyncAt
    ).toBe("just now")
  })

  it("says there is nothing to reach for native intake", () => {
    const native = readSeedSources().connections.find(
      (entry) => entry.kind === "native"
    )

    const result = probeSeedConnection(native!.id)
    expect(result.ok).toBe(true)
    expect(result.message).toContain("nothing to reach")
  })
})

describe("testing details that have not been saved", () => {
  const base: SeedSourceDraft = {
    projectId: "p_test",
    kind: "github",
    name: "here/web-app",
    auth: "pat",
    account: "svc-bot",
    baseUrl: "",
  }

  it("refuses a self-hosted provider with no instance named", () => {
    const result = probeSeedSourceDraft(
      { ...base, kind: "gitlab", baseUrl: "" },
      "a-long-enough-token"
    )
    expect(result.ok).toBe(false)
    expect(result.message).toContain("no base url")
  })

  it("refuses to send a credential over plain http", () => {
    const result = probeSeedSourceDraft(
      { ...base, kind: "gitlab", baseUrl: "http://git.internal" },
      "a-long-enough-token"
    )
    expect(result.ok).toBe(false)
    expect(result.message).toContain("in the clear")
  })

  it("reports the provider's refusal by name", () => {
    const result = probeSeedSourceDraft(base, "short")
    expect(result.ok).toBe(false)
    expect(result.message).toBe(
      "401 from api.github.com — the credential was rejected."
    )
  })

  it("answers the same way twice for the same details", () => {
    // Deterministic on its input: a probe that failed at random would teach the
    // operator to press it a second time.
    const first = probeSeedSourceDraft(base, "a-long-enough-token")
    const second = probeSeedSourceDraft(base, "a-long-enough-token")
    expect(first).toEqual(second)
    expect(first.ok).toBe(true)
    expect(first.message).toContain("api.github.com")
  })
})
