import { describe, expect, it } from "vitest"

import { settingsToJson, sourceConnectionViewToConnection } from "@/domains/sources/api/mappers"
import type { SourceConnectionView } from "@/shared/api/_generated/types/SourceConnectionView"

/**
 * The seam between the host's flat `SourceConnectionView` and the domain's
 * richer `SourceConnection`. Mock mode never enters this code path; the
 * tests pin the behaviour for the cases the wire actually answers.
 */
describe("sourceConnectionViewToConnection", () => {
  it("maps an enabled github connection onto its domain shape", () => {
    const view: SourceConnectionView = {
      id: "sc_1",
      projectId: "p_comuki",
      provider: "github",
      name: "comuki/web-app",
      settingsJson: /*lang=json*/ '{"auth":"app-install","account":"svc-bot"}',
      secretEnvRef: "env:GITHUB_TOKEN",
      webhookPath: "/api/hooks/github",
      enabled: true,
    }

    const connection = sourceConnectionViewToConnection(view)

    expect(connection.id).toBe("sc_1")
    expect(connection.projectId).toBe("p_comuki")
    expect(connection.name).toBe("comuki/web-app")
    expect(connection.kind).toBe("github")
    expect(connection.state).toBe("connected")
    expect(connection.removable).toBe(true)
    // Settings parsed from settingsJson, not synthesized from nothing.
    expect(connection.auth).toBe("app-install")
    expect(connection.account).toBe("svc-bot")
  })

  it("maps an enabled=false connection to a disabled state", () => {
    const view: SourceConnectionView = {
      id: "sc_2",
      projectId: "p_comuki",
      provider: "github",
      name: "archived repo",
      settingsJson: "{}",
      secretEnvRef: "env:GITHUB_TOKEN",
      webhookPath: "/api/hooks/github",
      enabled: false,
    }

    const connection = sourceConnectionViewToConnection(view)

    expect(connection.state).toBe("disabled")
  })

  it("treats native as non-removable — the product's own intake", () => {
    const view: SourceConnectionView = {
      id: "sc_native",
      projectId: "p_comuki",
      provider: "native",
      name: "native intake",
      settingsJson: "{}",
      secretEnvRef: "",
      webhookPath: "",
      enabled: true,
    }

    const connection = sourceConnectionViewToConnection(view)

    expect(connection.kind).toBe("native")
    expect(connection.removable).toBe(false)
    expect(connection.auth).toBe("none")
  })

  it("reads a self-hosted baseUrl out of settings and flips selfHosted", () => {
    const view: SourceConnectionView = {
      id: "sc_gitlab",
      projectId: "p_plexor",
      provider: "gitlab",
      name: "plexor/identity-svc",
      settingsJson: /*lang=json*/ '{"auth":"pat","account":"svc","baseUrl":"https://git.plexor.internal"}',
      secretEnvRef: "env:GITLAB_TOKEN",
      webhookPath: "/api/hooks/gitlab",
      enabled: true,
    }

    const connection = sourceConnectionViewToConnection(view)

    expect(connection.baseUrl).toBe("https://git.plexor.internal")
    expect(connection.selfHosted).toBe(true)
    expect(connection.account).toBe("svc")
    expect(connection.auth).toBe("pat")
  })

  it("falls back to empty strings and 'pat' when settings are absent", () => {
    const view: SourceConnectionView = {
      id: "sc_3",
      projectId: "p_comuki",
      provider: "jira",
      name: "tracker",
      settingsJson: "{}",
      secretEnvRef: "env:JIRA_TOKEN",
      webhookPath: "",
      enabled: true,
    }

    const connection = sourceConnectionViewToConnection(view)

    expect(connection.account).toBe("")
    expect(connection.auth).toBe("pat")
    expect(connection.baseUrl).toBeUndefined()
    expect(connection.selfHosted).toBe(false)
  })

  it("ignores settings that fail to parse as JSON, rather than throwing", () => {
    const view: SourceConnectionView = {
      id: "sc_bad",
      projectId: "p_comuki",
      provider: "github",
      name: "garbled",
      settingsJson: "not-json",
      secretEnvRef: "env:GITHUB_TOKEN",
      webhookPath: "",
      enabled: true,
    }

    const connection = sourceConnectionViewToConnection(view)

    // The mapper is at the wire boundary — a malformed body from an old
    // host version must not crash the list. Empty defaults are honest:
    // the row renders with "—" / empty until the connection is fixed.
    expect(connection.account).toBe("")
    expect(connection.auth).toBe("pat")
    expect(connection.baseUrl).toBeUndefined()
  })

  it("ignores settings fields with the wrong type", () => {
    const view: SourceConnectionView = {
      id: "sc_types",
      projectId: "p_comuki",
      provider: "github",
      name: "wrong types",
      settingsJson: '{"auth":42,"account":17,"baseUrl":null}',
      secretEnvRef: "env:GITHUB_TOKEN",
      webhookPath: "",
      enabled: true,
    }

    const connection = sourceConnectionViewToConnection(view)

    expect(connection.auth).toBe("pat")
    expect(connection.account).toBe("")
    expect(connection.baseUrl).toBeUndefined()
  })

  it("leaves runtime fields the wire does not carry as the screens' honest defaults", () => {
    const view: SourceConnectionView = {
      id: "sc_runtime",
      projectId: "p_comuki",
      provider: "jira",
      name: "tracker",
      settingsJson: "{}",
      secretEnvRef: "env:JIRA_TOKEN",
      webhookPath: "",
      enabled: true,
    }

    const connection = sourceConnectionViewToConnection(view)

    // None of these are on the wire; the mapper fills them with what the
    // screens already know how to render as "nothing to report".
    expect(connection.reason).toBeUndefined()
    expect(connection.secretStoredAt).toBeUndefined()
    expect(connection.lastSyncAt).toBeUndefined()
    expect(connection.watch).toBeNull()
  })

  it("passes an unknown provider through with its host word", () => {
    const view: SourceConnectionView = {
      id: "sc_x",
      projectId: "p_comuki",
      provider: "linear",
      name: "new provider",
      settingsJson: "{}",
      secretEnvRef: "env:LINEAR_TOKEN",
      webhookPath: "",
      enabled: true,
    }

    const connection = sourceConnectionViewToConnection(view)

    // The cast is the contract — until `SOURCE_KIND_BRAND` and
    // `SOURCE_KIND_LABEL` know about "linear", the row renders with the
    // provider's own word. A failing cast would close the row; an unknown
    // provider is a future the dashboard has to grow into, not a 500.
    expect(connection.kind).toBe("linear")
  })
})

describe("settingsToJson", () => {
  it("folds auth, account and baseUrl into a flat json object", () => {
    const json = settingsToJson({
      auth: "pat",
      account: "svc-bot",
      baseUrl: "https://git.example.internal",
    })

    expect(JSON.parse(json)).toEqual({
      auth: "pat",
      account: "svc-bot",
      baseUrl: "https://git.example.internal",
    })
  })

  it("omits empty strings so the settings stay minimal", () => {
    const json = settingsToJson({ auth: "pat", account: "", baseUrl: "" })

    expect(JSON.parse(json)).toEqual({ auth: "pat" })
  })

  it("round-trips through the parser", () => {
    const json = settingsToJson({
      auth: "app-install",
      account: "comuki-swarm",
    })
    const parsed = JSON.parse(json)

    // The contract: anything the dashboard writes is what the dashboard reads.
    expect(parsed).toMatchObject({
      auth: "app-install",
      account: "comuki-swarm",
    })
  })
})
