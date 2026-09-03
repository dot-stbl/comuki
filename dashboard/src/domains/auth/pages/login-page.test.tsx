import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import type { ReactElement, ReactNode } from "react"

import {
  isMockSignedIn,
  resetMockAuth,
  setMockOidcProvider,
  signOutMock,
} from "@/shared/api/mock/auth.store"

/* `vi.hoisted` runs before `vi.mock` is hoisted, so the env stubs land
   *before* any module reads `import.meta.env`. `env.ts` parses at module
   load; without this, the first import of `LoginPage` captures empty
   stubs and `env.useMock` is fixed at `false`. */
const setup = vi.hoisted(() => {
  vi.stubEnv("VITE_USE_MOCK", "true")
  vi.stubEnv("VITE_API_BASE_URL", "")
  vi.stubEnv("VITE_OIDC_PROVIDER", "")
  return {
    useLoginMutation: vi.fn(),
    startOidcFlow: vi.fn(),
  }
})

vi.mock("@/domains/identity/api/mutations", () => ({
  useLoginMutation: setup.useLoginMutation,
}))
vi.mock("@/domains/auth/api/oidc-start", () => ({
  startOidcFlow: setup.startOidcFlow,
}))

/* Lazy SUT: imported *after* the hoisted stubs run, so `env.ts` parses
   `import.meta.env.VITE_USE_MOCK` with `"true"`. The previous test file
   imported `./login-page` at the top and ran every test against a
   module that captured `env.useMock = false`, which is why the OIDC
   button was hidden. */
const { LoginPage } = await import("./login-page")

/** Mock builder for `useLoginMutation`. */
type LoginMock = {
  mutate: ReturnType<typeof vi.fn>
  mutateAsync: ReturnType<typeof vi.fn>
  reset: ReturnType<typeof vi.fn>
  isPending: boolean
  isError: boolean
  error: unknown
  status: string
}
function buildLoginMock(overrides: Partial<LoginMock> = {}): LoginMock {
  const mutateAsync = vi.fn().mockResolvedValue({
    id: "u_test",
    name: "Test User",
    email: "test@comuki.local",
    platformRoles: ["member"],
    projectRoles: {},
  })
  return {
    mutate: vi.fn(),
    mutateAsync,
    reset: vi.fn(),
    isPending: false,
    isError: false,
    error: null,
    status: "idle",
    ...overrides,
  }
}

function withQuery({ children }: { children: ReactNode }): ReactElement {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

beforeEach(() => {
  resetMockAuth()
  signOutMock()
  setup.useLoginMutation.mockReset()
  setup.startOidcFlow.mockReset()
})

afterEach(() => {
  resetMockAuth()
})

async function fillIn(identity: string, password: string) {
  const user = userEvent.setup()
  await user.type(screen.getByLabelText("Email or username"), identity)
  await user.type(screen.getByLabelText("Password"), password)
  return user
}

describe("the sign-in screen", () => {
  it("is identified by the mark and nothing else — no rail, no topbar", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage />, { wrapper: withQuery })

    expect(document.querySelector("[data-test='login-screen'] svg")).not.toBeNull()
    // Navigation offered to someone the product has not identified yet —
    // the chrome above the form is a footer with a repo link, not a nav.
    expect(screen.queryByRole("navigation")).toBeNull()
    const footer = document.querySelector("[data-test='login-footer']")
    expect(footer).not.toBeNull()
  })

  it("names the build at the floor of the screen", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage />, { wrapper: withQuery })

    const footer = document.querySelector("[data-test='login-footer']")
    expect(footer).not.toBeNull()
    // Build line carries the deploy env — the thing an operator checks
    // before typing their real password in.
    expect(footer?.textContent ?? "").toMatch(/build/)
  })
})

describe("the three landings", () => {
  it("cold: asks, and announces nothing", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage />, { wrapper: withQuery })

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Sign in")
    expect(document.querySelector("[data-test='login-landing']")).toBeNull()
    expect(screen.getByText(/Sign in to reach the dispatcher board/)).not.toBeNull()
  })

  it("expired: says so, and says where they will be put back", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage reason="expired" redirect="/runs?status=waiting" />, {
      wrapper: withQuery,
    })

    const landing = document.querySelector("[data-test='login-landing']")
    expect(landing?.getAttribute("data-landing")).toBe("expired")
    expect(screen.getByText("Your session expired")).not.toBeNull()
    expect(screen.getByText("/runs?status=waiting")).not.toBeNull()
  })

  it("signed out: confirms quietly, with no talk of expiry", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage reason="signed-out" />, { wrapper: withQuery })

    const landing = document.querySelector("[data-test='login-landing']")
    expect(landing?.getAttribute("data-landing")).toBe("signed-out")
    expect(screen.getByText("You're signed out")).not.toBeNull()
    expect(screen.queryByText(/expired/i)).toBeNull()
  })

  it("is one screen: the form is identical in all three", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    const { rerender } = render(<LoginPage />, { wrapper: withQuery })
    const fields = () => [
      screen.getByLabelText("Email or username"),
      screen.getByLabelText("Password"),
      screen.getByRole("button", { name: "Sign in" }),
    ]

    expect(fields()).toHaveLength(3)
    rerender(<LoginPage reason="expired" />)
    expect(fields()).toHaveLength(3)
    rerender(<LoginPage reason="signed-out" />)
    expect(fields()).toHaveLength(3)
  })
})

