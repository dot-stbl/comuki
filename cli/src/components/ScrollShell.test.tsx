/**
 * End-to-end smoke test of the scroll wiring exactly as `ChatApp`
 * composes it: `useTranscriptScroll` + `TranscriptViewport` +
 * `PromptInput` (with `historyRecallEnabled`) + the global `useInput`
 * scroll branches, driven through ink-testing-library's mock stdin.
 *
 * This is where the key-transport assumptions are proven:
 *   - PgUp/PgDn as real escape sequences (`\x1b[5~` / `\x1b[6~`, what
 *     ConPTY sends) land in `useInput` and page the viewport;
 *   - ↑/↓ scroll by one line while scrolled up and leave the prompt
 *     draft alone; at the bottom they still recall prompt history.
 */
import { describe, expect, test } from "bun:test"
import React, { useState } from "react"
import { Box, useInput } from "ink"
import { render } from "ink-testing-library"
import { PromptInput } from "./PromptInput"
import { TranscriptViewport } from "./TranscriptViewport"
import { useTranscriptScroll } from "../hooks/useTranscriptScroll"

const PGUP = "\x1b[5~"
const PGDN = "\x1b[6~"
const UP = "\x1b[A"

interface ShellProps {
  readonly lines: readonly string[]
  readonly height: number
}

/** The same wiring `ChatApp` uses for the transcript + prompt block. */
function ScrollShell({ lines, height }: ShellProps) {
  const scroll = useTranscriptScroll(lines.length, height, "s1")
  const [, setSubmitted] = useState<string[]>([])
  useInput((input, key) => {
    if (key.pageUp || input === "\x1b[5~" || input === "[5~") {
      scroll.pageUp()
      return
    }
    if (key.pageDown || input === "\x1b[6~" || input === "[6~") {
      scroll.pageDown()
      return
    }
    if (key.upArrow && scroll.scrolledUp) {
      scroll.lineUp()
      return
    }
    if (key.downArrow && scroll.scrolledUp) {
      scroll.lineDown()
      return
    }
    if (scroll.scrolledUp && !key.ctrl && !key.meta) {
      if (input === "j") {
        scroll.lineDown()
        return
      }
      if (input === "k") {
        scroll.lineUp()
        return
      }
      if (input === "g") {
        scroll.toTop()
        return
      }
      if (input === "G") {
        scroll.toBottom()
        return
      }
    }
  })
  return (
    <Box flexDirection="column">
      <TranscriptViewport
        lines={lines}
        height={height}
        offset={scroll.offset}
        newBelow={scroll.newBelow}
      />
      <PromptInput
        onSubmit={(value) => setSubmitted((current) => [...current, value])}
        history={["recall-me"]}
        historyRecallEnabled={!scroll.scrolledUp}
      />
    </Box>
  )
}

function settle(ms = 60): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

const LINES = Array.from({ length: 30 }, (_, index) => `row-${index}`)

async function renderShell(
  lines: readonly string[] = LINES,
  height = 5
): Promise<ReturnType<typeof render>> {
  const instance = render(<ScrollShell lines={lines} height={height} />)
  await settle()
  return instance
}

describe("ScrollShell — transcript keys end to end", () => {
  test("PgUp escape sequence suspends follow and pages up", async () => {
    const { stdin, lastFrame, unmount } = await renderShell()
    expect(lastFrame() ?? "").toContain("row-29")
    stdin.write(PGUP)
    await settle()
    const frame = lastFrame() ?? ""
    // One viewport up: rows 20-24, the bottom row-29 out of sight.
    expect(frame).toContain("row-20")
    expect(frame).toContain("row-24")
    expect(frame).not.toContain("row-29")
    unmount()
  })

  test("new output while suspended shows the indicator; PgDn resumes", async () => {
    const grown = [...LINES, "row-30", "row-31"]
    const { stdin, rerender, lastFrame, unmount } = await renderShell()
    stdin.write(PGUP)
    await settle()
    rerender(<ScrollShell lines={grown} height={5} />)
    await settle()
    const suspended = lastFrame() ?? ""
    expect(suspended).toContain("new messages")
    // Still anchored on the same content (the indicator reserves one
    // row, so rows 21-24 of the pre-append window 20-24 stay visible).
    expect(suspended).toContain("row-24")
    expect(suspended).not.toContain("row-31")

    stdin.write(PGDN)
    await settle()
    // One page from offset 7 lands at 2 — the indicator stays until
    // the bottom is actually reached.
    expect(lastFrame() ?? "").toContain("new messages")
    stdin.write(PGDN)
    await settle()
    const resumed = lastFrame() ?? ""
    expect(resumed).toContain("row-31")
    expect(resumed).not.toContain("new messages")
    unmount()
  })

  test("↑/↓ scroll by one line while suspended, draft untouched", async () => {
    const { stdin, lastFrame, unmount } = await renderShell()
    stdin.write("draf")
    await settle()
    stdin.write(PGUP)
    await settle()
    stdin.write(UP)
    await settle()
    const frame = lastFrame() ?? ""
    // Window moved one line up: 19-23 (was 20-24).
    expect(frame).toContain("row-19")
    expect(frame).not.toContain("row-24")
    // The prompt draft survived — arrows belonged to the viewport.
    expect(frame).toContain("draf")
    expect(frame).not.toContain("recall-me")
    unmount()
  })

  test("j/k scroll one line while scrolledUp; g/G jump top/bottom", async () => {
    const { stdin, lastFrame, unmount } = await renderShell()
    stdin.write(PGUP)
    await settle()
    stdin.write("k")
    await settle()
    const afterK = lastFrame() ?? ""
    expect(afterK).toContain("row-19")
    expect(afterK).not.toContain("row-24")
    stdin.write("j")
    await settle()
    const afterJ = lastFrame() ?? ""
    expect(afterJ).toContain("row-20")
    expect(afterJ).toContain("row-24")
    stdin.write("g")
    await settle()
    const atTop = lastFrame() ?? ""
    expect(atTop).toContain("row-0")
    expect(atTop).not.toContain("row-29")
    stdin.write("G")
    await settle()
    const atBottom = lastFrame() ?? ""
    expect(atBottom).toContain("row-29")
    expect(atBottom).not.toContain("row-0")
    unmount()
  })

  test("j/k at the bottom insert into the prompt, not scroll", async () => {
    const { stdin, lastFrame, unmount } = await renderShell()
    stdin.write("j")
    await settle()
    stdin.write("k")
    await settle()
    const frame = lastFrame() ?? ""
    expect(frame).toContain("jk")
    expect(frame).toContain("row-29")
    unmount()
  })

  test("at the bottom ↑ still recalls prompt history", async () => {
    const { stdin, lastFrame, unmount } = await renderShell()
    stdin.write(UP)
    await settle()
    expect(lastFrame() ?? "").toContain("recall-me")
    unmount()
  })

  test("↓ back at the bottom returns the arrows to the prompt", async () => {
    const { stdin, lastFrame, unmount } = await renderShell()
    stdin.write(PGUP)
    await settle()
    // 5-row viewport, 30 lines: two PgDn to reach the bottom.
    stdin.write(PGDN)
    await settle()
    stdin.write(PGDN)
    await settle()
    stdin.write(UP)
    await settle()
    // Scrolled up again? No — we are at the bottom, so ↑ recalled.
    expect(lastFrame() ?? "").toContain("recall-me")
    unmount()
  })
})
