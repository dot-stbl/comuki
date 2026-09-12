import { afterEach, describe, expect, it } from "vitest"

import {
  clearMockAuth,
  expireMockSession,
  resetMockAuth,
  signOutMock,
} from "@/shared/api/mock/auth.store"

import { guardSession, type GuardedLocation } from "./guard"

interface ThrownRedirect {
  options: {
    to?: string
    search?: { reason?: string; redirect?: string }
    replace?: boolean
  }
}

/** `redirect()` builds a value to throw, so the guard's answer is its throw. */
async function bounce(location: GuardedLocation): Promise<ThrownRedirect | null> {
  try {
    await guardSession(location)
    return null
  } catch (thrown) {
    return thrown as ThrownRedirect
  }
}

const runs: GuardedLocation = { pathname: "/runs", href: "/runs?status=waiting" }

afterEach(() => {
  resetMockAuth()
})

describe("the session guard", () => {
  it("lets a signed-in shift through", async () => {
    resetMockAuth()

    expect(await bounce(runs)).toBeNull()
  })

  it("never guards the screen that hands out sessions", async () => {
    clearMockAuth()

    expect(await bounce({ pathname: "/login", href: "/login" })).toBeNull()
    // Trailing slash is the same screen, and a guard that bounced it would
    // bounce it to itself for ever.
    expect(await bounce({ pathname: "/login/", href: "/login/" })).toBeNull()
  })

  it("sends an unidentified visitor to the sign-in screen", async () => {
    clearMockAuth()

    expect((await bounce(runs))?.options.to).toBe("/login")
  })

  // The whole point of the redirect param: they asked for a screen, and after
  // signing in they get that screen rather than the front door.
  it("carries the path they wanted, search string and all", async () => {
    clearMockAuth()

    expect((await bounce(runs))?.options.search?.redirect).toBe("/runs?status=waiting")
  })

  it("leaves the board out of the address bar, since it is the default anyway", async () => {
    clearMockAuth()

    expect((await bounce({ pathname: "/", href: "/" }))?.options.search).toEqual({})
  })

  it("replaces rather than pushes, so back does not bounce again", async () => {
    clearMockAuth()

    expect((await bounce(runs))?.options.replace).toBe(true)
  })

  it("says the session expired when it did", async () => {
    expireMockSession()

    expect((await bounce(runs))?.options.search?.reason).toBe("expired")
  })

  it("says they signed out when they chose to", async () => {
    signOutMock()

    expect((await bounce(runs))?.options.search?.reason).toBe("signed-out")
  })

  it("explains nothing to someone who never had a session", async () => {
    clearMockAuth()

    expect((await bounce(runs))?.options.search?.reason).toBeUndefined()
  })
})
