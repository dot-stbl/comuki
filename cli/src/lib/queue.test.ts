import { describe, expect, it } from "bun:test"
import {
  dequeueMessage,
  enqueueMessage,
  queueHintLine,
  queuedNoticeLine,
  stoppedNoticeLine,
} from "./queue"
import { stripAnsi } from "../theme"

describe("enqueueMessage", () => {
  it("appends in order", () => {
    expect(enqueueMessage([], "first")).toEqual(["first"])
    expect(enqueueMessage(["first"], "second")).toEqual(["first", "second"])
  })

  it("ignores blank input", () => {
    expect(enqueueMessage(["kept"], "   ")).toEqual(["kept"])
    expect(enqueueMessage([], "")).toEqual([])
  })
})

describe("dequeueMessage", () => {
  it("removes the head and keeps the rest in order", () => {
    expect(dequeueMessage(["a", "b", "c"])).toEqual({
      message: "a",
      rest: ["b", "c"],
    })
    expect(dequeueMessage(["solo"])).toEqual({
      message: "solo",
      rest: [],
    })
  })

  it("empty queue yields undefined and stays empty", () => {
    expect(dequeueMessage([])).toEqual({ message: undefined, rest: [] })
  })
})

describe("notice lines", () => {
  it("queued/stopped are dim event lines with the ⏺ glyph", () => {
    expect(stripAnsi(queuedNoticeLine())).toBe("  ⏺ queued")
    expect(stripAnsi(stoppedNoticeLine())).toBe("  ⏺ stopped")
  })

  it("the queue hint carries the count and the drain rule", () => {
    expect(stripAnsi(queueHintLine(2))).toBe(
      "  ⏺ 2 queued — sends when the turn ends"
    )
    expect(stripAnsi(queueHintLine(1))).toBe(
      "  ⏺ 1 queued — sends when the turn ends"
    )
  })

  it("notices use the faint tier (background noise, never reading text)", () => {
    expect(queuedNoticeLine()).toContain("\x1b[38;2;138;138;143m")
    expect(stoppedNoticeLine()).toContain("\x1b[38;2;138;138;143m")
  })
})
