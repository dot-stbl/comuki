import { describe, expect, it } from "vitest"

import {
  ADMISSION_MODES,
  AUTH_BY_KIND,
  CONNECTABLE_KINDS,
  admissionLabel,
  admittedCount,
  connectionHost,
  connectionNote,
  needsBaseUrl,
  parseTicketLabels,
} from "@/domains/sources/model/providers"
import type {
  NativeTicket,
  SourceConnection,
} from "@/domains/sources/model/types"

function connection(
  overrides: Partial<SourceConnection> = {}
): SourceConnection {
  return {
    id: "src_x",
    projectId: "p_test",
    kind: "github",
    name: "here/web-app",
    state: "connected",
    auth: "pat",
    selfHosted: false,
    account: "svc-bot",
    removable: true,
    lastSyncAt: "4 min ago",
    watch: {
      enabled: true,
      filter: "labels: swarm",
      mode: "inbox-only",
      matched: 12,
      mapping: [],
    },
    ...overrides,
  }
}

describe("what a row says is happening", () => {
  it("leads with the provider's own reason when it is broken", () => {
    const note = connectionNote(
      connection({ state: "error", reason: "401 — the token was revoked." }),
      []
    )
    expect(note).toBe("401 — the token was revoked.")
  })

  it("says a healthy watch is admitting nothing", () => {
    const note = connectionNote(
      connection({ watch: { ...connection().watch!, matched: 0 } }),
      []
    )
    expect(note).toBe("the filter matched nothing in the last day")
  })

  it("distinguishes a watch that is off from a connection that is off", () => {
    expect(
      connectionNote(
        connection({ watch: { ...connection().watch!, enabled: false } }),
        []
      )
    ).toBe("watch off — nothing is being admitted from here")

    expect(connectionNote(connection({ state: "disabled" }), [])).toBe(
      "turned off — last synced 4 min ago"
    )
  })

  it("counts what has been filed when the row is native", () => {
    const native = connection({ kind: "native", watch: null })
    const tickets: NativeTicket[] = [
      {
        id: "nt_1",
        projectId: "p_test",
        title: "a",
        body: "",
        labels: [],
        createdAt: "today",
        straightToWork: false,
      },
      {
        id: "nt_2",
        projectId: "p_other",
        title: "b",
        body: "",
        labels: [],
        createdAt: "today",
        straightToWork: false,
      },
    ]

    expect(admittedCount(native, tickets)).toBe(1)
    expect(connectionNote(native, tickets)).toBe("1 ticket filed here")
  })

  it("never returns a blank, because a blank reads as a broken render", () => {
    const notes = [
      connectionNote(connection(), []),
      connectionNote(connection({ state: "error" }), []),
      connectionNote(connection({ kind: "native", watch: null }), []),
      connectionNote(connection({ lastSyncAt: undefined }), []),
    ]
    for (const note of notes) {
      expect(note.length).toBeGreaterThan(0)
    }
  })
})

describe("the instance a connection talks to", () => {
  it("names the host of a self-hosted instance", () => {
    expect(
      connectionHost(
        connection({ selfHosted: true, baseUrl: "https://git.plexor.internal" })
      )
    ).toBe("git.plexor.internal")
  })

  it("says cloud rather than leaving a blank", () => {
    expect(connectionHost(connection())).toBe("cloud")
  })

  it("says native has no remote end at all", () => {
    expect(connectionHost(connection({ kind: "native", watch: null }))).toBe(
      "in-platform"
    )
  })
})

describe("what a connection is admitting", () => {
  it("says native intake rather than a watch state it does not have", () => {
    expect(admissionLabel(connection({ kind: "native", watch: null }))).toBe(
      "native intake"
    )
  })

  it("says the mode when the watch is on and off when it is not", () => {
    expect(admissionLabel(connection())).toBe("inbox-only")
    expect(
      admissionLabel(
        connection({ watch: { ...connection().watch!, enabled: false } })
      )
    ).toBe("watch off")
  })

  it("counts nothing while the watch is off", () => {
    expect(
      admittedCount(
        connection({ watch: { ...connection().watch!, enabled: false } }),
        []
      )
    ).toBe(0)
  })
})

describe("what the connect form may ask for", () => {
  it("never offers native, which every project already has", () => {
    expect(CONNECTABLE_KINDS).not.toContain("native")
    expect(CONNECTABLE_KINDS).toHaveLength(4)
  })

  it("offers each provider only the credentials its connector implements", () => {
    expect(AUTH_BY_KIND.github).toEqual(["pat", "app-install"])
    expect(AUTH_BY_KIND.jira).toEqual(["pat"])
    expect(AUTH_BY_KIND["yandex-tracker"]).toEqual(["oauth"])
    expect(AUTH_BY_KIND.native).toEqual(["none"])
  })

  it("asks for a base url only where an instance can be self-hosted", () => {
    expect(needsBaseUrl("gitlab")).toBe(true)
    expect(needsBaseUrl("jira")).toBe(true)
    expect(needsBaseUrl("github")).toBe(false)
    expect(needsBaseUrl("native")).toBe(false)
  })
})

describe("the three admission modes", () => {
  it("are three, named once, each with its own sentence", () => {
    expect(ADMISSION_MODES.map((mode) => mode.value)).toEqual([
      "watch",
      "inbox-only",
      "both",
    ])
    expect(new Set(ADMISSION_MODES.map((mode) => mode.description)).size).toBe(
      3
    )
    for (const mode of ADMISSION_MODES) {
      expect(mode.description.length).toBeGreaterThan(24)
    }
  })
})

describe("a ticket's labels are labels, not an expression", () => {
  it("splits on a comma and drops the empties", () => {
    expect(parseTicketLabels(" checkout-web , bug ,, ")).toEqual([
      "checkout-web",
      "bug",
    ])
  })

  it("is empty when nothing was typed", () => {
    expect(parseTicketLabels("   ")).toEqual([])
  })
})
