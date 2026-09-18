/**
 * StatusLine tests: the pure helpers behind the live segments (host
 * extraction, latency thresholds, connection labels) plus frame-level
 * assertions that the chips render — and that the classic one-shot look
 * survives untouched when no live props are passed.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import {
  connectionLabel,
  hostFromUrl,
  latencyLabel,
  latencyTone,
  StatusLine,
} from "./StatusLine"

describe("hostFromUrl", () => {
  test("keeps the host (with port), drops scheme, path and query", () => {
    expect(hostFromUrl("https://api.comuki.dev/ws/runs")).toBe(
      "api.comuki.dev"
    )
    expect(hostFromUrl("http://localhost:17173")).toBe("localhost:17173")
    expect(hostFromUrl("http://localhost:17173/ws/runs?x=1")).toBe(
      "localhost:17173"
    )
  })

  test("returns null for unparsable input", () => {
    expect(hostFromUrl("not a url")).toBeNull()
    expect(hostFromUrl("")).toBeNull()
  })
})

describe("latencyTone", () => {
  test("ok under 200ms, waiting under 1s, error at or above", () => {
    expect(latencyTone(0)).toBe("ok")
    expect(latencyTone(199)).toBe("ok")
    expect(latencyTone(200)).toBe("waiting")
    expect(latencyTone(999)).toBe("waiting")
    expect(latencyTone(1000)).toBe("error")
    expect(latencyTone(9000)).toBe("error")
  })
})

describe("latencyLabel", () => {
  test("rounds to whole milliseconds", () => {
    expect(latencyLabel(42.4)).toBe("42ms")
    expect(latencyLabel(1234)).toBe("1234ms")
  })
})

describe("connectionLabel", () => {
  test("uses the ellipsis forms for transitional states", () => {
    expect(connectionLabel("live")).toBe("live")
    expect(connectionLabel("connecting")).toBe("connecting…")
    expect(connectionLabel("reconnecting")).toBe("reconnecting…")
    expect(connectionLabel("offline")).toBe("offline")
  })
})

describe("StatusLine", () => {
  test("classic look: no connection chip without a hub state", () => {
    const { lastFrame, unmount } = render(<StatusLine identity="dev" />)
    const frame = lastFrame() ?? ""
    expect(frame).toContain("comuki v")
    expect(frame).toContain("dev")
    expect(frame).not.toContain("live")
    expect(frame).not.toContain("offline")
    expect(frame).not.toContain("ms")
    unmount()
  })

  test("shows host, connection state and latency badge on one line", () => {
    const { lastFrame, unmount } = render(
      <StatusLine
        identity="dev"
        project="nova"
        connection="live"
        serverUrl="https://api.comuki.dev/ws/runs"
        latencyMs={42}
      />
    )
    const frame = lastFrame() ?? ""
    expect(frame).toContain("project: nova")
    expect(frame).toContain("api.comuki.dev")
    expect(frame).toContain("live")
    expect(frame).toContain("42ms")
    // The host chip replaces the URL — the scheme never leaks.
    expect(frame).not.toContain("https://")
    expect(frame.trim().split("\n")).toHaveLength(1)
    unmount()
  })

  test("reconnecting state shows the ellipsis label", () => {
    const { lastFrame, unmount } = render(
      <StatusLine identity="dev" connection="reconnecting" />
    )
    expect(lastFrame()).toContain("reconnecting…")
    unmount()
  })

  test("offline (REST-only fallback) stays on the line", () => {
    const { lastFrame, unmount } = render(
      <StatusLine identity="dev" connection="offline" />
    )
    expect(lastFrame()).toContain("offline")
    unmount()
  })

  test("hides the latency badge while no send has completed", () => {
    const { lastFrame, unmount } = render(
      <StatusLine identity="dev" connection="live" latencyMs={null} />
    )
    expect(lastFrame()).not.toContain("ms")
    unmount()
  })

  test("shows the context meter when token data is present", () => {
    const { lastFrame, unmount } = render(
      <StatusLine identity="dev" contextUsed={12_000} contextWindow={128_000} />
    )
    const frame = lastFrame() ?? ""
    expect(frame).toContain("ctx 12k")
    expect(frame).toContain("▮")
    unmount()
  })

  test("hides the context meter when no token data is passed", () => {
    const { lastFrame, unmount } = render(<StatusLine identity="dev" />)
    expect(lastFrame()).not.toContain("ctx ")
    unmount()
  })
})
