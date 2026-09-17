import { describe, expect, it } from "bun:test"
import { isEnterInput, routeEnterKey } from "./multiline"

/** A plain enter exactly as ink 5 flags `\r`: return, nothing else. */
const PLAIN = { return: true }

describe("routeEnterKey — newline variants", () => {
  it("shift+enter (flagged) inserts a newline", () => {
    expect(
      routeEnterKey("draft", "\r", { return: true, shift: true })
    ).toBe("newline")
  })

  it("alt/meta+enter (flagged) inserts a newline", () => {
    expect(routeEnterKey("draft", "\r", { return: true, alt: true })).toBe(
      "newline"
    )
    expect(routeEnterKey("draft", "\r", { return: true, meta: true })).toBe(
      "newline"
    )
  })

  it("a bare \\r without the return flag is alt+enter after ink's esc-strip", () => {
    expect(routeEnterKey("draft", "\r", {})).toBe("newline")
  })

  it("LF (\\n — many terminals) inserts a newline, with or without flags", () => {
    expect(routeEnterKey("draft", "\n", {})).toBe("newline")
    expect(routeEnterKey("draft", "\n", { return: false })).toBe("newline")
  })

  it("kitty CSI-u enter with any modifier inserts a newline", () => {
    expect(routeEnterKey("draft", "[13;2u", {})).toBe("newline")
    expect(routeEnterKey("draft", "[13;5u", {})).toBe("newline")
  })
})

describe("routeEnterKey — submit and continuation", () => {
  it("plain enter submits", () => {
    expect(routeEnterKey("hello", "\r", PLAIN)).toBe("submit")
  })

  it("plain enter on a trailing backslash continues the line", () => {
    expect(routeEnterKey("select \\", "\r", PLAIN)).toBe("continue")
    expect(routeEnterKey("\\", "\r", PLAIN)).toBe("continue")
  })

  it("a backslash mid-buffer (not trailing) still submits", () => {
    expect(routeEnterKey("path\\to\\thing x", "\r", PLAIN)).toBe("submit")
  })

  it("newline input wins over the trailing-backslash rule", () => {
    expect(routeEnterKey("tail \\", "\n", {})).toBe("newline")
  })
})

describe("isEnterInput", () => {
  it("flags return, \\r, \\n and kitty CSI-u", () => {
    expect(isEnterInput("\r", PLAIN)).toBe(true)
    expect(isEnterInput("\r", {})).toBe(true)
    expect(isEnterInput("\n", {})).toBe(true)
    expect(isEnterInput("[13;2u", {})).toBe(true)
  })

  it("ignores ordinary characters and other sequences", () => {
    expect(isEnterInput("a", {})).toBe(false)
    expect(isEnterInput("[5~", {})).toBe(false)
    expect(isEnterInput("", {})).toBe(false)
  })
})
