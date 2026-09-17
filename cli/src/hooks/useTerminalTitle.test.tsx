/**
 * `useTerminalTitle` with a capturing writer: the title is emitted on
 * mount and on every change, and a bare "comuki" is written once on
 * unmount (save-none restore — the prior title is never queried).
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { Text } from "ink"
import { render } from "ink-testing-library"
import { useTerminalTitle } from "./useTerminalTitle"
import { titleSequence } from "../lib/term"

const writes: string[] = []
const writer = (sequence: string) => {
  writes.push(sequence)
}

function TitleProbe({ title }: { readonly title: string }) {
  useTerminalTitle(title, writer)
  return <Text>probe</Text>
}

/** Lets React flush mount effects before an assertion. */
function settle(ms = 30): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

describe("useTerminalTitle", () => {
  test("writes the title on mount and on change, restores comuki on unmount", async () => {
    writes.length = 0
    const { rerender, unmount } = render(<TitleProbe title="comuki — a ⏳" />)
    await settle()
    expect(writes).toEqual([titleSequence("comuki — a ⏳")])

    rerender(<TitleProbe title="comuki — a ✓" />)
    await settle()
    expect(writes).toEqual([
      titleSequence("comuki — a ⏳"),
      titleSequence("comuki — a ✓"),
    ])

    // Same title → no re-write.
    rerender(<TitleProbe title="comuki — a ✓" />)
    await settle()
    expect(writes).toHaveLength(2)

    unmount()
    await settle()
    expect(writes).toEqual([
      titleSequence("comuki — a ⏳"),
      titleSequence("comuki — a ✓"),
      titleSequence("comuki"),
    ])
  })
})
