/**
 * Machine-mode mapping (issue #80) — five classification rules for
 * errors, and a 1:1 map for the four kernel events that have a stable
 * machine representation. The classifier exits to auth.* (exit 2) vs
 * runtime.* (exit 1) via `machineExitCode`; the event mapper returns
 * `null` for anything outside the supported set so the caller (an
 * Ndjson adapter) drops it.
 */
import { describe, expect, it } from "bun:test"
import { sessionId, turnRequestId, type CliError } from "../harness/state"
import { ComukiApiError } from "../lib/client"
import { machineExitCode } from "./envelopes"
import type { MachineEvent } from "./envelopes"
import { harnessEventToMachineEvent, machineErrorFrom } from "./mapping"
import type { HarnessMessage } from "../harness/state"

const sessionA = sessionId("session-a")
const requestA = turnRequestId("request-a")

function cliError(input: {
  readonly kind: CliError["kind"]
  readonly code: string
  readonly message: string
  readonly retryable?: boolean
}): CliError {
  return {
    kind: input.kind,
    code: input.code,
    message: input.message,
    retryable: input.retryable ?? false,
  }
}

function harnessMessage(id: string): HarnessMessage {
  return {
    id,
    role: "assistant",
    content: "ok",
    createdAtUnixMs: 1_700_000_000_000,
  }
}

describe("machineErrorFrom — CliError-shaped input (rule 1)", () => {
  it("classifies kind=auth with a code as auth.<code>", () => {
    const result = machineErrorFrom(
      cliError({ kind: "auth", code: "missing", message: "no key" })
    )
    expect(result.code).toBe("auth.missing")
    expect(result.message).toBe("no key")
    expect(machineExitCode(result)).toBe(2)
  })

  it("falls back to auth.denied when kind=auth and code is empty", () => {
    const result = machineErrorFrom(
      cliError({ kind: "auth", code: "", message: "denied" })
    )
    expect(result.code).toBe("auth.denied")
    expect(machineExitCode(result)).toBe(2)
  })

  it("classifies every other kind as runtime.<kind>", () => {
    for (const kind of ["aborted", "network", "server", "storage", "unknown"] as const) {
      const result = machineErrorFrom(
        cliError({ kind, code: "x", message: `${kind} happened` })
      )
      expect(result.code).toBe(`runtime.${kind}`)
      expect(result.message).toBe(`${kind} happened`)
      expect(machineExitCode(result)).toBe(1)
    }
  })

  it("ignores objects that look almost-but-not-quite like CliError", () => {
    // Missing `retryable` boolean — not CliError-shaped.
    const missingRetryable = {
      kind: "auth",
      code: "missing",
      message: "x",
    }
    expect(machineErrorFrom(missingRetryable).code).toBe("runtime.unknown")

    // Unknown kind value — not in the allow-list.
    const unknownKind = cliError({
      kind: "weird" as CliError["kind"],
      code: "x",
      message: "x",
    })
    expect(machineErrorFrom(unknownKind).code).toBe("runtime.unknown")
  })
})

describe("machineErrorFrom — ComukiApiError (rule 2)", () => {
  it("classifies 401 as auth.denied (exit 2)", () => {
    const result = machineErrorFrom(
      new ComukiApiError(401, undefined, "no session")
    )
    expect(result.code).toBe("auth.denied")
    expect(result.message).toBe("HTTP 401: no session")
    expect(machineExitCode(result)).toBe(2)
  })

  it("classifies 403 as auth.denied (exit 2)", () => {
    const result = machineErrorFrom(
      new ComukiApiError(403, "knowledge:write", "missing scope")
    )
    expect(result.code).toBe("auth.denied")
    expect(result.message).toBe("HTTP 403: missing scope")
    expect(machineExitCode(result)).toBe(2)
  })

  it("classifies other HTTP statuses as runtime.upstream (exit 1)", () => {
    const result = machineErrorFrom(
      new ComukiApiError(502, "upstream.timeout", "bad gateway")
    )
    expect(result.code).toBe("runtime.upstream")
    expect(result.message).toBe("HTTP 502 (upstream.timeout): bad gateway")
    expect(machineExitCode(result)).toBe(1)
  })

  it("falls back to a generic request-failed message when detail is missing", () => {
    const result = machineErrorFrom(new ComukiApiError(500, undefined, undefined))
    expect(result.code).toBe("runtime.upstream")
    expect(result.message).toBe("HTTP 500: request failed")
  })

  it("omits the code parenthetical when no wire code is present", () => {
    const result = machineErrorFrom(
      new ComukiApiError(500, undefined, "boom")
    )
    expect(result.message).toBe("HTTP 500: boom")
  })
})

