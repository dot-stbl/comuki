import { describe, expect, it } from "vitest"

import { loginFailureMessage } from "./login-failure"

/** The shape the kubb transport throws for a non-2xx answer. */
function transportFailure(status: number, code?: string): Error {
  return Object.assign(new Error(`request failed ${status}`), {
    status,
    data: { code, detail: "server-side sentence" },
  })
}

describe("login failure copy", () => {
  it("says incorrect credentials for the host's 401 code", () => {
    expect(
      loginFailureMessage(transportFailure(401, "auth.invalid_credentials")),
    ).toBe("Incorrect email or password")
  })

  it("treats a bare 401 the same — the login endpoint has no other 401", () => {
    expect(loginFailureMessage(transportFailure(401))).toBe(
      "Incorrect email or password",
    )
  })

  it("maps a validation 400 to the email sentence", () => {
    expect(loginFailureMessage(transportFailure(400))).toBe(
      "Enter a valid email address",
    )
  })

  it("maps the login rate limiter's 429", () => {
    expect(loginFailureMessage(transportFailure(429))).toBe(
      "Too many attempts — try again in a minute",
    )
  })

  it("maps fetch's network failure (TypeError) to a reachability sentence", () => {
    expect(loginFailureMessage(new TypeError("Failed to fetch"))).toBe(
      "Cannot reach the server — check your connection",
    )
  })

  it("passes a mock rejection's own words through", () => {
    expect(
      loginFailureMessage(
        new Error("Those credentials were refused. Check the address and try again."),
      ),
    ).toBe("Those credentials were refused. Check the address and try again.")
  })

  it("keeps a fallback for whatever else can be thrown", () => {
    expect(loginFailureMessage("nope")).toBe(
      "Sign-in failed. Check the address and try again.",
    )
    expect(loginFailureMessage(null)).toBe(
      "Sign-in failed. Check the address and try again.",
    )
  })

  it("does not show a raw transport message for a 401 with an unexpected code", () => {
    expect(loginFailureMessage(transportFailure(401, "auth.other"))).toBe(
      "Sign-in failed. Check the address and try again.",
    )
  })
})
