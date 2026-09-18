import { describe, expect, test } from "bun:test"
import { render } from "ink-testing-library"
import React from "react"
import { CommandPalette } from "./CommandPalette"

function settle(ms = 60): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

describe("CommandPalette", () => {
  test("filters existing slash commands and dispatches through command text", async () => {
    const selected: string[] = []
    const { stdin, lastFrame, unmount } = render(
      <CommandPalette
        width={64}
        onSelect={(command) => selected.push(command)}
        onClose={() => {}}
      />
    )
    await settle()
    stdin.write("status")
    await settle()

    expect(lastFrame() ?? "").toContain("/status")
    expect(lastFrame() ?? "").not.toContain("/retry")
    stdin.write("\r")
    await settle()
    expect(selected).toEqual(["/status"])
    unmount()
  })

  test("escape closes without dispatching", async () => {
    const closed: number[] = []
    const selected: string[] = []
    const { stdin, unmount } = render(
      <CommandPalette
        width={64}
        onSelect={(command) => selected.push(command)}
        onClose={() => closed.push(1)}
      />
    )
    await settle()
    stdin.write("\x1b")
    await settle()

    expect(closed).toEqual([1])
    expect(selected).toEqual([])
    unmount()
  })
})
