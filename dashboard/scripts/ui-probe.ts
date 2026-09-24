// scripts/ui-probe.ts
//
// `bun run ui:probe` — WS17 of openspec/changes/add-agentic-test-contour
// (branch feat/agentic-test-contour-spec, depends on WS16). See that
// change's tasks.md (WS16/WS17) and design.md ("Report format for agents",
// "Local dev commands") for the spec this implements, and
// scripts/UI-PROBE.md for usage.
//
// Renders exactly one story (or one route, in `--page` mode) in headless
// Chromium, once per requested theme, and writes a screenshot, a DOM
// snapshot, an ARIA accessibility tree, axe violations and the console/page
// error log for each render, plus a summary.json + summary.md a coding
// agent reads without opening a browser itself.
//
// A bounded single-shot invocation, not a dev/watch server — see
// AGENTS.md §6 and scripts/UI-PROBE.md for why that distinction matters
// here. It builds (or reuses) a static artifact, serves it from an
// in-process HTTP server on a scratch port, drives one Playwright browser,
// and tears both down before the process exits — the same shape
// storybook-test.ts (WS16) already established, reusing its
// `lib/static-server.ts`.
//
// Usage:
//   bun run ui:probe -- --story runs-list--default
//   bun run ui:probe -- --story chat-dock--panel-depth --theme both
//   bun run ui:probe -- --page /runs --viewport 1280x800
//   bun run ui:probe -- --story runs-list--default --skip-build --out artifacts/tmp

import { spawnSync } from "node:child_process"
import { existsSync, mkdirSync, writeFileSync } from "node:fs"
import { join, relative, resolve } from "node:path"

import { chromium, type Browser, type ConsoleMessage, type Page } from "@playwright/test"
import { getViolations, injectAxe } from "axe-playwright"

import {
  parseArgs,
  PORT_POOL_MAX,
  PORT_POOL_MIN,
  RESERVED_PORT,
  slugifyTarget,
  UiProbeArgError,
  type Theme,
  type UiProbeOptions,
} from "./lib/ui-probe-args"
import {
  buildReport,
  renderStatus,
  writeReport,
  type RenderResult,
} from "./lib/ui-probe-report"
import { startStaticServer, StaticServerBindError, type StaticServerHandle } from "./lib/static-server"
import { installStoryReadySignal, waitForStoryReady } from "./lib/storybook-ready-signal"

const ROOT = process.cwd()
const STORYBOOK_STATIC_DIR = resolve(ROOT, "storybook-static")
const DASHBOARD_DIST_DIR = resolve(ROOT, "dist")
const DEFAULT_OUT_ROOT = resolve(ROOT, "artifacts/ui-probe")

// A render should finish well inside this; it exists to fail fast on a
// story that genuinely hangs (bad play function, portal never resolves)
// rather than eat the whole `--timeout` budget on one theme.
const RENDER_READY_TIMEOUT_MS = 20_000

export class BuildFailedError extends Error {
  override readonly name = "BuildFailedError"
  readonly command: string
  readonly exitCode: number

  constructor(command: string, exitCode: number) {
    super(`${command} exited ${exitCode}`)
    this.command = command
    this.exitCode = exitCode
  }
}

export class NoAvailablePortError extends Error {
  override readonly name = "NoAvailablePortError"

  constructor(min: number, max: number) {
    super(`no free port in the ui:probe pool ${min}-${max}`)
  }
}

/** A path for display/report purposes — relative to the repo root, forward
 *  slashes on every platform, matching design.md's own `artifactPaths`
 *  examples (`artifacts/add-null-check/run.log`). Filesystem calls
 *  (`writeFileSync`, `page.screenshot`) still use the real, absolute,
 *  OS-native path. */
function toDisplayPath(absolutePath: string): string {
  return relative(ROOT, absolutePath).split("\\").join("/")
}

function usage(): string {
  return [
    "bun run ui:probe -- --story <storyId> [--theme light|dark|both] [--viewport WxH] [--build|--skip-build] [--out <dir>] [--port <port>] [--timeout <ms>]",
    "bun run ui:probe -- --page <route> [same flags] # requires a `vite build` static preview, no dev server",
  ].join("\n")
}

