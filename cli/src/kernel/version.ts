/**
 * ClientVersion / ServerVersion handshake (issue #81).
 *
 * On `kernel.start()` the client calls `GET /api/v1/version` and
 * compares the result with its own build version:
 *
 *   - Major match + minor >= client → log `ok`, proceed.
 *   - Minor is behind the client → log `warn`, let the user proceed.
 *     Older servers may be missing newer features; the client must
 *     still work.
 *   - Major mismatch → refuse to start. Surface the typed error
 *     `VersionMismatchError` so the host can present the
 *     `chrome.versionMismatch` hint with a one-liner upgrade link.
 *
 * The server already exposes `GET /api/v1/version`. The response
 * shape:
 *
 *   { "version": "1.3.0", "name": "comuki" }
 *
 * — `version` is semver (`<major>.<minor>.<patch>`); `name` is
 * informational and may be absent.
 *
 * Renderer-agnostic — no Ink, no React, no ANSI. The kernel exposes
 * `clientVersion()` and the result is `log()`-ed through the
 * telemetry sink.
 */

import { defaultStateDirectory } from "./receipts"

// ---------------------------------------------------------------------------
// Version parse + compare
// ---------------------------------------------------------------------------

export interface SemVer {
  readonly major: number
  readonly minor: number
  readonly patch: number
}

/** Parse `1.3.0` into `{ major: 1, minor: 3, patch: 0 }`. Returns `null` on bad input. */
export function parseSemVer(value: string): SemVer | null {
  const parts = value.split(".")
  if (parts.length !== 3) {
    return null
  }
  const [a, b, c] = parts
  if (a === undefined || b === undefined || c === undefined) {
    return null
  }
  const major = Number.parseInt(a, 10)
  const minor = Number.parseInt(b, 10)
  const patch = Number.parseInt(c, 10)
  if (
    !Number.isInteger(major) ||
    !Number.isInteger(minor) ||
    !Number.isInteger(patch) ||
    major < 0 ||
    minor < 0 ||
    patch < 0 ||
    String(major) !== a ||
    String(minor) !== b ||
    String(patch) !== c
  ) {
    return null
  }
  return { major, minor, patch }
}

export type VersionComparison =
  | { readonly kind: "ok" }
  | { readonly kind: "warn"; readonly reason: "server-minor-behind" | "server-major-ahead" }
  | { readonly kind: "refused"; readonly reason: "server-major-behind" | "invalid-version" }

/**
 * Compare `server` against `client`. The decision matrix:
 *
 *   server.major  >  client.major  → warn (server is ahead)
 *   server.major  <  client.major  → refuse (server too old)
 *   server.major == client.major:
 *     server.minor >= client.minor → ok
 *     server.minor <  client.minor → warn (server is behind, client must work anyway)
 */
export function compareVersions(client: SemVer, server: SemVer): VersionComparison {
  if (server.major > client.major) {
    return { kind: "warn", reason: "server-major-ahead" }
  }
  if (server.major < client.major) {
    return { kind: "refused", reason: "server-major-behind" }
  }
  if (server.minor < client.minor) {
    return { kind: "warn", reason: "server-minor-behind" }
  }
  return { kind: "ok" }
}

// ---------------------------------------------------------------------------
// Server version fetch — fetch surface is injectable so tests fake it
// ---------------------------------------------------------------------------

export interface ServerVersion {
  readonly version: string
  readonly name?: string
}

/** `fetchImpl` shape — the bare fetch surface (response only). */
export type VersionFetch = (url: string) => Promise<Response>

export const VERSION_PATH = "/api/v1/version"

/**
 * Fetch + decode `GET /api/v1/version`. Throws `VersionFetchError` on
 * network / decode failure; throws `VersionMismatchError` is NOT
 * raised here — that's the comparison's job.
 */
export async function fetchServerVersion(
  baseUrl: string,
  fetchImpl: VersionFetch,
  _options: { readonly signal?: AbortSignal } = {}
): Promise<ServerVersion> {
  const url = `${baseUrl.replace(/\/+$/, "")}${VERSION_PATH}`
  let response: Response
  try {
    response = await fetchImpl(url)
  } catch (error: unknown) {
    throw new VersionFetchError(
      "network",
      `GET ${url} failed: ${error instanceof Error ? error.message : String(error)}`
    )
  }
  if (!response.ok) {
    throw new VersionFetchError(
      "http",
      `GET ${url} returned ${response.status} ${response.statusText}`
    )
  }
  let raw: unknown
  try {
    raw = await response.json()
  } catch (error: unknown) {
    throw new VersionFetchError(
      "decode",
      `GET ${url} returned non-JSON body: ${
        error instanceof Error ? error.message : String(error)
      }`
    )
  }
  if (
    raw === null ||
    typeof raw !== "object" ||
    Array.isArray(raw) ||
    typeof (raw as Record<string, unknown>)["version"] !== "string"
  ) {
    throw new VersionFetchError(
      "decode",
      `GET ${url} returned a body without a string 'version' field`
    )
  }
  const record = raw as Record<string, unknown>
  const version = record["version"]
  const name = record["name"]
  const result: ServerVersion = {
    version: typeof version === "string" ? version : "",
  }
  if (typeof name === "string" && name.length > 0) {
    ;(result as { name?: string }).name = name
  }
  return result
}

