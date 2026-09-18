/**
 * useMouse tests: DECSET enable/disable around the mount, plus the
 * live hook driven through ink-testing-library's mock stdin (the same
 * `input` emitter useInput listens on).
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { Text, useInput } from "ink"
import { render } from "ink-testing-library"
import { DISABLE_MOUSE, ENABLE_MOUSE } from "../lib/mouse"
import { useMouse, type MouseClick } from "./useMouse"

function settle(ms = 50): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

describe("useMouse (through mock stdin)", () => {
  test("enables tracking on mount and disables on unmount", async () => {
    const written: string[] = []
    const writer = (sequence: string) => {
      written.push(sequence)
    }
    function Probe() {
      useInput(() => {})
      useMouse(() => {}, true, writer)
      return <Text>probe</Text>
    }
    const { unmount } = render(<Probe />)
    await settle()
    expect(written).toEqual([ENABLE_MOUSE])
    unmount()
    await settle()
    expect(written).toEqual([ENABLE_MOUSE, DISABLE_MOUSE])
  })

  test("left-button press fires onClick; release and other buttons do not", async () => {
    const seen: MouseClick[] = []
    function Probe() {
      useInput(() => {})
      useMouse((click) => seen.push(click))
      return <Text>probe</Text>
    }
    const { stdin, unmount } = render(<Probe />)
    await settle()
    stdin.write("\x1b[<0;12;4M")
    await settle()
    stdin.write("\x1b[<0;12;4m")
    await settle()
    stdin.write("\x1b[<2;3;3M")
    await settle()
    expect(seen).toEqual([{ x: 12, y: 4, button: 0 }])
    unmount()
  })

  test("inactive hook neither enables tracking nor fires clicks", async () => {
    const written: string[] = []
    const seen: MouseClick[] = []
    function Probe() {
      useInput(() => {})
      useMouse(
        (click) => seen.push(click),
        false,
        (sequence) => written.push(sequence)
      )
      return <Text>probe</Text>
    }
    const { stdin, unmount } = render(<Probe />)
    await settle()
    stdin.write("\x1b[<0;1;1M")
    await settle()
    expect(written).toEqual([])
    expect(seen).toEqual([])
    unmount()
  })

  test("keyboard sequences still pass through — the hook ignores them", async () => {
    const keys: string[] = []
    const clicks: MouseClick[] = []
    function Probe() {
      useInput((input) => keys.push(input))
      useMouse((click) => clicks.push(click))
      return <Text>probe</Text>
    }
    const { stdin, unmount } = render(<Probe />)
    await settle()
    stdin.write("a")
    await settle()
    stdin.write("\x1b[<0;2;2M")
    await settle()
    expect(keys).toContain("a")
    expect(clicks).toEqual([{ x: 2, y: 2, button: 0 }])
    unmount()
  })
})