function runBuildStorybook(): void {
  console.log("[ui:probe] building static storybook (bun run build-storybook)...")
  const result = spawnSync("bun", ["run", "build-storybook"], { stdio: "inherit", cwd: ROOT })
  if (result.error) {
    throw result.error
  }
  if (result.status !== 0) {
    throw new BuildFailedError("bun run build-storybook", result.status ?? -1)
  }
}

function runBuildDashboard(): void {
  console.log("[ui:probe] building dashboard (bunx vite build --mode mock)...")
  const result = spawnSync("bunx", ["vite", "build", "--mode", "mock"], { stdio: "inherit", cwd: ROOT })
  if (result.error) {
    throw result.error
  }
  if (result.status !== 0) {
    throw new BuildFailedError("bunx vite build --mode mock", result.status ?? -1)
  }
}

function candidatePorts(explicit: number | null): readonly number[] {
  if (explicit !== null) {
    return [explicit]
  }
  const ports: number[] = []
  for (let port = PORT_POOL_MIN; port <= PORT_POOL_MAX; port += 1) {
    if (port !== RESERVED_PORT) {
      ports.push(port)
    }
  }
  return ports
}

async function bindServer(root: string, explicitPort: number | null): Promise<StaticServerHandle> {
  for (const port of candidatePorts(explicitPort)) {
    try {
      return await startStaticServer(root, port)
    } catch (error) {
      if (error instanceof StaticServerBindError) {
        continue
      }
      throw error
    }
  }
  throw new NoAvailablePortError(PORT_POOL_MIN, PORT_POOL_MAX)
}

// Deliberately not a wipe-and-recreate: `--out` is a user-supplied path, and
// blindly `rm -rf`ing it is not a risk worth taking for a "make sure this
// directory exists" step. Every file this script writes has a fixed,
// deterministic name (`<target>--<theme>.*`, `summary.json`, `summary.md`),
// so re-running just overwrites its own output in place.
function ensureDir(path: string): void {
  mkdirSync(path, { recursive: true })
}

interface ConsoleEntry {
  readonly type: string
  readonly text: string
  readonly location: string | null
}

interface PageErrorEntry {
  readonly message: string
  readonly stack: string | null
}

async function setStorybookTheme(page: Page, theme: Theme): Promise<void> {
  // Mirrors .storybook/preview.ts's own decorator and
  // .storybook/test-runner.ts's `setTheme` (WS16) — a direct class toggle,
  // not a `globals=` URL param, so it is independent of any Storybook
  // version's URL-parsing quirks.
  await page.evaluate((nextTheme: string) => {
    document.documentElement.classList.toggle("dark", nextTheme === "dark")
  }, theme)
}

async function setDashboardTheme(page: Page, theme: Theme): Promise<void> {
  // The real app reads `localStorage["theme"]` on boot (src/app/theme-provider.tsx)
  // and applies both the `.dark`/`.light` class and `color-scheme` itself —
  // writing the value before the app's first script runs is the same thing
  // a returning visitor's browser already has stored, no DOM hack needed.
  await page.addInitScript((nextTheme: string) => {
    try {
      window.localStorage.setItem("theme", nextTheme)
    } catch {
      // storage denied — the app falls back to `system`, still a valid render.
    }
  }, theme)
}

