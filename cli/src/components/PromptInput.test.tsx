/**
 * History-recall tests for the `›` prompt editor: ↑/↓ cycling, draft
 * preservation, position kept while editing a recalled entry, and the
 * reset-to-newest on submit. Keystrokes go through ink-testing-library's
 * mock stdin, so the whole editor runs as in the real TUI.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { PromptInput } from "./PromptInput"

/** Lets React flush the state update a keystroke schedules. */
function settle(ms = 100): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

const UP = "\x1b[A"
const DOWN = "\x1b[B"
const ENTER = "\r"

interface PromptHandle {
  readonly stdin: { write: (data: string) => void }
  readonly lastFrame: () => string | undefined
  readonly unmount: () => void
  readonly submitted: string[]
}

async function renderPrompt(
  history: readonly string[],
  historyRecallEnabled: boolean = true
): Promise<PromptHandle> {
  const submitted: string[] = []
  const { stdin, lastFrame, unmount } = render(
    <PromptInput
      history={history}
      historyRecallEnabled={historyRecallEnabled}
      onSubmit={(value) => submitted.push(value)}
    />
  )
  // The first keystroke is lost without this: useInput subscribes in an
  // effect that must flush before the mock stdin can deliver anything.
  await settle()
  return { stdin, lastFrame, unmount, submitted }
}

describe("PromptInput history recall", () => {
  test("↑ recalls the newest entry, then walks older", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["alpha", "beta"])
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("beta")
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("alpha")
    unmount()
  })

  test("↓ past the newest entry returns to the empty draft line", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["alpha", "beta"])
    stdin.write(UP)
    await settle()
    // ↑ from the draft lands on the newest entry directly.
    expect(lastFrame()).toContain("beta")
    stdin.write(DOWN)
    await settle()
    // One ↓ past the newest entry drops back to the live line…
    expect(lastFrame()).not.toContain("beta")
    // …and ↓ on the draft stays on the draft.
    stdin.write(DOWN)
    await settle()
    expect(lastFrame()).not.toContain("beta")
    unmount()
  })

  test("a half-typed draft is saved on ↑ and restored after ↓", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["alpha"])
    stdin.write("draf")
    await settle()
    stdin.write("t")
    await settle()
    expect(lastFrame()).toContain("draft")

    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("alpha")

    stdin.write(DOWN)
    await settle()
    expect(lastFrame()).toContain("draft")
    unmount()
  })

  test("editing a recalled entry keeps its position for further ↑/↓", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(["one", "two"])
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("two")

    // Walk to the oldest entry and edit it — ↑ must not move away.
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("one")
    stdin.write("!")
    await settle()
    expect(lastFrame()).toContain("one!")

    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("one!")

    stdin.write(DOWN)
    await settle()
    expect(lastFrame()).toContain("two")
    unmount()
  })

  test("enter submits the recalled entry and resets to the live line", async () => {
    const { stdin, lastFrame, unmount, submitted } = await renderPrompt(["solo"])
    stdin.write(UP)
    await settle()
    stdin.write(ENTER)
    await settle()

    expect(submitted).toEqual(["solo"])
    expect(lastFrame()).not.toContain("solo")

    // Position is back at the draft: ↑ recalls from newest again.
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("solo")
    unmount()
  })

  test("typing then enter submits the typed value", async () => {
    const { stdin, lastFrame, unmount, submitted } = await renderPrompt([])
    stdin.write("hello")
    await settle()
    expect(lastFrame()).toContain("hello")
    stdin.write(ENTER)
    await settle()
    expect(submitted).toEqual(["hello"])
    expect(lastFrame()).not.toContain("hello")
    unmount()
  })

  test("↑ on an empty history stays on the live line", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt([])
    stdin.write("x")
    await settle()
    stdin.write(UP)
    await settle()
    expect(lastFrame()).toContain("x")
    unmount()
  })

  test("historyRecallEnabled=false: arrows neither recall nor disturb typing", async () => {
    const { stdin, lastFrame, unmount } = await renderPrompt(
      ["alpha", "beta"],
      false
    )
    stdin.write("draf")
    await settle()
    stdin.write(UP)
    await settle()
    stdin.write(UP)
    await settle()
    // The draft is untouched — the viewport scrolled instead.
    expect(lastFrame()).toContain("draf")
    expect(lastFrame()).not.toContain("beta")
    stdin.write(DOWN)
    await settle()
    expect(lastFrame()).not.toContain("alpha")
    unmount()
  })

  test("historyRecallEnabled=false: enter still submits the typed line", async () => {
    const { stdin, unmount, submitted } = await renderPrompt(["old"], false)
    stdin.write("hi")
    await settle()
    stdin.write(ENTER)
    await settle()
    expect(submitted).toEqual(["hi"])
    unmount()
  })
})
