/**
 * Version handshake matrix (issue #81) — three matrix cases the
 * kernel relies on:
 *
 *   1. ok         — server major matches, minor ≥ client
 *   2. warn       — server major matches but minor is behind, OR
 *                   server major is ahead (client runs on newer server).
 *                   Either way, proceed.
 *   3. refused    — server major is behind the client; raise
 *                   `VersionMismatchError` so the host surfaces
 *                   `chrome.versionMismatch`.
 *
 * The fetch surface is injectable so the test fakes `GET /api/v1/version`
 * per case; the parsing + comparison logic stays real.
 */

import { describe, expect, test } from "bun:test"

import {
  CLIENT_SEMVER,
  CLIENT_VERSION_STRING,
  compareVersions,
  fetchServerVersion,
  parseSemVer,
  resolveVersionHandshake,
  VersionFetchError,
  VersionMismatchError,
  VERSION_PATH,
  type SemVer,
  type VersionFetch,
} from "./version"

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  })
}

function makeFetcher(status: number, body: unknown): VersionFetch {
  return async (_url: string): Promise<Response> => jsonResponse(body, status)
}

describe("parseSemVer", () => {
  test("parses canonical semver", () => {
    expect(parseSemVer("1.3.0")).toEqual({ major: 1, minor: 3, patch: 0 })
    expect(parseSemVer("0.2.0")).toEqual({ major: 0, minor: 2, patch: 0 })
  })

  test("rejects malformed input", () => {
    expect(parseSemVer("")).toBeNull()
    expect(parseSemVer("1")).toBeNull()
    expect(parseSemVer("1.3")).toBeNull()
    expect(parseSemVer("1.3.0.4")).toBeNull()
    expect(parseSemVer("v1.3.0")).toBeNull()
    expect(parseSemVer("1.x.0")).toBeNull()
    expect(parseSemVer("-1.3.0")).toBeNull()
    expect(parseSemVer("1.-3.0")).toBeNull()
    expect(parseSemVer("1.3.-0")).toBeNull()
  })

  test("CLIENT_VERSION_STRING parses", () => {
    expect(CLIENT_SEMVER.major).toBeGreaterThanOrEqual(0)
    expect(CLIENT_VERSION_STRING).toMatch(/^\d+\.\d+\.\d+$/)
  })
})

describe("compareVersions — matrix cases", () => {
  const v = (major: number, minor: number, patch: number): SemVer => ({ major, minor, patch })

  test("ok: server.major == client.major, server.minor >= client.minor", () => {
    expect(compareVersions(v(1, 3, 0), v(1, 3, 0))).toEqual({ kind: "ok" })
    expect(compareVersions(v(1, 3, 0), v(1, 4, 0))).toEqual({ kind: "ok" })
    expect(compareVersions(v(1, 3, 0), v(1, 3, 5))).toEqual({ kind: "ok" })
    expect(compareVersions(v(0, 2, 0), v(0, 2, 0))).toEqual({ kind: "ok" })
  })

  test("warn: server minor is behind the client (server too old by minor)", () => {
    const result = compareVersions(v(1, 3, 0), v(1, 2, 0))
    expect(result.kind).toBe("warn")
    if (result.kind === "warn") {
      expect(result.reason).toBe("server-minor-behind")
    }
  })

  test("warn: server major is ahead of the client", () => {
    const result = compareVersions(v(1, 3, 0), v(2, 0, 0))
    expect(result.kind).toBe("warn")
    if (result.kind === "warn") {
      expect(result.reason).toBe("server-major-ahead")
    }
  })

  test("refused: server major is behind the client", () => {
    const result = compareVersions(v(1, 3, 0), v(0, 9, 0))
    expect(result.kind).toBe("refused")
    if (result.kind === "refused") {
      expect(result.reason).toBe("server-major-behind")
    }
  })
})

describe("fetchServerVersion", () => {
  test("decodes a well-formed response", async () => {
    const fetchImpl = makeFetcher(200, { version: "1.3.0", name: "comuki" })
    const result = await fetchServerVersion("https://example.com/", fetchImpl)
    expect(result.version).toBe("1.3.0")
    expect(result.name).toBe("comuki")
  })

  test("calls GET /api/v1/version on the configured base URL", async () => {
    const seen: string[] = []
    const fetchImpl: VersionFetch = async (url: string) => {
      seen.push(url)
      return jsonResponse({ version: "1.0.0" })
    }
    await fetchServerVersion("https://example.com", fetchImpl)
    expect(seen[0]).toBe("https://example.com/api/v1/version")
  })

  test("VERSION_PATH is the well-known constant", () => {
    expect(VERSION_PATH).toBe("/api/v1/version")
  })

  test("non-2xx response raises VersionFetchError(http)", async () => {
    const fetchImpl = makeFetcher(503, { error: "down" })
    await expect(
      fetchServerVersion("https://example.com", fetchImpl)
    ).rejects.toBeInstanceOf(VersionFetchError)
  })

  test("decode failure raises VersionFetchError(decode)", async () => {
    const fetchImpl: VersionFetch = async () =>
      new Response("not json", {
        status: 200,
        headers: { "content-type": "application/json" },
      })
    await expect(
      fetchServerVersion("https://example.com", fetchImpl)
    ).rejects.toBeInstanceOf(VersionFetchError)
  })

  test("missing version field raises VersionFetchError(decode)", async () => {
    const fetchImpl = makeFetcher(200, { name: "comuki" })
    await expect(
      fetchServerVersion("https://example.com", fetchImpl)
    ).rejects.toBeInstanceOf(VersionFetchError)
  })
})

describe("resolveVersionHandshake — three matrix cases", () => {
  test("ok: server major matches and minor >= client", async () => {
    const fetchImpl = makeFetcher(200, { version: CLIENT_VERSION_STRING })
    const result = await resolveVersionHandshake("https://example.com", fetchImpl)
    expect(result.status).toBe("ok")
    expect(result.client).toBe(CLIENT_VERSION_STRING)
    expect(result.server).toBe(CLIENT_VERSION_STRING)
    expect(result.reason).toBeUndefined()
  })

  test("warn: server minor is behind the client (proceed)", async () => {
    // Force a test scenario by passing a custom client version that's
    // a step ahead of the server.
    const fetchImpl = makeFetcher(200, { version: "1.2.0" })
    const result = await resolveVersionHandshake("https://example.com", fetchImpl, {
      clientVersion: "1.3.0",
    })
    expect(result.status).toBe("warn")
    expect(result.client).toBe("1.3.0")
    expect(result.server).toBe("1.2.0")
    expect(result.reason).toBe("server-minor-behind")
  })

  test("refused: server major is behind the client (raise VersionMismatchError)", async () => {
    const fetchImpl = makeFetcher(200, { version: "1.2.0" })
    await expect(
      resolveVersionHandshake("https://example.com", fetchImpl, {
        clientVersion: "2.0.0",
      })
    ).rejects.toBeInstanceOf(VersionMismatchError)
  })

  test("refused: invalid server version raises VersionMismatchError", async () => {
    const fetchImpl = makeFetcher(200, { version: "not-semver" })
    await expect(
      resolveVersionHandshake("https://example.com", fetchImpl)
    ).rejects.toBeInstanceOf(VersionMismatchError)
  })

  test("network failure degrades to warn (the brief: do not block first launch)", async () => {
    const fetchImpl: VersionFetch = async () => {
      throw new Error("ECONNREFUSED")
    }
    const result = await resolveVersionHandshake("https://example.com", fetchImpl)
    expect(result.status).toBe("warn")
    expect(result.server).toBe("unknown")
    expect(result.reason).toContain("network")
  })
})