async function captureRender(
  page: Page,
  theme: Theme,
  outDir: string,
  targetSlug: string,
  serverUrl: string,
  options: UiProbeOptions
): Promise<RenderResult> {
  const consoleLog: ConsoleEntry[] = []
  const pageErrors: PageErrorEntry[] = []

  const onConsole = (msg: ConsoleMessage): void => {
    const location = msg.location()
    consoleLog.push({
      type: msg.type(),
      text: msg.text(),
      location: location.url ? `${location.url}:${location.lineNumber}:${location.columnNumber}` : null,
    })
  }
  const onPageError = (error: Error): void => {
    pageErrors.push({ message: error.message, stack: error.stack ?? null })
  }
  page.on("console", onConsole)
  page.on("pageerror", onPageError)

  const base = `${targetSlug}--${theme}`
  // Absolute paths for every actual filesystem/Playwright call...
  const artifacts = {
    screenshot: join(outDir, `${base}.png`),
    dom: join(outDir, `${base}.dom.html`),
    ariaSnapshot: join(outDir, `${base}.aria.yaml`),
    axeViolations: join(outDir, `${base}.axe.json`),
    console: join(outDir, `${base}.console.json`),
  }
  // ...repo-root-relative versions for the report an agent reads (see
  // `toDisplayPath`).
  const displayArtifacts = {
    screenshot: toDisplayPath(artifacts.screenshot),
    dom: toDisplayPath(artifacts.dom),
    ariaSnapshot: toDisplayPath(artifacts.ariaSnapshot),
    axeViolations: toDisplayPath(artifacts.axeViolations),
    console: toDisplayPath(artifacts.console),
  }

  let renderError: string | null = null

  try {
    if (options.mode === "story") {
      await installStoryReadySignal(page)
      const url = `${serverUrl}/iframe.html?id=${encodeURIComponent(options.target)}&viewMode=story`
      await page.goto(url, { waitUntil: "domcontentloaded", timeout: RENDER_READY_TIMEOUT_MS })
      const ready = await waitForStoryReady(page, RENDER_READY_TIMEOUT_MS)
      renderError = ready.error
      if (renderError === null) {
        await setStorybookTheme(page, theme)
      }
    } else {
      await setDashboardTheme(page, theme)
      const url = `${serverUrl}${options.target}`
      try {
        await page.goto(url, { waitUntil: "networkidle", timeout: RENDER_READY_TIMEOUT_MS })
      } catch (error) {
        renderError = error instanceof Error ? error.message : String(error)
      }
    }
  } catch (error) {
    renderError = error instanceof Error ? error.message : String(error)
  }

  // Capture whatever is on screen even after a render error — a half-broken
  // page is exactly what an agent debugging a regression wants to see.
  try {
    await page.screenshot({ path: artifacts.screenshot, fullPage: true, animations: "disabled" })
  } catch {
    // page may already be closed/crashed — leave no screenshot file.
  }

  let domHtml: string
  try {
    domHtml = await page.evaluate(() => document.body.outerHTML)
  } catch {
    domHtml = "<!-- capture failed: page unavailable -->"
  }
  writeFileSync(artifacts.dom, domHtml)

  let ariaText: string
  try {
    ariaText = await page.locator("body").ariaSnapshot()
  } catch (error) {
    ariaText = `# capture failed: ${error instanceof Error ? error.message : String(error)}`
  }
  writeFileSync(artifacts.ariaSnapshot, ariaText)

  let axeViolationCount = 0
  try {
    await injectAxe(page)
    const violations = await getViolations(page)
    axeViolationCount = violations.length
    writeFileSync(artifacts.axeViolations, `${JSON.stringify(violations, null, 2)}\n`)
  } catch (error) {
    writeFileSync(
      artifacts.axeViolations,
      `${JSON.stringify({ error: error instanceof Error ? error.message : String(error) }, null, 2)}\n`
    )
  }

  writeFileSync(
    artifacts.console,
    `${JSON.stringify({ console: consoleLog, pageErrors }, null, 2)}\n`
  )

  page.off("console", onConsole)
  page.off("pageerror", onPageError)

  const consoleErrorCount = consoleLog.filter((entry) => entry.type === "error").length
  const result: Omit<RenderResult, "status"> = {
    theme,
    error: renderError,
    consoleErrorCount,
    pageErrorCount: pageErrors.length,
    axeViolationCount,
    artifacts: displayArtifacts,
  }

  return {
    ...result,
    status: renderStatus(result, {
      failOnConsoleError: options.failOnConsoleError,
      failOnAxe: options.failOnAxe,
    }),
  }
}

