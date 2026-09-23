// scripts/lib/static-server.ts
//
// The smallest static file server that can serve `storybook-static/` to
// `test-storybook`. No `serve`/`sirv`/`http-server` dependency — one-shot
// tooling for a repo that already keeps its script surface dependency-light
// (see `scripts/rule-audit.ts`, `scripts/visual-audit.ts`).
//
// Lives entirely in-process: `storybook-test.ts` awaits `listen()`, runs the
// test pass, then awaits `close()` — never a detached/long-lived server.

import { createReadStream, existsSync, statSync } from "node:fs"
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http"
import { extname, join, normalize, resolve } from "node:path"

const MIME_TYPES: Readonly<Record<string, string>> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".svg": "image/svg+xml",
  ".gif": "image/gif",
  ".webp": "image/webp",
  ".ico": "image/x-icon",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".ttf": "font/ttf",
  ".map": "application/json; charset=utf-8",
  ".txt": "text/plain; charset=utf-8",
}

export class StaticServerBindError extends Error {
  override readonly name = "StaticServerBindError"
  readonly port: number

  constructor(port: number, cause: unknown) {
    super(`static server could not bind to port ${port}: ${String(cause)}`)
    this.port = port
  }
}

export interface StaticServerHandle {
  readonly port: number
  readonly url: string
  close(): Promise<void>
}

function contentTypeFor(filePath: string): string {
  return MIME_TYPES[extname(filePath).toLowerCase()] ?? "application/octet-stream"
}

function respondNotFound(response: ServerResponse): void {
  response.writeHead(404, { "content-type": "text/plain; charset=utf-8" })
  response.end("not found")
}

function respondForbidden(response: ServerResponse): void {
  response.writeHead(403, { "content-type": "text/plain; charset=utf-8" })
  response.end("forbidden")
}

function serveFile(root: string, request: IncomingMessage, response: ServerResponse): void {
  const requestUrl = request.url ?? "/"
  const pathname = decodeURIComponent(requestUrl.split("?")[0] ?? "/")
  const withoutTraversal = normalize(pathname).replace(/^([.][.][/\\])+/, "")
  const candidate = resolve(root, `.${withoutTraversal}`)

  if (!candidate.startsWith(root)) {
    respondForbidden(response)
    return
  }

  const filePath = existsSync(candidate) && !statSync(candidate).isDirectory()
    ? candidate
    : join(root, "index.html")

  if (!existsSync(filePath)) {
    respondNotFound(response)
    return
  }

  response.writeHead(200, { "content-type": contentTypeFor(filePath) })
  createReadStream(filePath).pipe(response)
}

function bind(server: Server, port: number): Promise<void> {
  return new Promise((done, fail) => {
    const onError = (error: unknown): void => {
      fail(new StaticServerBindError(port, error))
    }
    server.once("error", onError)
    server.listen(port, "127.0.0.1", () => {
      server.removeListener("error", onError)
      done()
    })
  })
}

function unbind(server: Server): Promise<void> {
  return new Promise((done, fail) => {
    server.close((error) => (error ? fail(error) : done()))
  })
}

export async function startStaticServer(root: string, port: number): Promise<StaticServerHandle> {
  const resolvedRoot = resolve(root)
  const server = createServer((request, response) => {
    serveFile(resolvedRoot, request, response)
  })

  await bind(server, port)

  return {
    port,
    url: `http://127.0.0.1:${port}`,
    close: () => unbind(server),
  }
}
