// scripts/lib/report.ts
//
// The agent-facing report envelope from `openspec/changes/add-agentic-test-contour`
// design.md ("Report format for agents") — schemaVersion 1, JSON + a markdown
// rendering of the same object, one `failures[]` entry per failing story/stage.
//
// `test-storybook --json --outputFile=<path>` writes Jest's own result shape
// first; `parseJestResult` validates it at the boundary (zod, not `as`) and
// `buildReport` folds it into the shared envelope so `test:storybook`'s report
// looks like every other tier's report an agent reads.

import { writeFileSync } from "node:fs"
import { z } from "zod"

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

export interface ReportCost {
  readonly usdMicros: number
  readonly tokensIn: number
  readonly tokensOut: number
}

export interface StorybookTestReport {
  readonly schemaVersion: 1
  readonly tier: string
  readonly mode: string
  readonly startedAt: string
  readonly durationMs: number
  readonly summary: ReportSummary
  readonly failures: readonly ReportFailure[]
  readonly cost: ReportCost
}

export interface BuildReportOptions {
  readonly tier: string
  readonly mode: string
  readonly startedAt: string
  readonly durationMs: number
}

const jestAssertionResultSchema = z.object({
  fullName: z.string(),
  status: z.string(),
  failureMessages: z.array(z.string()).default([]),
})

const jestTestResultSchema = z.object({
  assertionResults: z.array(jestAssertionResultSchema).default([]),
})

const jestJsonResultSchema = z.object({
  numTotalTests: z.number().default(0),
  numPassedTests: z.number().default(0),
  numFailedTests: z.number().default(0),
  numPendingTests: z.number().default(0),
  testResults: z.array(jestTestResultSchema).default([]),
})

export type JestJsonResult = z.infer<typeof jestJsonResultSchema>

/** Validates Jest's `--json` output at the boundary instead of casting it. */
export function parseJestResult(raw: string): JestJsonResult {
  return jestJsonResultSchema.parse(JSON.parse(raw))
}

/** A synthetic result for when `test-storybook` never wrote its JSON file
 *  (crashed before finishing) — the report still says something happened. */
export function missingJestResult(reason: string): JestJsonResult {
  return {
    numTotalTests: 0,
    numPassedTests: 0,
    numFailedTests: 1,
    numPendingTests: 0,
    testResults: [
      {
        assertionResults: [
          { fullName: "test-storybook", status: "failed", failureMessages: [reason] },
        ],
      },
    ],
  }
}

// The `postVisit` hook in `.storybook/test-runner.ts` prefixes every check
// failure with `[stage:theme]`; a play-function failure carries no such
// prefix. Jest's `failureMessages` renders a thrown `Error` as
// `Error: <message>`, so the tag is not at the very start of the string —
// search for it rather than anchoring to `^`.
const STAGE_PATTERN = /\[(a11y|visual):(dark|light)]/
const ARTIFACT_PATTERN = /(?:diff image at|current saved to|no baseline at) (\S+\.png)/g

function detectStage(message: string): string {
  const match = STAGE_PATTERN.exec(message)
  return match?.[1] ?? "play"
}

function firstLine(message: string): string {
  return message.split("\n")[0]?.trim() ?? message
}

function extractArtifactPaths(message: string): string[] {
  const paths: string[] = []
  for (const match of message.matchAll(ARTIFACT_PATTERN)) {
    const path = match[1]
    if (path !== undefined) {
      paths.push(path)
    }
  }
  return paths
}

export function buildReport(
  jest: JestJsonResult,
  options: BuildReportOptions
): StorybookTestReport {
  const failures: ReportFailure[] = []

  for (const testResult of jest.testResults) {
    for (const assertion of testResult.assertionResults) {
      if (assertion.status !== "failed") {
        continue
      }
      const message = assertion.failureMessages[0] ?? "failed with no message"
      failures.push({
        scenario: assertion.fullName,
        stage: detectStage(message),
        message: firstLine(message),
        artifactPaths: extractArtifactPaths(message),
      })
    }
  }

  return {
    schemaVersion: 1,
    tier: options.tier,
    mode: options.mode,
    startedAt: options.startedAt,
    durationMs: options.durationMs,
    summary: {
      total: jest.numTotalTests,
      passed: jest.numPassedTests,
      failed: jest.numFailedTests,
      skipped: jest.numPendingTests,
    },
    failures,
    cost: { usdMicros: 0, tokensIn: 0, tokensOut: 0 },
  }
}

function escapeCell(value: string): string {
  return value.replace(/\|/g, "\\|")
}

export function renderMarkdown(report: StorybookTestReport): string {
  const lines: string[] = []
  lines.push("# Storybook test report")
  lines.push("")
  lines.push(`- tier: \`${report.tier}\` / mode: \`${report.mode}\``)
  lines.push(`- started: ${report.startedAt}`)
  lines.push(`- duration: ${(report.durationMs / 1000).toFixed(1)}s`)
  lines.push(
    `- summary: **${report.summary.passed}/${report.summary.total} passed**, ` +
      `${report.summary.failed} failed, ${report.summary.skipped} skipped`
  )
  lines.push("")

  if (report.failures.length === 0) {
    lines.push("No failures.")
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
    if (first.artifactPaths.length > 0) {
      lines.push("")
      lines.push("Artifacts:")
      for (const path of first.artifactPaths) {
        lines.push(`- \`${path}\``)
      }
    }
    lines.push("")
  }

  lines.push("## All failures")
  lines.push("")
  lines.push("| story | stage | message |")
  lines.push("| --- | --- | --- |")
  for (const failure of report.failures) {
    lines.push(
      `| ${escapeCell(failure.scenario)} | ${failure.stage} | ${escapeCell(failure.message)} |`
    )
  }
  lines.push("")

  return lines.join("\n")
}

export function writeReport(
  report: StorybookTestReport,
  jsonPath: string,
  markdownPath: string
): void {
  writeFileSync(jsonPath, `${JSON.stringify(report, null, 2)}\n`)
  writeFileSync(markdownPath, renderMarkdown(report))
}
