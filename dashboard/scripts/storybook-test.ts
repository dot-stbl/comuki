// scripts/storybook-test.ts
//
// `bun run test:storybook` — one-shot interaction + visual + a11y pass over
// the "ws16-batch1" story batch (runs/chat domains, see WS16 in
// openspec/changes/add-agentic-test-contour, branch
// feat/agentic-test-contour-spec). Never a dev/watch server: builds a static
// `storybook-static/`, serves it on a scratch port from the 17180-17200 band
// (AGENTS.md §9 / .agents/rules/process/ports.md), runs
// `@storybook/test-runner` against it, writes the design.md report envelope,
// tears the server and test-runner down, and exits with a real code.
//
// Usage:
//   bun run test:storybook                    # compare against committed baselines
//   bun run test:storybook -- --update-snapshots   # (re)write baselines locally
//   bun run test:storybook -- --skip-build         # reuse an existing storybook-static/
//   bun run test:storybook -- --port=17190         # override the scratch port
//
// Baselines are intentionally NOT committed from this script or from a dev
// machine — see storybook-tests/README.md. A missing baseline warns and skips
// outside CI; in CI (`CI=true` or `--ci`) a missing baseline fails the run,
// so baselines only ever come from the dedicated CI baseline-refresh job.

import { type ChildProcess, spawn, spawnSync } from "node:child_process"
import { existsSync, mkdirSync, rmSync } from "node:fs"
import { join, resolve } from "node:path"

import {
  buildReport,
  type JestJsonResult,
  missingJestResult,
  parseJestResult,
  writeReport,
} from "./lib/report"
import { startStaticServer } from "./lib/static-server"

const PORT_POOL_MIN = 17000
const PORT_POOL_MAX = 17200
const RESERVED_PORTS = new Set([17010, 17173])
const DEFAULT_PORT = 17186
const DEFAULT_TIMEOUT_MS = 480_000 // 8 min — inside the caller's `timeout 900`

const ROOT = process.cwd()
const STORYBOOK_STATIC_DIR = resolve(ROOT, "storybook-static")
const REPORT_DIR = resolve(ROOT, "storybook-tests")
const DIFF_DIR = join(REPORT_DIR, "diffs")
const ARTIFACT_DIR = join(REPORT_DIR, "artifacts")

export class PortRangeError extends Error {
  override readonly name = "PortRangeError"
  readonly port: number

  constructor(port: number) {
    super(`port ${port} is outside the Comuki pool ${PORT_POOL_MIN}-${PORT_POOL_MAX} (.agents/rules/process/ports.md)`)
    this.port = port
  }
}

export class PortReservedError extends Error {
  override readonly name = "PortReservedError"
  readonly port: number

  constructor(port: number) {
    super(`port ${port} is reserved (storybook dev=17010 / dashboard=17173) — pick another port in 17180-17200`)
    this.port = port
  }
}

export class BuildFailedError extends Error {
  override readonly name = "BuildFailedError"
  readonly exitCode: number

  constructor(exitCode: number) {
    super(`bun run build-storybook exited ${exitCode}`)
    this.exitCode = exitCode
  }
}

export class TestRunTimeoutError extends Error {
  override readonly name = "TestRunTimeoutError"
  readonly timeoutMs: number

  constructor(timeoutMs: number) {
    super(`test-storybook did not finish within ${timeoutMs}ms — killed`)
    this.timeoutMs = timeoutMs
  }
}

interface CliOptions {
  readonly updateSnapshots: boolean
  readonly skipBuild: boolean
  readonly port: number
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
  const portArg = argv.find((arg) => arg.startsWith("--port="))
  const port = portArg
    ? Number(portArg.slice("--port=".length))
    : numberFromEnv(process.env.STORYBOOK_TEST_PORT, DEFAULT_PORT)