let activeBrowser: Browser | null = null
let activeServer: StaticServerHandle | null = null
let watchdog: ReturnType<typeof setTimeout> | null = null

async function forceExit(reason: string): Promise<never> {
  console.error(`[ui:probe] FATAL: ${reason}`)
  try {
    await activeBrowser?.close()
  } catch {
    // best effort
  }
  try {
    await activeServer?.close()
  } catch {
    // best effort
  }
  process.exit(124)
}

async function main(): Promise<void> {
  const options = parseArgs(process.argv.slice(2))

  watchdog = setTimeout(() => {
    void forceExit(`hard timeout after ${options.timeoutMs}ms`)
  }, options.timeoutMs)

  const targetSlug = slugifyTarget(options.target)
  const outDir = options.out !== null ? resolve(ROOT, options.out) : join(DEFAULT_OUT_ROOT, targetSlug)
  ensureDir(outDir)

  if (options.mode === "story") {
    if (options.build) {
      runBuildStorybook()
    } else if (!existsSync(STORYBOOK_STATIC_DIR)) {
      throw new BuildFailedError("bun run build-storybook", -1)
    }
  } else {
    if (options.build) {
      runBuildDashboard()
    } else if (!existsSync(DASHBOARD_DIST_DIR)) {
      throw new BuildFailedError("bunx vite build --mode mock", -1)
    }
  }

  const serveRoot = options.mode === "story" ? STORYBOOK_STATIC_DIR : DASHBOARD_DIST_DIR
  console.log(`[ui:probe] serving ${serveRoot} ...`)
  const server = await bindServer(serveRoot, options.port)
  activeServer = server
  console.log(`[ui:probe] listening on ${server.url}`)

  const startedAt = new Date()

  let browser: Browser | null = null
  const renders: RenderResult[] = []

  try {
    browser = await chromium.launch({ headless: true })
    activeBrowser = browser

    for (const theme of options.themes) {
      const context = await browser.newContext({
        viewport: options.viewport,
        reducedMotion: "reduce",
      })
      const page = await context.newPage()
      try {
        const render = await captureRender(page, theme, outDir, targetSlug, server.url, options)
        renders.push(render)
        console.log(
          `[ui:probe] ${theme}: ${render.status}` +
            (render.error ? ` — ${render.error}` : ` (console errors ${render.consoleErrorCount}, axe ${render.axeViolationCount})`)
        )
      } finally {
        await context.close()
      }
    }
  } finally {
    if (browser) {
      await browser.close()
      activeBrowser = null
    }
    await server.close()
    activeServer = null
  }

  const durationMs = Date.now() - startedAt.getTime()
  const report = buildReport(renders, {
    mode: options.mode,
    target: options.target,
    viewport: options.viewport,
    startedAt: startedAt.toISOString(),
    durationMs,
  })

  writeReport(report, join(outDir, "summary.json"), join(outDir, "summary.md"))

  console.log(`[ui:probe] artifacts written to ${toDisplayPath(outDir)}/`)
  const verdict =
    report.summary.failed === 0
      ? `PASS ${report.summary.passed}/${report.summary.total}`
      : `FAIL ${report.summary.failed}/${report.summary.total} — see ${toDisplayPath(join(outDir, "summary.md"))}`
  console.log(`[ui:probe] ${verdict}`)

  if (watchdog) {
    clearTimeout(watchdog)
    watchdog = null
  }

  process.exitCode = report.summary.failed === 0 ? 0 : 1
}

main().catch(async (error: unknown) => {
  if (watchdog) {
    clearTimeout(watchdog)
    watchdog = null
  }
  if (error instanceof UiProbeArgError) {
    console.error(`[ui:probe] ${error.message}`)
    console.error("")
    console.error(usage())
    process.exitCode = 1
    return
  }
  console.error("[ui:probe] FATAL:", error instanceof Error ? error.stack ?? error.message : error)
  try {
    await activeBrowser?.close()
  } catch {
    // best effort
  }
  try {
    await activeServer?.close()
  } catch {
    // best effort
  }
  process.exitCode = 1
})
