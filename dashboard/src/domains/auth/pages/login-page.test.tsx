import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import {
  isMockSignedIn,
  resetMockAuth,
  setMockOidcProvider,
  signOutMock,
} from "@/shared/api/mock/auth.store"

import { LoginPage } from "./login-page"

beforeEach(() => {
  resetMockAuth()
  // Every test starts from "there is nobody here", which is the only state in
  // which signing in can be shown to have changed anything.
  signOutMock()
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
    render(<LoginPage />)

    expect(document.querySelector("[data-test='login-screen'] svg")).not.toBeNull()
    // Navigation offered to someone the product has not identified yet —
    // the chrome above the form is a footer with a repo link, not a nav.
    expect(screen.queryByRole("navigation")).toBeNull()
    const footer = document.querySelector("[data-test='login-footer']")
    expect(footer).not.toBeNull()
  })

  it("names the build at the floor of the screen", () => {
    render(<LoginPage />)

    const footer = document.querySelector("[data-test='login-footer']")
    expect(footer).not.toBeNull()
    // Build line carries the deploy env — the thing an operator checks
    // before typing their real password in.
    expect(footer?.textContent ?? "").toMatch(/build/)
  })
})

describe("the three landings", () => {
  it("cold: asks, and announces nothing", () => {
    render(<LoginPage />)

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Sign in")
    expect(document.querySelector("[data-test='login-landing']")).toBeNull()
    expect(screen.getByText(/Sign in to reach the dispatcher board/)).not.toBeNull()
  })

  it("expired: says so, and says where they will be put back", () => {
    render(<LoginPage reason="expired" redirect="/runs?status=waiting" />)

    const landing = document.querySelector("[data-test='login-landing']")
    expect(landing?.getAttribute("data-landing")).toBe("expired")
    expect(screen.getByText("Your session expired")).not.toBeNull()
    expect(screen.getByText("/runs?status=waiting")).not.toBeNull()
  })

  it("signed out: confirms quietly, with no talk of expiry", () => {
    render(<LoginPage reason="signed-out" />)

    const landing = document.querySelector("[data-test='login-landing']")
    expect(landing?.getAttribute("data-landing")).toBe("signed-out")
    expect(screen.getByText("You're signed out")).not.toBeNull()
    expect(screen.queryByText(/expired/i)).toBeNull()
  })

  it("is one screen: the form is identical in all three", () => {
    const { rerender } = render(<LoginPage />)
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
    render(<LoginPage />)

    const password = screen.getByLabelText("Password")
    expect(password.getAttribute("type")).toBe("password")
    expect(password.getAttribute("autocomplete")).toBe("current-password")
    expect(screen.getByLabelText("Email or username").getAttribute("autocomplete")).toBe(
      "username"
    )
  })

  it("will not submit an empty form", () => {
    render(<LoginPage />)

    expect(
      screen.getByRole("button", { name: "Sign in" }).hasAttribute("disabled")
    ).toBe(true)
  })

  it("takes any credentials, sets the session and lands on the board", async () => {
    const onSignedIn = vi.fn()
    render(<LoginPage onSignedIn={onSignedIn} />)

    const user = await fillIn("duty@comuki.local", "anything")
    await user.click(screen.getByRole("button", { name: "Sign in" }))

    await vi.waitFor(() => expect(onSignedIn).toHaveBeenCalledWith("/"))
    expect(isMockSignedIn()).toBe(true)
  })

  // The return path is the reason the expired landing carries one at all.
  it("returns to where they were headed", async () => {
    const onSignedIn = vi.fn()
    render(<LoginPage redirect="/runs?status=waiting" onSignedIn={onSignedIn} />)

    const user = await fillIn("duty", "anything")
    await user.click(screen.getByRole("button", { name: "Sign in" }))

    await vi.waitFor(() =>
      expect(onSignedIn).toHaveBeenCalledWith("/runs?status=waiting")
    )
  })

  it("submits on Enter, without a key handler pretending to be a form", async () => {
    const onSignedIn = vi.fn()
    render(<LoginPage onSignedIn={onSignedIn} />)

    const user = await fillIn("duty", "anything")
    await user.type(screen.getByLabelText("Password"), "{Enter}")

    await vi.waitFor(() => expect(onSignedIn).toHaveBeenCalled())
  })
})

describe("the identity provider", () => {
  it("offers the provider when one is configured", () => {
    render(<LoginPage />)

    expect(screen.getByRole("button", { name: /Continue with OIDC/ })).not.toBeNull()
  })

  // §16 says "if configured". A button that leads nowhere teaches an operator
  // to distrust the screen, so the tenant without a provider does not get one.
  it("offers nothing when none is", () => {
    setMockOidcProvider(null)
    render(<LoginPage />)

    expect(screen.queryByRole("button", { name: /Continue with/ })).toBeNull()
    expect(screen.getByRole("button", { name: "Sign in" })).not.toBeNull()
  })

  it("signs in through the provider and lands on the return path", async () => {
    const onSignedIn = vi.fn()
    render(<LoginPage redirect="/queue" onSignedIn={onSignedIn} />)

    const user = userEvent.setup()
    await user.click(screen.getByRole("button", { name: /Continue with OIDC/ }))

    await vi.waitFor(() => expect(onSignedIn).toHaveBeenCalledWith("/queue"))
    expect(isMockSignedIn()).toBe(true)
  })
})
