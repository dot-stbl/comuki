import { describe, expect, test } from "bun:test"
import {
  hasContextualWorkbench,
  resolveHarnessLayout,
} from "./harness-layout"

describe("resolveHarnessLayout", () => {
  test("uses one header row and no workbench below 60 columns", () => {
    const layout = resolveHarnessLayout(59, true)

    expect(layout.mode).toBe("compact")
    expect(layout.topBarRows).toBe(1)
    expect(layout.workbenchWidth).toBe(0)
    expect(layout.workspaceWidth).toBe(59)
  })

  test("gives the conversation the full width through 109 columns", () => {
    const layout = resolveHarnessLayout(100, true)

    expect(layout.mode).toBe("standard")
    expect(layout.workbenchWidth).toBe(0)
    expect(layout.workspaceWidth).toBe(100)
  })

  test("shows a contextual workbench at 110 columns and above", () => {
    const layout = resolveHarnessLayout(110, true)

    expect(layout.mode).toBe("wide")
    expect(layout.workbenchWidth).toBe(34)
    expect(layout.dividerWidth).toBe(1)
    expect(layout.workspaceWidth).toBe(75)
  })

  test("does not reserve an empty workbench merely for wide layout", () => {
    const layout = resolveHarnessLayout(140, false)

    expect(layout.mode).toBe("wide")
    expect(layout.workbenchWidth).toBe(0)
    expect(layout.workspaceWidth).toBe(140)
  })

  test("never returns a zero-width workspace", () => {
    expect(resolveHarnessLayout(0, true).workspaceWidth).toBe(1)
  })

  test("operational context, not session count, gates the workbench", () => {
    expect(hasContextualWorkbench({ status: "idle" })).toBe(false)
    expect(hasContextualWorkbench({ status: "thinking" })).toBe(false)
    expect(hasContextualWorkbench({ status: "running" })).toBe(true)
    expect(hasContextualWorkbench({ awaitingApproval: true })).toBe(true)
    expect(hasContextualWorkbench({ pendingPlan: { nodes: [] } })).toBe(true)
    expect(hasContextualWorkbench({ runsFeed: { rows: [] } })).toBe(true)
  })
})
