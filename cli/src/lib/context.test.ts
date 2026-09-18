import { describe, expect, it } from "bun:test"
import {
  contextBar,
  contextMeterLabel,
  DEFAULT_CONTEXT_WINDOW,
  tokensCompact,
} from "./context"

describe("tokensCompact", () => {
  it("keeps sub-thousand counts bare and formats thousands as k", () => {
    expect(tokensCompact(0)).toBe("0")
    expect(tokensCompact(999)).toBe("999")
    expect(tokensCompact(1000)).toBe("1k")
    expect(tokensCompact(12000)).toBe("12k")
    expect(tokensCompact(128_000)).toBe("128k")
    expect(tokensCompact(1500)).toBe("1.5k")
  })
})

describe("contextBar", () => {
  it("renders an 8-char bar from used/window", () => {
    expect(contextBar(0, 128_000)).toBe("▯▯▯▯▯▯▯▯")
    expect(contextBar(64_000, 128_000)).toBe("▮▮▮▮▯▯▯▯")
    expect(contextBar(128_000, 128_000)).toBe("▮▮▮▮▮▮▮▮")
    expect(contextBar(12_000, 128_000)).toBe("▮▯▯▯▯▯▯▯")
  })

  it("clamps over-full and empty/negative windows", () => {
    expect(contextBar(200_000, 128_000)).toBe("▮▮▮▮▮▮▮▮")
    expect(contextBar(-10, 128_000)).toBe("▯▯▯▯▯▯▯▯")
    expect(contextBar(50, 0)).toBe("▯▯▯▯▯▯▯▯")
    expect(contextBar(50, -1)).toBe("▯▯▯▯▯▯▯▯")
  })
})

describe("contextMeterLabel", () => {
  it("joins compact count and the bar", () => {
    expect(contextMeterLabel(12_000, DEFAULT_CONTEXT_WINDOW)).toBe(
      "ctx 12k ▮▯▯▯▯▯▯▯"
    )
    expect(contextMeterLabel(64_000, 128_000)).toBe("ctx 64k ▮▮▮▮▯▯▯▯")
  })
})
