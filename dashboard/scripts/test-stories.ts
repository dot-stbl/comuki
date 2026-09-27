// scripts/test-stories.ts
//
// `bun run test:stories` — one-shot interaction + visual + a11y pass over
// the tagged story batch, on the vitest `stories` project (browser mode,
// real chromium — see storybook-tests/harness.spec.ts and
// storybook-tests/README.md). Never a dev/watch server: no static build
// and no scratch HTTP server are involved anymore — vite serves the spec
// and the story modules directly, the harness middleware serves baseline
// and diff bytes.
//
// This is the port of the old `scripts/storybook-test.ts` (WS16,
// @storybook/test-runner against a built storybook-static/): same report
// envelope (storybook-tests/report.json + .md), same env knobs, same
// exit-code semantics — only the runner underneath changed.
//
// Usage:
//   bun run test:stories                              # compare against committed baselines
//   bun run test:stories -- --update-snapshots       # (re)write baselines locally
//   bun run test:stories -- --ci                     # missing baseline fails (CI mode)
//
// Baselines are intentionally NOT committed from this script or from a dev
// machine — see storybook-tests/README.md. A missing baseline warns and
// skips outside CI; in CI a missing baseline fails the run, so baselines
// only ever come from the dedicated CI baseline-refresh job.

import { spawnSync } from "node:child_process"
import { existsSync, mkdirSync, readFileSync, rmSync } from "node:fs"
import { join, resolve } from "node:path"

import {
  buildReport,
  type JestJsonResult,
  missingJestResult,
  parseJestResult,
  writeReport,
} from "./lib/report"

const ROOT = process.cwd()
const REPORT_DIR = resolve(ROOT, "storybook-tests")
const DIFF_DIR = join(REPORT_DIR, "diffs")
const ARTIFACT_DIR = join(REPORT_DIR, "artifacts")
const VITEST_JSON = join(ARTIFACT_DIR, "vitest.json")
const DEFAULT_TIMEOUT_MS = 480_000 // 8 min — inside the caller's `timeout 900`

interface CliOptions {
  readonly updateSnapshots: boolean
  readonly ci: boolean
  readonly timeoutMs: number
}

function isTruthyEnv(value: string | undefined): boolean {
  return value === "1" || value === "true"
}

function numberFromEnv(value: string | undefined, fallback: number): number {
  const parsed = value === undefined ? Number.NaN : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function parseArgs(argv: readonly string[]): CliOptions {
  return {
    updateSnapshots: argv.includes("--update-snapshots"),
    ci: isTruthyEnv(process.env.CI) || argv.includes("--ci"),
    timeoutMs: numberFromEnv(process.env.STORYBOOK_TEST_TIMEOUT_MS, DEFAULT_TIMEOUT_MS),
  }
}

function resetDir(path: string): void {
  rmSync(path, { recursive: true, force: true })
  mkdirSync(path, { recursive: true })
}

export class TestRunTimeoutError extends Error {
  override readonly name = "TestRunTimeoutError"
  readonly timeoutMs: number

  constructor(timeoutMs: number) {
    super(`vitest --project stories did not finish within ${timeoutMs}ms`)
    this.timeoutMs = timeoutMs
  }
}

interface VitestRun {
  readonly exitCode: number | null
  readonly timedOut: boolean
}

function runVitestStories(options: CliOptions): VitestRun {
  // vitest's json reporter is jest-shaped on purpose, so report.ts's
  // parseJestResult validates it as-is; `--outputFile.json=` keeps the
  // default reporter on the console while the blob lands in a file.
  const result = spawnSync(
    "bunx",
    [
      "vitest",
      "run",
      "--project",
      "stories",
      "--reporter=default",
      "--reporter=json",
      `--outputFile.json=${VITEST_JSON}`,
    ],
    {
      cwd: ROOT,
      stdio: "inherit",
      env: {
        ...process.env,
        STORYBOOK_TEST_UPDATE_SNAPSHOTS: options.updateSnapshots ? "1" : "0",
        STORYBOOK_TEST_CI: options.ci ? "1" : "0",
        STORYBOOK_TEST_TIMEOUT_MS: String(options.timeoutMs),
      },
      timeout: options.timeoutMs,
    }
  )

  if (result.error !== undefined && result.error.name === "TimeoutError" && result.signal === "SIGTERM") {
    throw new TestRunTimeoutError(options.timeoutMs)
  }
  if (result.error !== undefined) {
    throw result.error
  }
  return { exitCode: result.status, timedOut: false }
}

function readVitestJson(): JestJsonResult {
  if (!existsSync(VITEST_JSON)) {
    return missingJestResult(
      "vitest produced no JSON report — see the inherited console output above for the underlying failure (crash/timeout)."
    )
  }
  return parseJestResult(readFileSync(VITEST_JSON, "utf8"))
}

function main(): void {
  const options = parseArgs(process.argv.slice(2))

  mkdirSync(REPORT_DIR, { recursive: true })
  resetDir(DIFF_DIR)
  resetDir(ARTIFACT_DIR)

  const startedAt = new Date()
  const run = runVitestStories(options)
  const durationMs = Date.now() - startedAt.getTime()

  const jestResult = readVitestJson()
  const report = buildReport(jestResult, {
    tier: "ui-storybook",
    mode: options.updateSnapshots ? "update-snapshots" : "compare",
    startedAt: startedAt.toISOString(),
    durationMs,
  })

  writeReport(report, join(REPORT_DIR, "report.json"), join(REPORT_DIR, "report.md"))

  const verdict =
    run.exitCode === 0 && report.summary.failed === 0
      ? `PASS ${report.summary.passed}/${report.summary.total}`
      : `FAIL ${report.summary.failed || 1}/${Math.max(report.summary.total, 1)} — see storybook-tests/report.md`
  console.log(`[test:stories] ${verdict}`)

  process.exitCode = run.exitCode === 0 && report.summary.failed === 0 ? 0 : 1
}

main()