  return {
    updateSnapshots: argv.includes("--update-snapshots"),
    skipBuild: argv.includes("--skip-build"),
    port,
    ci: isTruthyEnv(process.env.CI) || argv.includes("--ci"),
    timeoutMs: numberFromEnv(process.env.STORYBOOK_TEST_TIMEOUT_MS, DEFAULT_TIMEOUT_MS),
  }
}

function assertPoolPort(port: number): void {
  if (!Number.isInteger(port) || port < PORT_POOL_MIN || port > PORT_POOL_MAX) {
    throw new PortRangeError(port)
  }
  if (RESERVED_PORTS.has(port)) {
    throw new PortReservedError(port)
  }
}

function resetDir(path: string): void {
  rmSync(path, { recursive: true, force: true })
  mkdirSync(path, { recursive: true })
}

function runBuildStorybook(): void {
  console.log("[test:storybook] building static storybook (bun run build-storybook)...")
  const result = spawnSync("bun", ["run", "build-storybook"], {
    stdio: "inherit",
    cwd: ROOT,
  })
  if (result.error) {
    throw result.error
  }
  if (result.status !== 0) {
    throw new BuildFailedError(result.status ?? -1)
  }
}

function killTree(child: ChildProcess): void {
  if (child.pid === undefined) {
    return
  }
  if (process.platform === "win32") {
    // Exact-PID tree kill, not a blanket `taskkill /IM node.exe`
    // (.agents/rules/process/agent-runtime-safety.md).
    spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"])
    return
  }
  try {
    process.kill(-child.pid, "SIGKILL")
  } catch {
    child.kill("SIGKILL")
  }
}

export interface TestStorybookRun {
  readonly exitCode: number
  readonly stdout: string
}

function spawnTestStorybook(url: string, options: CliOptions): Promise<TestStorybookRun> {
  return new Promise((resolveRun, rejectRun) => {
    const env: NodeJS.ProcessEnv = {
      ...process.env,
      STORYBOOK_TEST_UPDATE_SNAPSHOTS: options.updateSnapshots ? "1" : "0",
      STORYBOOK_TEST_CI: options.ci ? "1" : "0",
      // storybook/internal/common's getProjectRoot() walks up looking for a
      // `.git` *directory* and stops at the first one it finds. A worktree's
      // `.git` is a *file* (pointing at the real gitdir), so the walk skips
      // right past it and lands on whichever ancestor repo happens to own
      // the real `.git` directory — in this harness's worktree layout
      // (`.claude/worktrees/<agent>/` nested inside the main checkout) that
      // is the main checkout, not this worktree, which then makes Jest scan
      // every sibling worktree as `rootDir` and find zero of *our* stories.
      // Pin it explicitly instead of relying on that walk. Forward-slashed:
      // storybook/internal/common returns this value completely unprocessed,
      // and the story-glob code downstream joins it with `/`-separated glob
      // fragments — a native win32 backslash root produces a mixed-separator
      // pattern that Jest's glob matcher silently matches nothing against.
      STORYBOOK_PROJECT_ROOT: ROOT.split("\\").join("/"),
    }

    const args = [
      "test-storybook",
      "--url",
      url,
      // test-runner only auto-enables index-json mode for a URL it does NOT
      // recognise as a local IP (storybookjs/test-runner's own heuristic is
      // "remote URL => index.json, local IP => scan story files on disk").
      // We serve a static build on 127.0.0.1, which trips that "local" check
      // even though there is no dev server or watch mode here — force it.
      "--index-json",
      // No `--outputFile`: @storybook/test-runner@0.23.0's Commander
      // definition for that flag omits `<value>`, so it can only ever parse
      // as a bare boolean — any path passed after it becomes a stray
      // positional that Jest reads as its own `testPathPattern` instead
      // ("Pattern: <our path> - 0 matches", not the report). `--json` alone
      // is documented (`test-storybook --help`) to send the JSON blob to
      // stdout and everything else to stderr — captured below instead.
      "--json",
      "--maxWorkers",
      "2",
      "--ci",
    ]

    const child = spawn("bunx", args, {
      cwd: ROOT,
      env,
      stdio: ["inherit", "pipe", "inherit"],
      detached: process.platform !== "win32",
    })

    const stdoutChunks: Buffer[] = []
    child.stdout?.on("data", (chunk: Buffer) => {
      stdoutChunks.push(chunk)
    })

    const timeout = setTimeout(() => {
      killTree(child)
      rejectRun(new TestRunTimeoutError(options.timeoutMs))
    }, options.timeoutMs)
    timeout.unref()

    const onSignal = (): void => {
      clearTimeout(timeout)
      killTree(child)
    }
    process.once("SIGINT", onSignal)
    process.once("SIGTERM", onSignal)

    child.once("error", (error) => {
      clearTimeout(timeout)
      rejectRun(error)
    })
    child.once("exit", (code) => {
      clearTimeout(timeout)
      process.removeListener("SIGINT", onSignal)
      process.removeListener("SIGTERM", onSignal)
      resolveRun({ exitCode: code ?? 1, stdout: Buffer.concat(stdoutChunks).toString("utf8") })
    })
  })
}

