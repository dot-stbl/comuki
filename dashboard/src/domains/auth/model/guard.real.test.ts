import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/* Real mode: the guard's session source is the me query through the shared
   query client, not the seed store. The mocked env pins `useMock: false`
   for this whole file — the mock-mode behaviour lives in `guard.test.ts`. */
vi.mock("@/shared/config/env", () => ({
  env: { useMock: false },
}))

import { queryClient } from "@/app/query-client"
import { meQueryKey } from "@/domains/identity/api/queries"

import { guardSession, type GuardedLocation } from "./guard"

interface ThrownRedirect {
  options: {
    to?: string
    search?: { reason?: string; redirect?: string }
    replace?: boolean
  }
}

async function bounce(location: GuardedLocation): Promise<ThrownRedirect | null> {
  try {
    await guardSession(location)
    return null
  } catch (thrown) {
    return thrown as ThrownRedirect
  }
}

const runs: GuardedLocation = { pathname: "/runs", href: "/runs?status=waiting" }

let ensureQueryData: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  ensureQueryData = vi.spyOn(queryClient, "ensureQueryData")
})

afterEach(() => {
  ensureQueryData.mockRestore()
  queryClient.clear()
})

describe("the session guard in real mode", () => {
  it("resolves the session through the me query and lets it through", async () => {
    ensureQueryData.mockResolvedValue({ id: "u_1" })

    expect(await bounce(runs)).toBeNull()
    expect(ensureQueryData).toHaveBeenCalledTimes(1)
    // One source of truth: the guard asks the same key the provider tree
    // reads, not a second fetch of its own.
    expect(ensureQueryData.mock.calls[0]?.[0]).toMatchObject({ queryKey: meQueryKey })
  })

  it("does not ask the host on the screen that hands out sessions", async () => {
    ensureQueryData.mockResolvedValue({ id: "u_1" })

    expect(
      await bounce({ pathname: "/login", href: "/login?reason=oidc-failed" }),
    ).toBeNull()
    expect(ensureQueryData).not.toHaveBeenCalled()
  })

  it("sends a refused session to the sign-in screen as expired", async () => {
    ensureQueryData.mockRejectedValue(
      Object.assign(new Error("auth boundary 401"), { status: 401 }),
    )

    const thrown = await bounce(runs)

    expect(thrown?.options.to).toBe("/login")
    expect(thrown?.options.search).toEqual({
      reason: "expired",
      redirect: "/runs?status=waiting",
    })
    expect(thrown?.options.replace).toBe(true)
  })

  it("leaves the board out of the redirect, since it is the default anyway", async () => {
    ensureQueryData.mockRejectedValue(
      Object.assign(new Error("auth boundary 401"), { status: 401 }),
    )

    expect(
      (await bounce({ pathname: "/", href: "/" }))?.options.search,
    ).toEqual({ reason: "expired" })
  })
})