describe("machineErrorFrom — AbortError (rule 3)", () => {
  it("classifies an AbortError-named Error as runtime.timeout (exit 1)", () => {
    const err = new Error("aborted")
    err.name = "AbortError"
    const result = machineErrorFrom(err)
    expect(result.code).toBe("runtime.timeout")
    expect(result.message).toBe("operation timed out")
    expect(machineExitCode(result)).toBe(1)
  })
})

describe("machineErrorFrom — transport error pattern (rule 4)", () => {
  it("matches 'unable to connect'", () => {
    expect(machineErrorFrom(new Error("unable to connect to host")).code).toBe(
      "runtime.network"
    )
  })

  it("matches 'fetch failed' (Node 18+ undici message)", () => {
    expect(machineErrorFrom(new Error("fetch failed")).code).toBe(
      "runtime.network"
    )
  })

  it("matches 'ECONNREFUSED'", () => {
    expect(machineErrorFrom(new Error("connect ECONNREFUSED 127.0.0.1:5000")).code).toBe(
      "runtime.network"
    )
  })

  it("matches 'connection refused' (case-insensitive)", () => {
    expect(machineErrorFrom(new Error("Connection refused by peer")).code).toBe(
      "runtime.network"
    )
  })

  it("matches are case-insensitive", () => {
    expect(machineErrorFrom(new Error("UNABLE TO CONNECT")).code).toBe(
      "runtime.network"
    )
  })
})

describe("machineErrorFrom — fallback (rule 5)", () => {
  it("uses Error.message for unknown Error values", () => {
    const result = machineErrorFrom(new Error("totally weird"))
    expect(result.code).toBe("runtime.unknown")
    expect(result.message).toBe("totally weird")
    expect(machineExitCode(result)).toBe(1)
  })

  it("stringifies non-Error throws", () => {
    expect(machineErrorFrom("oops").message).toBe("oops")
    expect(machineErrorFrom(42).message).toBe("42")
    expect(machineErrorFrom(null).message).toBe("null")
  })
})

describe("harnessEventToMachineEvent", () => {
  it("maps remote-session-adopted to session.created", () => {
    const machine = harnessEventToMachineEvent({
      type: "remote-session-adopted",
      pendingSessionId: sessionId("pending-a") as never,
      sessionId: sessionA,
      projectId: null,
      title: "first",
    })
    expect(machine).toEqual({ event: "session.created", sessionId: "session-a" })
  })

  it("maps thinking-chunk-received to turn.chunk with the text", () => {
    const machine = harnessEventToMachineEvent({
      type: "thinking-chunk-received",
      sessionId: sessionA,
      requestId: requestA,
      text: "thinking…",
    })
    expect(machine).toEqual({
      event: "turn.chunk",
      sessionId: "session-a",
      text: "thinking…",
    })
  })

  it("maps turn-completed with the message count", () => {
    const messages: readonly HarnessMessage[] = [
      harnessMessage("m1"),
      harnessMessage("m2"),
      harnessMessage("m3"),
    ]
    const machine = harnessEventToMachineEvent({
      type: "turn-completed",
      sessionId: sessionA,
      requestId: requestA,
      messages,
      awaitingApproval: false,
    })
    expect(machine).toEqual({
      event: "turn.completed",
      sessionId: "session-a",
      messageCount: 3,
    })
  })

  it("maps turn-failed with the error classified into a MachineError", () => {
    const machine = harnessEventToMachineEvent({
      type: "turn-failed",
      sessionId: sessionA,
      requestId: requestA,
      error: cliError({ kind: "auth", code: "missing", message: "no key" }),
    })
    expect(machine).toEqual({
      event: "turn.failed",
      sessionId: "session-a",
      error: { code: "auth.missing", message: "no key" },
    })
  })

  it("returns null for events that have no machine equivalent", () => {
    expect(
      harnessEventToMachineEvent({
        type: "draft-saved",
        sessionId: sessionA,
        text: "draft",
        savedAtUnixMs: 1,
      })
    ).toBeNull()
  })

  it("returns null for connection/auth events too", () => {
    expect(
      harnessEventToMachineEvent({ type: "connection-established" })
    ).toBeNull()
    expect(
      harnessEventToMachineEvent({
        type: "auth-succeeded",
        subjectId: "s",
      })
    ).toBeNull()
  })

  it("does not widen the return type — null is preserved alongside events", () => {
    const ok: MachineEvent | null = harnessEventToMachineEvent({
      type: "thinking-chunk-received",
      sessionId: sessionA,
      requestId: requestA,
      text: "x",
    })
    const skipped: MachineEvent | null = harnessEventToMachineEvent({
      type: "draft-saved",
      sessionId: sessionA,
      text: "x",
      savedAtUnixMs: 1,
    })
    expect(ok).not.toBeNull()
    expect(skipped).toBeNull()
  })
})