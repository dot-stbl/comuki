// scripts/lib/ui-probe-args.ts
//
// Pure argument parsing for `bun run ui:probe` (WS17,
// openspec/changes/add-agentic-test-contour, branch
// feat/agentic-test-contour-spec — see tasks.md WS17 and design.md "Local
// dev commands"). No I/O here — `ui-probe.ts` is the only thing allowed to
// touch the filesystem, a browser or a socket; this module is exhaustively
// unit-testable without any of that.
//
// Accepts both `--flag value` and `--flag=value` so it reads naturally
// whether a human or an agent types it (design.md's own example uses
// `=`, the WS17 brief that commissioned this script uses a space).

export type Theme = "light" | "dark"
export type ProbeMode = "story" | "page"

export interface Viewport {
  readonly width: number
  readonly height: number
}

export interface UiProbeOptions {
  readonly mode: ProbeMode
  readonly target: string // storyId in "story" mode, route path in "page" mode
  readonly themes: readonly Theme[]
  readonly viewport: Viewport
  readonly build: boolean
  readonly out: string | null
  readonly port: number | null
  readonly timeoutMs: number
  readonly failOnConsoleError: boolean
  readonly failOnAxe: boolean
}

export const PORT_POOL_MIN = 17180
export const PORT_POOL_MAX = 17200
/** `test:storybook`'s default scratch port — WS17's own pool excludes it. */
export const RESERVED_PORT = 17186

export const DEFAULT_VIEWPORT: Viewport = { width: 1440, height: 900 }
export const DEFAULT_THEME: Theme = "dark"
// Covers the whole process, including an unskipped build — build-storybook
// alone can take well over a minute; this is the outer watchdog `ui-probe.ts`
// arms before doing anything else, not just the render step.
export const DEFAULT_TIMEOUT_MS = 180_000

export class UiProbeArgError extends Error {
  override readonly name = "UiProbeArgError"
}

export class PortRangeError extends Error {
  override readonly name = "PortRangeError"
  readonly port: number

  constructor(port: number) {
    super(
      `port ${port} is outside the ui:probe pool ${PORT_POOL_MIN}-${PORT_POOL_MAX} (.agents/rules/process/ports.md)`
    )
    this.port = port
  }
}

export class PortReservedError extends Error {
  override readonly name = "PortReservedError"
  readonly port: number

  constructor(port: number) {
    super(`port ${port} is reserved for test:storybook — pick another port in ${PORT_POOL_MIN}-${PORT_POOL_MAX}`)
    this.port = port
  }
}

function isFlag(arg: string, name: string): boolean {
  return arg === name || arg.startsWith(`${name}=`)
}

/**
 * Reads the value for `--name` from `argv[i]`, accepting `--name=value` (the
 * value is on the same token) or `--name value` (the value is the next
 * token). Returns `[value, nextIndex]` so the caller's loop can skip ahead.
 */
function readValue(argv: readonly string[], i: number, name: string): [string, number] {
  const current = argv[i]
  if (current === undefined) {
    throw new UiProbeArgError(`${name} requires a value`)
  }
  const eq = current.indexOf("=")
  if (eq !== -1) {
    return [current.slice(eq + 1), i + 1]
  }
  const next = argv[i + 1]
  if (next === undefined || next.startsWith("--")) {
    throw new UiProbeArgError(`${name} requires a value`)
  }
  return [next, i + 2]
}

export function parseTheme(raw: string): readonly Theme[] {
  switch (raw) {
    case "light":
      return ["light"]
    case "dark":
      return ["dark"]
    case "both":
      return ["light", "dark"]
    default:
      throw new UiProbeArgError(`--theme must be one of light|dark|both, got "${raw}"`)
  }
}

const VIEWPORT_PATTERN = /^(\d+)x(\d+)$/i

export function parseViewport(raw: string): Viewport {
  const match = VIEWPORT_PATTERN.exec(raw.trim())
  if (!match) {
    throw new UiProbeArgError(`--viewport must look like "1440x900", got "${raw}"`)
  }
  const width = Number(match[1])
  const height = Number(match[2])
  if (width <= 0 || height <= 0) {
    throw new UiProbeArgError(`--viewport dimensions must be positive, got "${raw}"`)
  }
  return { width, height }
}

