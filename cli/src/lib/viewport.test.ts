/**
 * Viewport math tests: follow-bottom default, offset windows, boundary
 * clamping, short/empty transcripts, degenerate heights.
 */
import { describe, expect, test } from "bun:test"
import {
  clampOffset,
  maxOffset,
  NEW_MESSAGES_INDICATOR,
  viewportSlice,
} from "./viewport"

const TEN = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"]

describe("maxOffset", () => {
  test("0 when the transcript fits the viewport", () => {
    expect(maxOffset(3, 5)).toBe(0)
    expect(maxOffset(5, 5)).toBe(0)
    expect(maxOffset(0, 5)).toBe(0)
  })

  test("lineCount - height once it overflows", () => {
    expect(maxOffset(10, 3)).toBe(7)
    expect(maxOffset(100, 24)).toBe(76)
  })

  test("degenerate height never yields a negative offset", () => {
    expect(maxOffset(5, 0)).toBe(5)
    expect(maxOffset(5, -3)).toBe(5)
  })
})

describe("clampOffset", () => {
  test("keeps in-range offsets untouched", () => {
    expect(clampOffset(TEN, 3, 0)).toBe(0)
    expect(clampOffset(TEN, 3, 4)).toBe(4)
    expect(clampOffset(TEN, 3, 7)).toBe(7)
  })

  test("clamps past-max and negative offsets", () => {
    expect(clampOffset(TEN, 3, 99)).toBe(7)
    expect(clampOffset(TEN, 3, -5)).toBe(0)
  })

  test("short transcript clamps everything to 0 (always following)", () => {
    expect(clampOffset(["a"], 5, 3)).toBe(0)
    expect(clampOffset([], 5, 3)).toBe(0)
  })
})

describe("viewportSlice", () => {
  test("offset 0 returns the last `height` lines (follow bottom)", () => {
    expect(viewportSlice(TEN, 3, 0)).toEqual(["7", "8", "9"])
  })

  test("offset windows scroll up line by line", () => {
    expect(viewportSlice(TEN, 3, 1)).toEqual(["6", "7", "8"])
    expect(viewportSlice(TEN, 3, 2)).toEqual(["5", "6", "7"])
  })

  test("max offset shows the first lines of the transcript", () => {
    expect(viewportSlice(TEN, 3, 7)).toEqual(["0", "1", "2"])
  })

  test("offset past max clamps to the top window", () => {
    expect(viewportSlice(TEN, 3, 100)).toEqual(["0", "1", "2"])
  })

  test("negative offset clamps to the bottom window", () => {
    expect(viewportSlice(TEN, 3, -2)).toEqual(["7", "8", "9"])
  })

  test("short transcript returns itself whole regardless of offset", () => {
    expect(viewportSlice(["a", "b"], 5, 0)).toEqual(["a", "b"])
    expect(viewportSlice(["a", "b"], 5, 4)).toEqual(["a", "b"])
  })

  test("empty transcript renders nothing", () => {
    expect(viewportSlice([], 5, 0)).toEqual([])
    expect(viewportSlice([], 5, 3)).toEqual([])
  })

  test("non-positive height renders nothing", () => {
    expect(viewportSlice(TEN, 0, 0)).toEqual([])
    expect(viewportSlice(TEN, -1, 0)).toEqual([])
  })

  test("viewport exactly the transcript size returns everything", () => {
    expect(viewportSlice(TEN, 10, 0)).toEqual(TEN)
  })
})

describe("NEW_MESSAGES_INDICATOR", () => {
  test("is the dim tail hint rendered at the viewport bottom", () => {
    expect(NEW_MESSAGES_INDICATOR).toBe("v new messages")
  })
})
