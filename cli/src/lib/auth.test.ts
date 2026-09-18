import { describe, expect, it } from "bun:test"
import { stripAnsi, symbols } from "../theme"
import {
  describeAuthFailure,
  formatWhoamiLines,
  whoFromError,
  whoFromMe,
  type WhoAmI,
} from "./auth"
import { ComukiApiError, type MeView } from "./client"

function me(partial: Partial<MeView> = {}): MeView {
  return {
    userId: "u1",
    subjectType: "user",
    subjectId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    email: "ada@example.io",
    displayName: "Ada",
    roles: [],
    permissions: [],
    ...partial,
  }
}

describe("whoFromMe", () => {
  it("prefers display name for a user subject", () => {
    expect(whoFromMe(me())).toEqual({ kind: "user", label: "Ada" })
  })

  it("falls back to email then a short subject id", () => {
    expect(whoFromMe(me({ displayName: null }))).toEqual({
      kind: "user",
      label: "ada@example.io",
    })
    expect(whoFromMe(me({ displayName: null, email: null }))).toEqual({
      kind: "user",
      label: "user aaaaaaaa",
    })
  })

  it("labels an api-key subject with the short id", () => {
    expect(
      whoFromMe(me({ subjectType: "api-key", subjectId: "ck_z6bc48d1secret" }))
    ).toEqual({ kind: "apiKey", label: "api key ck_z6bc4" })
  })
})

describe("whoFromError", () => {
  it("maps 401 to anonymous and anything else to offline", () => {
    expect(whoFromError(new ComukiApiError(401, undefined, "nope"))).toEqual({
      kind: "anonymous",
      label: "anonymous",
    })
    expect(whoFromError(new ComukiApiError(500, undefined, "boom"))).toEqual({
      kind: "anonymous",
      label: "offline",
    })
    expect(whoFromError(new Error("fetch failed"))).toEqual({
      kind: "anonymous",
      label: "offline",
    })
  })
})

describe("formatWhoamiLines", () => {
  it("prints kind · label as the first line", () => {
    const who: WhoAmI = { kind: "user", label: "Ada" }
    expect(stripAnsi(formatWhoamiLines(who)[0] ?? "")).toBe(
      `  user ${symbols.bullet} Ada`
    )
  })

  it("appends roles and permissions when the payload carries them", () => {
    const who = whoFromMe(me())
    const lines = formatWhoamiLines(
      who,
      me({
        roles: ["owner", "operator"],
        permissions: ["chat:write", "knowledge:read"],
      })
    ).map(stripAnsi)
    expect(lines).toEqual([
      `  user ${symbols.bullet} Ada`,
      "  roles: owner, operator",
      "  permissions: chat:write, knowledge:read",
    ])
  })

  it("omits empty roles and permissions", () => {
    const lines = formatWhoamiLines(whoFromMe(me()), me()).map(stripAnsi)
    expect(lines).toEqual([`  user ${symbols.bullet} Ada`])
  })
})

describe("describeAuthFailure", () => {
  it("maps a 401 onto the inline /login notice", () => {
    expect(
      describeAuthFailure(new ComukiApiError(401, "auth.expired", "gone"), false)
    ).toBe("session expired — run /login to sign in again")
  })

  it("maps a 403 with an API key onto the roles notice, never a retry hint", () => {
    expect(
      describeAuthFailure(
        new ComukiApiError(403, "permission.denied", "chat:use required"),
        true
      )
    ).toBe(
      "API keys don't carry roles yet — run /login (cookie) or grant the key platform-admin"
    )
  })

  it("leaves a cookie 403 and non-HTTP errors to the caller", () => {
    expect(
      describeAuthFailure(
        new ComukiApiError(403, "permission.denied", "chat:use required"),
        false
      )
    ).toBeNull()
    expect(describeAuthFailure(new Error("boom"), true)).toBeNull()
  })
})
