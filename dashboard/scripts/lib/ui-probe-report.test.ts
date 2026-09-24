// scripts/lib/ui-probe-report.test.ts
//
// Unit tests for the pure `ui:probe` report builder (WS17). Run with
// `bun test scripts/lib` — see scripts/UI-PROBE.md "Testing the pure
// parts" and the header comment in ui-probe-args.test.ts for why this
// isn't part of `bun run test` (vitest, scoped to `src/**`).

import { mkdtempSync, readFileSync, rmSync } from "node:fs"
import { tmpdir } from "node:os"
import { join } from "node:path"

import { describe, expect, it } from "bun:test"

import { buildReport, renderMarkdown, renderStatus, writeReport, type RenderResult } from "./ui-probe-report"

function okRender(theme: "light" | "dark", overrides: Partial<RenderResult> = {}): RenderResult {
  return {
    theme,
    status: "ok",
    error: null,
    consoleErrorCount: 0,
    pageErrorCount: 0,
    axeViolationCount: 0,
    artifacts: {
      screenshot: `out/x--${theme}.png`,
      dom: `out/x--${theme}.dom.html`,
      ariaSnapshot: `out/x--${theme}.aria.yaml`,
      axeViolations: `out/x--${theme}.axe.json`,
      console: `out/x--${theme}.console.json`,
    },
    ...overrides,
  }
}

describe("renderStatus", () => {
  const lenient = { failOnConsoleError: false, failOnAxe: false }

  it("is ok when nothing went wrong", () => {
    expect(renderStatus({ error: null, consoleErrorCount: 0, axeViolationCount: 0 }, lenient)).toBe("ok")
  })

  it("is error whenever the render itself failed, regardless of flags", () => {
    expect(renderStatus({ error: "timed out", consoleErrorCount: 0, axeViolationCount: 0 }, lenient)).toBe(
      "error"
    )
  })

  it("does not fail on console errors or axe violations by default", () => {
    expect(renderStatus({ error: null, consoleErrorCount: 3, axeViolationCount: 5 }, lenient)).toBe("ok")
  })

  it("fails on console errors only when opted in", () => {
    expect(
      renderStatus({ error: null, consoleErrorCount: 1, axeViolationCount: 0 }, { failOnConsoleError: true, failOnAxe: false })
    ).toBe("error")
  })

  it("fails on axe violations only when opted in", () => {
    expect(
      renderStatus({ error: null, consoleErrorCount: 0, axeViolationCount: 1 }, { failOnConsoleError: false, failOnAxe: true })
    ).toBe("error")
  })
})

describe("buildReport", () => {
  const baseOptions = {
    mode: "story" as const,
    target: "runs-list--default",
    viewport: { width: 1440, height: 900 },
    startedAt: "2026-09-23T12:00:00.000Z",
    durationMs: 1234,
  }

  it("summarises an all-ok run with zero failures", () => {
    const report = buildReport([okRender("dark")], baseOptions)
    expect(report.schemaVersion).toBe(1)
    expect(report.tier).toBe("ui-probe")
    expect(report.summary).toEqual({ total: 1, passed: 1, failed: 0, skipped: 0 })
    expect(report.failures).toEqual([])
  })

  it("counts a failed render and builds a failures[] entry from it", () => {
    const failing = okRender("light", { status: "error", error: "storyMissing" })
    const report = buildReport([okRender("dark"), failing], baseOptions)
    expect(report.summary).toEqual({ total: 2, passed: 1, failed: 1, skipped: 0 })
    expect(report.failures).toHaveLength(1)
    expect(report.failures[0]).toMatchObject({
      scenario: "runs-list--default (light)",
      stage: "render",
      message: "storyMissing",
    })
    expect(report.failures[0]?.artifactPaths).toEqual([
      failing.artifacts.screenshot,
      failing.artifacts.dom,
      failing.artifacts.console,
    ])
  })

  it("cost is always zero — no model spend in a UI probe", () => {
    const report = buildReport([okRender("dark")], baseOptions)
    expect(report.cost).toEqual({ usdMicros: 0, tokensIn: 0, tokensOut: 0 })
  })
})

describe("renderMarkdown", () => {
  const baseOptions = {
    mode: "page" as const,
    target: "/runs",
    viewport: { width: 1440, height: 900 },
    startedAt: "2026-09-23T12:00:00.000Z",
    durationMs: 2500,
  }

  it("renders a clean report without a failures section", () => {
    const markdown = renderMarkdown(buildReport([okRender("dark")], baseOptions))
    expect(markdown).toContain("# UI probe report")
    expect(markdown).toContain("mode: `page` / target: `/runs`")
    expect(markdown).toContain("1/1 ok")
    expect(markdown).toContain("No render failures.")
    expect(markdown).not.toContain("## First failure")
  })

  it("surfaces the first failure with its artifact paths", () => {
    const failing = okRender("light", { status: "error", error: "timed out waiting for storyFinished" })
    const markdown = renderMarkdown(buildReport([failing], baseOptions))
    expect(markdown).toContain("## First failure")
    expect(markdown).toContain("timed out waiting for storyFinished")
    expect(markdown).toContain(failing.artifacts.screenshot)
  })
})

describe("writeReport", () => {
  it("writes matching JSON and Markdown files to disk", () => {
    const dir = mkdtempSync(join(tmpdir(), "ui-probe-report-test-"))
    try {
      const report = buildReport([okRender("dark")], {
        mode: "story",
        target: "runs-list--default",
        viewport: { width: 1440, height: 900 },
        startedAt: "2026-09-23T12:00:00.000Z",
        durationMs: 10,
      })
      const jsonPath = join(dir, "summary.json")
      const markdownPath = join(dir, "summary.md")

      writeReport(report, jsonPath, markdownPath)

      const parsed = JSON.parse(readFileSync(jsonPath, "utf8"))
      expect(parsed.summary).toEqual(report.summary)
      expect(readFileSync(markdownPath, "utf8")).toContain("# UI probe report")
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})