// ---------------------------------------------------------------------------
// Typed errors
// ---------------------------------------------------------------------------

export class VersionFetchError extends Error {
  override readonly name = "VersionFetchError"
  readonly code: "network" | "http" | "decode"
  constructor(code: "network" | "http" | "decode", message: string) {
    super(message)
    this.code = code
  }
}

export class VersionMismatchError extends Error {
  override readonly name = "VersionMismatchError"
  readonly clientVersion: string
  readonly serverVersion: string
  readonly reason: "server-major-behind" | "invalid-version"
  constructor(clientVersion: string, serverVersion: string, reason: "server-major-behind" | "invalid-version") {
    super(
      `Client ${clientVersion} is incompatible with server ${serverVersion} (${reason})`
    )
    this.clientVersion = clientVersion
    this.serverVersion = serverVersion
    this.reason = reason
  }
}

// ---------------------------------------------------------------------------
// ClientVersion — the value the kernel stamps on boot
// ---------------------------------------------------------------------------

/**
 * The client's own build version — bumped on every release of the CLI.
 *
 * This is the source of truth for the kernel's version handshake;
 * `components/StatusLine.tsx` re-exports the same string for the
 * one-shot header. Update this on every release.
 */
export const CLIENT_VERSION_STRING: string = "0.2.0"

/** Parsed form of the client version. */
export const CLIENT_SEMVER: SemVer = parseSemVer(CLIENT_VERSION_STRING) ?? {
  major: 0,
  minor: 0,
  patch: 0,
}

// ---------------------------------------------------------------------------
// resolveVersionHandshake — the single entry point the kernel calls
// ---------------------------------------------------------------------------

export interface VersionHandshake {
  readonly status: "ok" | "warn" | "refused"
  readonly client: string
  readonly server: string
  readonly reason?: string
}

/**
 * Perform the handshake against `baseUrl`. Returns the structured
 * result; throws only on `refused` (typed) so the caller can
 * propagate the error to the user.
 *
 * - `ok` and `warn` resolve normally; the caller logs the result.
 * - `refused` raises `VersionMismatchError` so the user sees a
 *   typed failure (the host maps it to `chrome.versionMismatch`).
 * - Network / decode failures raise `VersionFetchError`. The brief
 *   is silent on these — production treats them as a soft failure
 *   (proceed with `warn`) so a misconfigured host does not block
 *   the user's first launch. The caller can decide per-deploy.
 */
export async function resolveVersionHandshake(
  baseUrl: string,
  fetchImpl: VersionFetch,
  options: { readonly signal?: AbortSignal; readonly clientVersion?: string } = {}
): Promise<VersionHandshake> {
  const clientString = options.clientVersion ?? CLIENT_VERSION_STRING
  const clientParsed = parseSemVer(clientString) ?? CLIENT_SEMVER
  let server: ServerVersion
  try {
    server = await fetchServerVersion(baseUrl, fetchImpl, options)
  } catch (error: unknown) {
    if (error instanceof VersionFetchError) {
      return {
        status: "warn",
        client: clientString,
        server: "unknown",
        reason: `${error.code}: ${error.message}`,
      }
    }
    throw error
  }
  const serverParsed = parseSemVer(server.version)
  if (serverParsed === null) {
    throw new VersionMismatchError(clientString, server.version, "invalid-version")
  }
  const verdict = compareVersions(clientParsed, serverParsed)
  if (verdict.kind === "refused") {
    throw new VersionMismatchError(clientString, server.version, verdict.reason)
  }
  return {
    status: verdict.kind,
    client: clientString,
    server: server.version,
    ...(verdict.kind === "warn" ? { reason: verdict.reason } : {}),
  }
}

// Re-export the default state directory resolver so consumers that
// only need the version module do not have to import receipts.
export { defaultStateDirectory }
