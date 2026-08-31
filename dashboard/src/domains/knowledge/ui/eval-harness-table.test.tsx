import { render, screen } from "@testing-library/react"
import { beforeAll, describe, expect, it } from "vitest"

import type { EvalCase } from "@/domains/knowledge/model/types"

import { EvalHarnessTable } from "./eval-harness-table"

/* The virtualizer needs a scroll port with a depth and something watching it,
   and jsdom has neither — without these the body renders no rows at all and
   every assertion below would pass by looking at an empty table. Same stubs as
   `data-table.test.tsx`, for the same reason. */
beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
  Object.defineProperty(HTMLElement.prototype, "offsetHeight", {
    configurable: true,
    value: 320,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 1400,
  })
})

/* The product marks its own elements with `data-test`, not `data-testid`. */
const all = (selector: string) =>
  Array.from(document.querySelectorAll(selector))

const CASES: EvalCase[] = [
  { task: "idempotent-webhook", before: "fail", after: "pass", delta: "+" },
  { task: "jwt-rotation", before: "pass", after: "pass", delta: "=" },
  { task: "table-virtualize", before: "fail", after: "pass", delta: "+" },
  { task: "theme-migrate", before: "pass", after: "fail", delta: "-" },
]

describe("the golden-task harness", () => {
  it("draws a row per task rather than an empty frame", () => {
    render(<EvalHarnessTable cases={CASES} />)

    // The failure this project has shipped from this shape: every gate green on
    // a table that rendered nothing. Assert the rows exist before anything else.
    for (const item of CASES) {
      expect(screen.getByText(item.task)).toBeTruthy()
    }
  })

  it("states the difference in words, not only as two badges to subtract", () => {
    render(<EvalHarnessTable cases={CASES} />)

    const deltas = all('[data-test="eval-delta"]').map((el) => el.textContent)
    expect(deltas).toEqual(["improved", "no change", "improved", "regressed"])
  })

  it("carries the before and after readings as status badges", () => {
    render(<EvalHarnessTable cases={CASES} />)

    const badges = all('[data-test="status-badge"]')
    // Two per row, and every one of them says its own word — the reading is
    // never the hue alone.
    expect(badges).toHaveLength(CASES.length * 2)
    expect(badges[0].getAttribute("data-status")).toBe("failed")
    expect(badges[0].textContent).toContain("fail")
    expect(badges[1].getAttribute("data-status")).toBe("success")
    expect(badges[1].textContent).toContain("pass")
  })

  it("says which nothing it is when no harness run has happened", () => {
    render(<EvalHarnessTable cases={[]} />)

    expect(
      screen.getByText("no golden task has been run against this revision")
    ).toBeTruthy()
  })
})
