import { describe, expect, it } from "bun:test"
import { describeAuthFailure } from "./auth"
import { ComukiApiError } from "./client"

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