describe("the local form", () => {
  it("uses a real password field, so a manager can fill it", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage />, { wrapper: withQuery })

    const password = screen.getByLabelText("Password")
    expect(password.getAttribute("type")).toBe("password")
    expect(password.getAttribute("autocomplete")).toBe("current-password")
    expect(screen.getByLabelText("Email or username").getAttribute("autocomplete")).toBe(
      "username"
    )
  })

  it("will not submit an empty form", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage />, { wrapper: withQuery })

    expect(
      screen.getByRole("button", { name: "Sign in" }).hasAttribute("disabled")
    ).toBe(true)
  })

  it("takes any credentials, runs the login mutation, and lands on the board", async () => {
    const mutateAsync = vi.fn().mockResolvedValue({
      id: "u_test",
      name: "Test User",
      email: "duty@comuki.local",
      platformRoles: ["member"],
      projectRoles: {},
    })
    setup.useLoginMutation.mockReturnValue(
      buildLoginMock({ mutateAsync, isPending: false, status: "success" }),
    )

    const onSignedIn = vi.fn()
    render(<LoginPage onSignedIn={onSignedIn} />, { wrapper: withQuery })

    const user = await fillIn("duty@comuki.local", "anything")
    await user.click(screen.getByRole("button", { name: "Sign in" }))

    await vi.waitFor(() => expect(mutateAsync).toHaveBeenCalled())
    expect(mutateAsync).toHaveBeenCalledWith({
      email: "duty@comuki.local",
      password: "anything",
    })
    await vi.waitFor(() => expect(onSignedIn).toHaveBeenCalledWith("/"))
  })

  // The return path is the reason the expired landing carries one at all.
  it("returns to where they were headed", async () => {
    const mutateAsync = vi.fn().mockResolvedValue({
      id: "u_test",
      name: "Test User",
      email: "duty@comuki.local",
      platformRoles: ["member"],
      projectRoles: {},
    })
    setup.useLoginMutation.mockReturnValue(buildLoginMock({ mutateAsync }))

    const onSignedIn = vi.fn()
    render(<LoginPage redirect="/runs?status=waiting" onSignedIn={onSignedIn} />, {
      wrapper: withQuery,
    })

    const user = await fillIn("duty", "anything")
    await user.click(screen.getByRole("button", { name: "Sign in" }))

    await vi.waitFor(() =>
      expect(onSignedIn).toHaveBeenCalledWith("/runs?status=waiting")
    )
  })

  it("submits on Enter, without a key handler pretending to be a form", async () => {
    const mutateAsync = vi.fn().mockResolvedValue({
      id: "u_test",
      name: "Test User",
      email: "duty@comuki.local",
      platformRoles: ["member"],
      projectRoles: {},
    })
    setup.useLoginMutation.mockReturnValue(buildLoginMock({ mutateAsync }))

    const onSignedIn = vi.fn()
    render(<LoginPage onSignedIn={onSignedIn} />, { wrapper: withQuery })

    const user = await fillIn("duty", "anything")
    await user.type(screen.getByLabelText("Password"), "{Enter}")

    await vi.waitFor(() => expect(mutateAsync).toHaveBeenCalled())
    await vi.waitFor(() => expect(onSignedIn).toHaveBeenCalled())
  })

  it("surfaces a mutation failure as the screen-level error block", async () => {
    const mutateAsync = vi
      .fn()
      .mockRejectedValue(new Error("Those credentials were refused."))
    setup.useLoginMutation.mockReturnValue(
      buildLoginMock({ mutateAsync, isError: true, error: new Error("rejected") }),
    )

    render(<LoginPage />, { wrapper: withQuery })

    const user = await fillIn("duty", "anything")
    await user.click(screen.getByRole("button", { name: "Sign in" }))

    const failure = document.querySelector("[data-test='login-failure']")
    expect(failure).not.toBeNull()
    expect(failure?.textContent).toContain("Those credentials were refused.")
  })
})

describe("the identity provider", () => {
  it("offers the provider when one is configured", () => {
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage />, { wrapper: withQuery })

    expect(screen.getByRole("button", { name: /Continue with OIDC/ })).not.toBeNull()
  })

  // §16 says "if configured". A button that leads nowhere teaches an operator
  // to distrust the screen, so the tenant without a provider does not get one.
  it("offers nothing when none is", () => {
    setMockOidcProvider(null)
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    render(<LoginPage />, { wrapper: withQuery })

    expect(screen.queryByRole("button", { name: /Continue with/ })).toBeNull()
    expect(screen.getByRole("button", { name: "Sign in" })).not.toBeNull()
  })

  it("signs in through the provider and lands on the return path", async () => {
    // Mock-mode OIDC still flows through the seed store, not the browser
    // redirect helper — the helper is for real mode only.
    setup.useLoginMutation.mockReturnValue(buildLoginMock())
    setup.startOidcFlow.mockImplementation(() => undefined)

    const onSignedIn = vi.fn()
    render(<LoginPage redirect="/queue" onSignedIn={onSignedIn} />, {
      wrapper: withQuery,
    })

    const user = userEvent.setup()
    await user.click(screen.getByRole("button", { name: /Continue with OIDC/ }))

    await vi.waitFor(() => expect(onSignedIn).toHaveBeenCalledWith("/queue"))
    expect(isMockSignedIn()).toBe(true)
    // Real-mode path is not exercised in mock mode.
    expect(setup.startOidcFlow).not.toHaveBeenCalled()
  })
})