/**
 * `test-storybook --json` (0.23.0) does NOT keep stdout to just the JSON
 * blob despite its own `--help` text's claim ("all other test output... to
 * stderr") — it still prints its own status lines there, e.g.
 * `[test-storybook] Detected a remote Storybook URL...` before the JSON and
 * `[test-storybook] Cleaning up <tmpdir>` after it. Jest's own `--json`
 * writer emits the result as a single line, so the line starting with `{`
 * is the payload; everything else on stdout is this CLI's own chatter.
 */
function extractJsonLine(stdout: string): string | null {
  for (const line of stdout.split("\n")) {
    const candidate = line.trim()
    if (candidate.startsWith("{") && candidate.endsWith("}")) {
      return candidate
    }
  }
  return null
}

function readJestResult(stdout: string): JestJsonResult {
  const jsonLine = extractJsonLine(stdout)
  if (jsonLine === null) {
    return missingJestResult(
      "test-storybook produced no parseable JSON on stdout — see the inherited stderr above for the underlying failure (build/serve/timeout)."
    )
  }
  return parseJestResult(jsonLine)
}

async function main(): Promise<void> {
  const options = parseArgs(process.argv.slice(2))
  assertPoolPort(options.port)

  if (!options.skipBuild) {
    runBuildStorybook()
  } else if (!existsSync(STORYBOOK_STATIC_DIR)) {
    throw new BuildFailedError(-1)
  }

  resetDir(DIFF_DIR)
  resetDir(ARTIFACT_DIR)
  mkdirSync(REPORT_DIR, { recursive: true })

  console.log(`[test:storybook] serving storybook-static/ on http://127.0.0.1:${options.port}`)
  const server = await startStaticServer(STORYBOOK_STATIC_DIR, options.port)

  const startedAt = new Date()
  let run: TestStorybookRun

  try {
    run = await spawnTestStorybook(server.url, options)
  } finally {
    await server.close()
  }

  const durationMs = Date.now() - startedAt.getTime()
  const jestResult = readJestResult(run.stdout)

  const report = buildReport(jestResult, {
    tier: "ui-storybook",
    mode: options.updateSnapshots ? "update-snapshots" : "compare",
    startedAt: startedAt.toISOString(),
    durationMs,
  })

  writeReport(report, join(REPORT_DIR, "report.json"), join(REPORT_DIR, "report.md"))

  const verdict =
    report.summary.failed === 0
      ? `PASS ${report.summary.passed}/${report.summary.total}`
      : `FAIL ${report.summary.failed}/${report.summary.total} — see storybook-tests/report.md`
  console.log(`[test:storybook] ${verdict}`)

  process.exitCode = run.exitCode === 0 && report.summary.failed === 0 ? 0 : 1
}

main().catch((error: unknown) => {
  console.error("[test:storybook] FATAL:", error instanceof Error ? error.message : error)
  process.exitCode = 1
})
