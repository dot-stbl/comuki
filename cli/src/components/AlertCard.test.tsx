/**
 * Ink mount of the alert card: the framed 401 shape survives the
 * renderer, and a 403 card does not reuse the signed-out title.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { AlertCard } from "./AlertCard"
import { stripAnsi } from "../theme"

describe("AlertCard", () => {
  test("renders the 401 frame with the kind word and hints", () => {
    const { lastFrame, unmount } = render(
      <AlertCard
        kind="error"
        code="authentication.required"
        title="signed out"
        detail="permission 'chat:use' requires a signed-in subject"
        hints={["/login  to sign in again", "/retry  last message"]}
        width={60}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain("┌─ error · authentication.required")
    expect(frame).toContain("permission 'chat:use'")
    expect(frame).toContain("/login")
    expect(frame).toContain("/retry")
    expect(frame).toContain("└")
    unmount()
  })

  test("403 title is permission denied, not signed out", () => {
    const { lastFrame, unmount } = render(
      <AlertCard
        kind="error"
        title="permission denied"
        detail="missing chat:use"
        hints={[]}
        width={60}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain("error · permission denied")
    expect(frame).not.toContain("signed out")
    unmount()
  })
})
