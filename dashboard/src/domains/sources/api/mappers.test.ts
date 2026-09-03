import { describe, expect, it } from "vitest"

import { sourceConnectionViewToConnection } from "@/domains/sources/api/mappers"
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
      settingsJson: "{}",
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

  it("leaves runtime fields the wire does not carry as the screens' honest defaults", () => {
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

    // None of these are on the wire; the mapper fills them with what the
    // screens already know how to render as "nothing to report".
    expect(connection.reason).toBeUndefined()
    expect(connection.baseUrl).toBeUndefined()
    expect(connection.account).toBe("")
    expect(connection.secretStoredAt).toBeUndefined()
    expect(connection.lastSyncAt).toBeUndefined()
    expect(connection.watch).toBeNull()
    expect(connection.selfHosted).toBe(false)
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
