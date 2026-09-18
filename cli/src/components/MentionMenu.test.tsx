/**
 * Mention-menu integration: the hook + PromptInput seams through
 * ink-testing-library's mock stdin. Covers the open-after-debounce
 * path, keyboard navigation, accept (slug insertion), Esc dismissal
 * and the one-char / failure guards.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { Box } from "ink"
import { useMentionMenu } from "./MentionMenu"
import { PromptInput } from "./PromptInput"
import type { MentionHit } from "../lib/mentions"

function settle(ms = 100): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

const TAB = "\t"
const ENTER = "\r"
const ESC = "\x1b"
const DOWN = "\x1b[B"

function hit(documentId: string, snippet: string, score = 0.9): MentionHit {
  return { documentId, snippet, score }
}

interface HostProps {
  readonly search: (query: string) => Promise<readonly MentionHit[]>
  readonly onUnavailable?: () => void
}

function Host({ search, onUnavailable }: HostProps) {
  const menu = useMentionMenu({ search, width: 60, onUnavailable })
  return (
    <Box flexDirection="column">
      {menu.element}
      <PromptInput onSubmit={() => {}} {...menu.promptBindings} />
    </Box>
  )
}

describe("useMentionMenu", () => {
  test("opens after the debounce only, tab accepts the slug", async () => {
    const search = async () => [
      hit("d1", "Identity Module\nhandles authentication."),
    ]
    const { stdin, lastFrame, unmount } = render(<Host search={search} />)
    await settle()

    stdin.write("@id")
    await settle(100)
    expect(lastFrame() ?? "").not.toContain("Identity Module")

    await settle(300)
    expect(lastFrame() ?? "").toContain("Identity Module")

    stdin.write(TAB)
    await settle()
    expect(lastFrame() ?? "").toContain("@identity-module")
    unmount()
  })

  test("one-char tokens never open the popup", async () => {
    const search = async () => [hit("d1", "Identity Module")]
    const { stdin, lastFrame, unmount } = render(<Host search={search} />)
    await settle()
    stdin.write("@i")
    await settle(400)
    expect(lastFrame() ?? "").not.toContain("Identity Module")
    unmount()
  })

  test("down-arrow moves the selection, enter accepts it", async () => {
    const search = async () => [
      hit("d1", "Alpha Doc\nfirst", 0.9),
      hit("d2", "Beta Doc\nsecond", 0.8),
    ]
    const { stdin, lastFrame, unmount } = render(<Host search={search} />)
    await settle()
    stdin.write("@doc")
    await settle(400)
    expect(lastFrame() ?? "").toContain("> Alpha Doc")

    stdin.write(DOWN)
    await settle()
    expect(lastFrame() ?? "").toContain("> Beta Doc")

    stdin.write(ENTER)
    await settle()
    expect(lastFrame() ?? "").toContain("@beta-doc")
    unmount()
  })

  test("esc dismisses until the token changes", async () => {
    const search = async () => [hit("d1", "Identity Module")]
    const { stdin, lastFrame, unmount } = render(<Host search={search} />)
    await settle()
    stdin.write("@id")
    await settle(400)
    expect(lastFrame() ?? "").toContain("Identity Module")

    stdin.write(ESC)
    await settle()
    expect(lastFrame() ?? "").not.toContain("Identity Module")

    // Same token re-typed (backspace + the same char) stays dismissed.
    stdin.write("\x7f")
    await settle()
    stdin.write("d")
    await settle(400)
    expect(lastFrame() ?? "").not.toContain("Identity Module")

    // A different token reopens.
    stdin.write("e")
    await settle(400)
    expect(lastFrame() ?? "").toContain("Identity Module")
    unmount()
  })

  test("a failed search reports unavailability and stays quiet", async () => {
    let unavailable = false
    const search = async () => {
      throw new Error("HTTP 403")
    }
    const { stdin, lastFrame, unmount } = render(
      <Host search={search} onUnavailable={() => (unavailable = true)} />
    )
    await settle()
    stdin.write("@id")
    await settle(400)
    expect(lastFrame() ?? "").not.toContain("[@knowledge")
    expect(unavailable).toBe(true)
    unmount()
  })
})
