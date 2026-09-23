// scripts/lib/ui-probe-report.ts
//
// Pure report building for `ui:probe` (WS17). Same agent-facing envelope
// shape as `scripts/lib/report.ts` (design.md "Report format for agents":
// schemaVersion, tier, mode, summary, failures[], cost) plus the
// probe-specific fields a coding agent actually reads a UI probe for — one
// `renders[]` entry per theme with paths to every artifact captured for it.
//
// No I/O except `writeReport`'s two `writeFileSync` calls — everything else
// here is a pure function of its arguments, so `ui-probe.ts` stays a thin
// shell around this and `ui-probe-args.ts`.

import { writeFileSync } from "node:fs"

import type { ProbeMode, Theme, Viewport } from "./ui-probe-args"

export type RenderStatus = "ok" | "error"

export interface RenderArtifacts {
  readonly screenshot: string
  readonly dom: string
  readonly ariaSnapshot: string
  readonly axeViolations: string
  readonly console: string
}

export interface RenderResult {
  readonly theme: Theme
  readonly status: RenderStatus
  /** Set when the render itself failed (timeout, storyMissing, navigation error, uncaught page error during load). */
  readonly error: string | null
  readonly consoleErrorCount: number
  readonly pageErrorCount: number
  readonly axeViolationCount: number
  readonly artifacts: RenderArtifacts
}

export interface ReportFailure {
  readonly scenario: string
  readonly stage: string
  readonly message: string
  readonly artifactPaths: readonly string[]
}

export interface ReportSummary {
  readonly total: number
  readonly passed: number
  readonly failed: number
  readonly skipped: number
}

export interface UiProbeReport {
  readonly schemaVersion: 1
  readonly tier: "ui-probe"
  readonly mode: ProbeMode
  readonly target: string
  readonly viewport: Viewport
  readonly startedAt: string
  readonly durationMs: number
  readonly summary: ReportSummary
  readonly renders: readonly RenderResult[]
  readonly failures: readonly ReportFailure[]
  readonly cost: { readonly usdMicros: number; readonly tokensIn: number; readonly tokensOut: number }
}

export interface BuildReportOptions {
  readonly mode: ProbeMode
  readonly target: string
  readonly viewport: Viewport
  readonly startedAt: string
  readonly durationMs: number
}

/**
 * A render's `status` is "error" only for a hard render failure (page crash,
 * storyMissing, readiness timeout, uncaught exception during load) — never
 * for console errors or axe violations by themselves. Those are still
 * counted and written to disk (the whole point of the probe is to hand an
 * agent that data), but folding them into pass/fail by default would make
 * every story with pre-existing a11y debt "fail" a tool whose job is to
 * report, not to gate (see storybook-tests/README.md's allowlist approach
 * for the same tension). `--fail-on-console-error` / `--fail-on-axe` opt in
 * to stricter gating for a caller that wants one.
 */
export function renderStatus(
  render: Pick<RenderResult, "error" | "consoleErrorCount" | "axeViolationCount">,
  options: { readonly failOnConsoleError: boolean; readonly failOnAxe: boolean }
): RenderStatus {
  if (render.error !== null) {
    return "error"
  }
  if (options.failOnConsoleError && render.consoleErrorCount > 0) {
    return "error"
  }
  if (options.failOnAxe && render.axeViolationCount > 0) {
    return "error"
  }
  return "ok"
}

export function buildReport(renders: readonly RenderResult[], options: BuildReportOptions): UiProbeReport {
  const failed = renders.filter((render) => render.status === "error").length
  const passed = renders.length - failed

  const failures: ReportFailure[] = renders
    .filter((render) => render.status === "error")
    .map((render) => ({
      scenario: `${options.target} (${render.theme})`,
      stage: "render",
      message: render.error ?? "render failed",
      artifactPaths: [render.artifacts.screenshot, render.artifacts.dom, render.artifacts.console],
    }))

  return {
    schemaVersion: 1,
    tier: "ui-probe",
    mode: options.mode,
    target: options.target,
    viewport: options.viewport,
    startedAt: options.startedAt,
    durationMs: options.durationMs,
    summary: {
      total: renders.length,
      passed,
      failed,
      skipped: 0,
    },
    renders,
    failures,
    cost: { usdMicros: 0, tokensIn: 0, tokensOut: 0 },
  }
}

function escapeCell(value: string): string {
  return value.replace(/\|/g, "\\|").replace(/\n/g, " ")
}

export function renderMarkdown(report: UiProbeReport): string {
  const lines: string[] = []
  lines.push("# UI probe report")
  lines.push("")
  lines.push(`- mode: \`${report.mode}\` / target: \`${report.target}\``)
  lines.push(`- viewport: ${report.viewport.width}x${report.viewport.height}`)
  lines.push(`- started: ${report.startedAt}`)
  lines.push(`- duration: ${(report.durationMs / 1000).toFixed(1)}s`)
  lines.push(
    `- summary: **${report.summary.passed}/${report.summary.total} ok**, ` +
      `${report.summary.failed} failed`
  )
  lines.push("")

  lines.push("## Renders")
  lines.push("")
  lines.push("| theme | status | console errors | page errors | axe violations | screenshot | dom | aria | axe | console log |")
  lines.push("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
  for (const render of report.renders) {
    lines.push(
      `| ${render.theme} | ${render.status} | ${render.consoleErrorCount} | ${render.pageErrorCount} | ` +
        `${render.axeViolationCount} | \`${render.artifacts.screenshot}\` | \`${render.artifacts.dom}\` | ` +
        `\`${render.artifacts.ariaSnapshot}\` | \`${render.artifacts.axeViolations}\` | \`${render.artifacts.console}\` |`
    )
  }
  lines.push("")

  if (report.failures.length === 0) {
    lines.push("No render failures.")
    lines.push("")
    return lines.join("\n")
  }

  const [first] = report.failures
  if (first !== undefined) {
    lines.push("## First failure")
    lines.push("")
    lines.push(`**${first.scenario}** — stage \`${first.stage}\``)
    lines.push("")
    lines.push("```")
    lines.push(first.message)
    lines.push("```")
    lines.push("")
    lines.push("Artifacts:")
    for (const path of first.artifactPaths) {
      lines.push(`- \`${path}\``)
    }
    lines.push("")
  }

  if (report.failures.length > 1) {
    lines.push("## All failures")
    lines.push("")
    lines.push("| scenario | stage | message |")
    lines.push("| --- | --- | --- |")
    for (const failure of report.failures) {
      lines.push(`| ${escapeCell(failure.scenario)} | ${failure.stage} | ${escapeCell(failure.message)} |`)
    }
    lines.push("")
  }

  return lines.join("\n")
}

export function writeReport(report: UiProbeReport, jsonPath: string, markdownPath: string): void {
  writeFileSync(jsonPath, `${JSON.stringify(report, null, 2)}\n`)
  writeFileSync(markdownPath, renderMarkdown(report))
}