function parsePort(raw: string): number {
  const port = Number(raw)
  if (!Number.isInteger(port)) {
    throw new UiProbeArgError(`--port must be an integer, got "${raw}"`)
  }
  return port
}

export function assertPoolPort(port: number): void {
  if (port < PORT_POOL_MIN || port > PORT_POOL_MAX) {
    throw new PortRangeError(port)
  }
  if (port === RESERVED_PORT) {
    throw new PortReservedError(port)
  }
}

function parseTimeout(raw: string): number {
  const ms = Number(raw)
  if (!Number.isFinite(ms) || ms <= 0) {
    throw new UiProbeArgError(`--timeout must be a positive number of milliseconds, got "${raw}"`)
  }
  return ms
}

/**
 * `--story <id>` and `--page <route>` are mutually exclusive — exactly one
 * names the render target. Every other flag has a default so `ui:probe
 * --story <id>` alone is a complete, valid invocation.
 */
export function parseArgs(argv: readonly string[]): UiProbeOptions {
  let story: string | null = null
  let page: string | null = null
  let themes: readonly Theme[] = [DEFAULT_THEME]
  let viewport: Viewport = DEFAULT_VIEWPORT
  let build = true
  let out: string | null = null
  let port: number | null = null
  let timeoutMs = DEFAULT_TIMEOUT_MS
  let failOnConsoleError = false
  let failOnAxe = false

  let i = 0
  while (i < argv.length) {
    const arg = argv[i]
    if (arg === undefined) {
      break
    }

    if (isFlag(arg, "--story")) {
      const [value, next] = readValue(argv, i, "--story")
      story = value
      i = next
      continue
    }
    if (isFlag(arg, "--page")) {
      const [value, next] = readValue(argv, i, "--page")
      page = value
      i = next
      continue
    }
    if (isFlag(arg, "--theme")) {
      const [value, next] = readValue(argv, i, "--theme")
      themes = parseTheme(value)
      i = next
      continue
    }
    if (isFlag(arg, "--viewport")) {
      const [value, next] = readValue(argv, i, "--viewport")
      viewport = parseViewport(value)
      i = next
      continue
    }
    if (isFlag(arg, "--out")) {
      const [value, next] = readValue(argv, i, "--out")
      out = value
      i = next
      continue
    }
    if (isFlag(arg, "--port")) {
      const [value, next] = readValue(argv, i, "--port")
      port = parsePort(value)
      i = next
      continue
    }
    if (isFlag(arg, "--timeout")) {
      const [value, next] = readValue(argv, i, "--timeout")
      timeoutMs = parseTimeout(value)
      i = next
      continue
    }
    if (arg === "--build") {
      build = true
      i += 1
      continue
    }
    if (arg === "--skip-build") {
      build = false
      i += 1
      continue
    }
    if (arg === "--fail-on-console-error") {
      failOnConsoleError = true
      i += 1
      continue
    }
    if (arg === "--fail-on-axe") {
      failOnAxe = true
      i += 1
      continue
    }

    throw new UiProbeArgError(`unrecognised argument "${arg}"`)
  }

  if (story === null && page === null) {
    throw new UiProbeArgError("provide either --story <storyId> or --page <route>")
  }
  if (story !== null && page !== null) {
    throw new UiProbeArgError("--story and --page are mutually exclusive")
  }

  if (port !== null) {
    assertPoolPort(port)
  }

  const mode: ProbeMode = story !== null ? "story" : "page"
  const target = (story ?? page) as string

  return {
    mode,
    target,
    themes,
    viewport,
    build,
    out,
    port,
    timeoutMs,
    failOnConsoleError,
    failOnAxe,
  }
}

/** Filesystem/URL-safe slug for a story id or a route path. */
export function slugifyTarget(target: string): string {
  const slug = target.replace(/[^a-z0-9-]+/gi, "_").replace(/^_+|_+$/g, "")
  return slug.length > 0 ? slug : "root"
}
