// storybook-tests/harness-io.ts
//
// Vite dev-server middleware for the `stories` vitest project
// (storybook-tests/harness.spec.ts): the browser-side spec needs PNG bytes
// that live on disk — CI-generated visual baselines to compare against, and
// diff images to write on failure or in --update-snapshots mode — but a
// browser-mode test cannot touch `node:fs`. This plugin gives the spec a
// narrow, same-origin file channel on the very vite server that already
// serves the test iframe:
//
//   GET  /__sb-harness/baseline/<slug>--<theme>.png   → 200 PNG bytes | 404
//   PUT  /__sb-harness/baseline/<slug>--<theme>.png   → write (update mode only, 403 otherwise)
//   PUT  /__sb-harness/diff/<slug>--<theme>.png       → write (always)
//
// Registered only in the `stories` project of vitest.config.ts, so the
// plain `bun run dev` server never carries it. Baselines stay under
// `storybook-tests/visual-baselines/` (gitignored, CI-generated — the
// no-local-baselines policy from storybook-tests/README.md), diffs under
// `storybook-tests/diffs/`. Every request name must match the strict
// `[a-z0-9_-]+\.png` pattern the spec's `slugify` already produces — it
// rejects separators, `..` and drive letters — and the resolved path is
// re-checked to sit inside its root, so a request can never escape.

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs"
import { dirname, resolve, sep } from "node:path"
import type { Plugin } from "vite"

const BASELINE_DIR = resolve(process.cwd(), "storybook-tests/visual-baselines")
const DIFF_DIR = resolve(process.cwd(), "storybook-tests/diffs")

/** The only shape a requested file name may take — `slugify()`'s output
 *  plus `--dark`/`--light` and an optional `.diff` infix. */
const SAFE_NAME = /^[a-z0-9_-]+\.png$/

function isUpdateMode(): boolean {
  return process.env.STORYBOOK_TEST_UPDATE_SNAPSHOTS === "1"
}

/** Resolves `<dir>/<name>` after pattern validation, or null when the name
 *  is not a plain slug (SAFE_NAME already excludes separators, `..` and
 *  drive letters; the prefix check is defence in depth). */
function safeResolve(dir: string, name: string): string | null {
  if (!SAFE_NAME.test(name)) {
    return null
  }
  const resolved = resolve(dir, name)
  return resolved.startsWith(dir + sep) ? resolved : null
}

interface HarnessResponse {
  statusCode: number
  setHeader(key: string, value: string): void
  end(body?: Buffer | string): void
}

function sendPng(res: HarnessResponse, bytes: Buffer): void {
  res.statusCode = 200
  res.setHeader("Content-Type", "image/png")
  res.setHeader("Content-Length", String(bytes.length))
  res.end(bytes)
}

function sendStatus(res: HarnessResponse, code: number, text: string): void {
  res.statusCode = code
  res.end(text)
}

interface MiddlewareRequest {
  method?: string
  url?: string
  on(event: "data", listener: (chunk: Buffer) => void): void
  on(event: "end", listener: () => void): void
}

function readBody(request: MiddlewareRequest): Promise<Buffer> {
  return new Promise((resolveBody) => {
    const chunks: Buffer[] = []
    request.on("data", (chunk) => {
      chunks.push(chunk)
    })
    request.on("end", () => {
      resolveBody(Buffer.concat(chunks))
    })
  })
}

async function handle(request: MiddlewareRequest, res: HarnessResponse): Promise<void> {
  const url = new URL(request.url ?? "/", "http://localhost")
  const segments = url.pathname.split("/").filter((segment) => segment.length > 0)
  const kind = segments[0]
  const name = segments[1]

  if (name === undefined) {
    sendStatus(res, 404, "expected /__sb-harness/{baseline|diff}/<name>.png")
    return
  }

  if (kind === "baseline" && request.method === "GET") {
    const path = safeResolve(BASELINE_DIR, name)
    if (path === null || !existsSync(path)) {
      sendStatus(res, 404, `no baseline at storybook-tests/visual-baselines/${name}`)
      return
    }
    sendPng(res, readFileSync(path))
    return
  }

  if (kind === "baseline" && (request.method === "PUT" || request.method === "POST")) {
    if (!isUpdateMode()) {
      sendStatus(res, 403, "baseline writes require STORYBOOK_TEST_UPDATE_SNAPSHOTS=1")
      return
    }
    const path = safeResolve(BASELINE_DIR, name)
    if (path === null) {
      sendStatus(res, 400, "bad name")
      return
    }
    mkdirSync(dirname(path), { recursive: true })
    writeFileSync(path, await readBody(request))
    sendStatus(res, 200, "written")
    return
  }

  if (kind === "diff" && (request.method === "PUT" || request.method === "POST")) {
    const path = safeResolve(DIFF_DIR, name)
    if (path === null) {
      sendStatus(res, 400, "bad name")
      return
    }
    mkdirSync(dirname(path), { recursive: true })
    writeFileSync(path, await readBody(request))
    sendStatus(res, 200, "written")
    return
  }

  sendStatus(res, 404, "unknown harness route")
}

/** Vite plugin — see the module docblock. */
export function storybookHarnessIo(): Plugin {
  return {
    name: "comuki-storybook-harness-io",
    configureServer(server) {
      // Every request under this prefix belongs to the harness — handle it
      // and end the response; never fall through to the static/vite stack.
      server.middlewares.use("/__sb-harness", (req, res) => {
        const request = req as MiddlewareRequest
        void handle(request, res as HarnessResponse).catch(() => {
          sendStatus(res as HarnessResponse, 500, "harness io error")
        })
      })
    },
  }
}
