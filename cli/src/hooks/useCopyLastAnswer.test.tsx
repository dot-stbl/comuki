/**
 * Smoke tests for the ctrl+y copy-last-answer hook: the keypress must
 * call the clipboard writer with the last assistant text and flash a
 * transient hint; an empty transcript must not touch the clipboard.
 *
 * The writer is injected (spy) — tests never touch the OS clipboard.
 */
import { describe, expect, mock, test } from "bun:test"
import React from "react"
import { Text } from "ink"
import { render } from "ink-testing-library"
import {
  useCopyLastAnswer,
  type ClipboardWriter,
} from "./useCopyLastAnswer"

/** Lets ink flush React updates between keystroke and assertion. */
function settle(ms = 100): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

interface HarnessProps {
  readonly getLastAnswer: () => string | undefined
  readonly write: ClipboardWriter
  readonly hintMs: number
}

function CopyHarness({ getLastAnswer, write, hintMs }: HarnessProps) {
  const { hint } = useCopyLastAnswer(getLastAnswer, { write, hintMs })
  return <Text>{hint ?? "no hint"}</Text>
}

interface CopyHandle {
  readonly stdin: { write: (data: string) => void }
  readonly lastFrame: () => string | undefined
  readonly unmount: () => void
}

async function renderCopyHarness(
  getLastAnswer: () => string | undefined,
  write: ClipboardWriter,
  hintMs: number
): Promise<CopyHandle> {
  const { stdin, lastFrame, unmount } = render(
    <CopyHarness getLastAnswer={getLastAnswer} write={write} hintMs={hintMs} />
  )
  // The first keystroke is lost without this: useInput subscribes in an
  // effect that must flush before the mock stdin can deliver anything.
  await settle()
  return { stdin, lastFrame, unmount }
}

const CTRL_Y = "\x19"

describe("useCopyLastAnswer", () => {
  test("ctrl+y writes the last answer and flashes the copied hint", async () => {
    const write = mock((_text: string) => Promise.resolve())
    const { stdin, lastFrame, unmount } = await renderCopyHarness(
      () => "the answer",
      write,
      60_000
    )

    stdin.write(CTRL_Y)
    await settle()

    expect(write).toHaveBeenCalledTimes(1)
    expect(write.mock.calls[0]?.[0]).toBe("the answer")
    expect(lastFrame()).toContain("copied")
    unmount()
  })

  test("ctrl+y with no assistant message hints and never writes", async () => {
    const write = mock((_text: string) => Promise.resolve())
    const { stdin, lastFrame, unmount } = await renderCopyHarness(
      () => undefined,
      write,
      60_000
    )

    stdin.write(CTRL_Y)
    await settle()

    expect(write).not.toHaveBeenCalled()
    expect(lastFrame()).toContain("nothing to copy")
    unmount()
  })

  test("a failed write downgrades the hint to copy failed", async () => {
    const write = mock((_text: string) =>
      Promise.reject(new Error("no clipboard"))
    )
    const { stdin, lastFrame, unmount } = await renderCopyHarness(
      () => "the answer",
      write,
      60_000
    )

    stdin.write(CTRL_Y)
    await settle()

    expect(lastFrame()).toContain("copy failed")
    unmount()
  })

  test("the hint fades after hintMs", async () => {
    const write = mock((_text: string) => Promise.resolve())
    const { stdin, lastFrame, unmount } = await renderCopyHarness(
      () => "the answer",
      write,
      500
    )

    stdin.write(CTRL_Y)
    await settle(150)
    expect(lastFrame()).toContain("copied")

    await settle(700)
    expect(lastFrame()).not.toContain("copied")
    unmount()
  })

  test("plain y without ctrl never reaches the clipboard", async () => {
    const write = mock((_text: string) => Promise.resolve())
    const { stdin, lastFrame, unmount } = await renderCopyHarness(
      () => "the answer",
      write,
      60_000
    )

    stdin.write("y")
    await settle()

    expect(write).not.toHaveBeenCalled()
    expect(lastFrame()).not.toContain("copied")
    unmount()
  })
})
