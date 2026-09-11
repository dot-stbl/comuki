import { describe, expect, it } from "vitest"

import {
  ADMISSION_MODES,
  CONNECTABLE_PROVIDERS,
  NATIVE_PROVIDER,
  PROVIDERS,
  admissionLabel,
  admittedCount,
  connectionHost,
  connectionNote,
  filterFields,
  intakeNote,
  isKnownProvider,
  isNativeIntake,
  needsBaseUrl,
  parseTicketLabels,
  providerAuth,
  providerBrand,
  providerLabel,
  providerOf,
  targetLabel,
  targetPlaceholder,
  type Provider,
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
    expect(CONNECTABLE_PROVIDERS.map((entry) => entry.key)).not.toContain(
      NATIVE_PROVIDER
    )
    expect(CONNECTABLE_PROVIDERS).toHaveLength(4)
    // Derived from the fact rather than from the name: a provider is off the
    // connect form because there is nothing to point a credential at.
    for (const provider of CONNECTABLE_PROVIDERS) {
      expect(provider.remote).toBe(true)
    }
  })

  it("offers each provider only the credentials its connector implements", () => {
    expect(providerAuth("github")).toEqual(["pat", "app-install"])
    expect(providerAuth("jira")).toEqual(["pat"])
    expect(providerAuth("yandex-tracker")).toEqual(["oauth"])
    expect(providerAuth(NATIVE_PROVIDER)).toEqual(["none"])
  })

  it("asks for a base url only where an instance can be self-hosted", () => {
    expect(needsBaseUrl("gitlab")).toBe(true)
    expect(needsBaseUrl("jira")).toBe(true)
    expect(needsBaseUrl("github")).toBe(false)
    expect(needsBaseUrl(NATIVE_PROVIDER)).toBe(false)
  })
})

/**
 * The registry is the schema, and these are the two properties that make it
 * worth being one.
 *
 * The first is that a provider is *one* entry: adding a sixth is a single
 * object literal, and there is no second table anywhere in `src/domains` that
 * could be forgotten. The test enforces it the only way a test can — by
 * writing the entry and then asking every reader in the domain about it.
 *
 * The second is that a provider with no entry at all still renders. That was
 * the failure this registry exists to make unreachable: six exhaustive
 * `Record<SourceKind, …>` tables all answered `undefined` for `linear`, and
 * the provider column rendered an empty cell.
 */
describe("adding a provider is one registry entry", () => {
  /** A sixth provider, written once, the way somebody would write it. */
  const LINEAR: Provider = {
    key: "linear",
    label: "linear",
    brand: null,
    auth: ["oauth"],
    remote: true,
    selfHostable: false,
    target: "team key",
    targetPlaceholder: "ENG",
    filterFields: ["team", "labels", "state"],
    intakeNote:
      "issues land from a watched team. one written here is stamped as the team's.",
  }

  it("is the only thing the type asks for", () => {
    // Half of this assertion is the compiler's: `LINEAR` is a `Provider`, and
    // every field of `Provider` is required, so a half-added provider fails
    // to build rather than rendering an empty cell three screens away. The
    // other half is that a shipped entry has no field this one lacks —
    // nowhere for a sixth provider to be quietly incomplete.
    expect(Object.keys(LINEAR).sort()).toEqual(Object.keys(PROVIDERS[0]).sort())
  })

  it("is enough, because every reader reads the entry and nothing else", () => {
    // The property that makes the claim true: there is no second table in
    // this domain for a provider to be missing from. Each reader answers with
    // the entry's own field, for every entry, so writing the entry is the
    // whole of the work — and a reader that grew a lookup of its own would
    // fail here.
    for (const provider of PROVIDERS) {
      expect(providerOf(provider.key)).toBe(provider)
      expect(providerLabel(provider.key)).toBe(provider.label)
      expect(providerBrand(provider.key)).toBe(provider.brand)
      expect(providerAuth(provider.key)).toBe(provider.auth)
      expect(isNativeIntake(provider.key)).toBe(!provider.remote)
      expect(needsBaseUrl(provider.key)).toBe(provider.selfHostable)
      expect(targetLabel(provider.key)).toBe(provider.target)
      expect(targetPlaceholder(provider.key)).toBe(provider.targetPlaceholder)
      expect(filterFields(provider.key)).toBe(provider.filterFields)
      expect(intakeNote(provider.key)).toBe(provider.intakeNote)
      expect(isKnownProvider(provider.key)).toBe(true)
    }
  })
})

describe("a provider with no entry at all", () => {
  it("is not in the registry, and says so without throwing", () => {
    expect(providerOf("linear")).toBeNull()
    expect(isKnownProvider("linear")).toBe(false)
  })

  it("renders the host's own word everywhere instead of an empty cell", () => {
    // Every reader, one after the other. Not one of them can answer
    // `undefined` — that is the whole difference between this and the nine
    // `Record` tables it replaced.
    expect(providerLabel("linear")).toBe("linear")
    expect(providerBrand("linear")).toBeNull()
    expect(targetLabel("linear")).toBe("name")
    expect(targetPlaceholder("linear")).toBe("")
    expect(filterFields("linear")).toEqual([])
    expect(intakeNote("linear").length).toBeGreaterThan(24)
  })

  it("treats it as somebody else's tracker, because that is what it is", () => {
    // Degrading the other way would make an unknown connection unremovable
    // and hide its credential fields — the product refusing to manage a row
    // because it did not recognise a word.
    expect(isNativeIntake("linear")).toBe(false)
    expect(needsBaseUrl("linear")).toBe(false)
  })

  it("admits it does not know the credential rather than guessing one", () => {
    // Every credential the dashboard can render a form for, and none it
    // cannot. `none` is native's and would be a lie about a remote provider.
    expect(providerAuth("linear")).toEqual(["pat", "oauth", "app-install"])
    expect(providerAuth("linear")).not.toContain("none")
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
