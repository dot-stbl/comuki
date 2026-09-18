import { describe, expect, test } from "bun:test"
import { resolveHarnessLayout } from "./harness-layout"

describe("resolveHarnessLayout", () => {
  test("uses compact chrome below 60 columns", () => {
    const layout = resolveHarnessLayout(59, true)

    expect(layout.mode).toBe("compact")
    expect(layout.railWidth).toBe(0)
    expect(layout.navigationRows).toBe(1)
    expect(layout.workspaceWidth).toBe(59)
  })

  test("uses a top session strip from 60 through 109 columns", () => {
    const layout = resolveHarnessLayout(100, true)

    expect(layout.mode).toBe("standard")
    expect(layout.navigationRows).toBe(1)
    expect(layout.workspaceWidth).toBe(100)
  })

  test("uses a stable session rail at 110 columns and above", () => {
    const layout = resolveHarnessLayout(110, true)

    expect(layout.mode).toBe("wide")
    expect(layout.railWidth).toBe(24)
    expect(layout.dividerWidth).toBe(1)
    expect(layout.navigationRows).toBe(0)
    expect(layout.workspaceWidth).toBe(85)
  })

  test("does not reserve an empty rail on welcome", () => {
    const layout = resolveHarnessLayout(140, false)

    expect(layout.mode).toBe("wide")
    expect(layout.railWidth).toBe(0)
    expect(layout.workspaceWidth).toBe(140)
  })

  test("never returns a zero-width workspace", () => {
    expect(resolveHarnessLayout(0, true).workspaceWidth).toBe(1)
  })
})
