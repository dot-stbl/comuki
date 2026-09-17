/**
 * useHomeEndKeys tests: the pure sequence matcher plus the live hook
 * driven through ink-testing-library's mock stdin (the same `input`
 * emitter `useInput` listens on).
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { Text, useInput } from "ink"
import { render } from "ink-testing-library"
import { matchHomeEnd, useHomeEndKeys } from "./useHomeEndKeys"

function settle(ms = 50): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

describe("matchHomeEnd", () => {
  test("xterm-style Home/End", () => {
    expect(matchHomeEnd("\x1b[H")).toBe("home")
    expect(matchHomeEnd("\x1b[F")).toBe("end")
  })

  test("tilde-style Home/End (VT400 / ConPTY / rxvt)", () => {
    expect(matchHomeEnd("\x1b[1~")).toBe("home")
    expect(matchHomeEnd("\x1b[7~")).toBe("home")
    expect(matchHomeEnd("\x1b[4~")).toBe("end")
    expect(matchHomeEnd("\x1b[8~")).toBe("end")
  })

  test("SS3 Home/End", () => {
    expect(matchHomeEnd("\x1bOH")).toBe("home")
    expect(matchHomeEnd("\x1bOF")).toBe("end")
  })

  test("anything else is not Home/End", () => {
    expect(matchHomeEnd("")).toBeNull()
    expect(matchHomeEnd("a")).toBeNull()
    expect(matchHomeEnd("\x1b[A")).toBeNull() // up arrow
    expect(matchHomeEnd("\x1b[B")).toBeNull() // down arrow
    expect(matchHomeEnd("\x1b[5~")).toBeNull() // pgup — useInput's job
    expect(matchHomeEnd("\x1b[6~")).toBeNull() // pgdn
  })
})

describe("useHomeEndKeys (through mock stdin)", () => {
  // The internal `input` emitter only runs while some useInput owns raw
  // mode (App subscribes to stdin `readable` inside setRawMode) — the
  // probe mirrors the chat shell, which always mounts a useInput.
  test("Home and End sequences fire the callbacks", async () => {
    const seen: string[] = []
    function Probe() {
      useInput(() => {})
      useHomeEndKeys(
        () => seen.push("home"),
        () => seen.push("end")
      )
      return <Text>probe</Text>
    }
    const { stdin, unmount } = render(<Probe />)
    await settle()
    stdin.write("\x1b[H")
    await settle()
    stdin.write("\x1b[F")
    await settle()
    stdin.write("\x1b[1~")
    await settle()
    expect(seen).toEqual(["home", "end", "home"])
    unmount()
  })

  test("inactive hook ignores the keys", async () => {
    const seen: string[] = []
    function Probe() {
      useInput(() => {})
      useHomeEndKeys(
        () => seen.push("home"),
        () => seen.push("end"),
        false
      )
      return <Text>probe</Text>
    }
    const { stdin, unmount } = render(<Probe />)
    await settle()
    stdin.write("\x1b[H")
    await settle()
    expect(seen).toEqual([])
    unmount()
  })
})
