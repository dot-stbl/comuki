/**
 * `comuki procedures trace <runId>` — the planned-vs-observed
 * timeline for one procedure-pinned run, rendered as a fixed-column
 * table. The host's <c>GET /api/v1/procedures/runs/{runId}/trace</c>
 * endpoint (PHASE 1) is the same shape Studio's Replay panel reads;
 * this command is the terminal mirror — the same row vocabulary,
 * without the React Flow canvas.
 *
 * Each row carries the trace's `EventType` (the same closed vocabulary
 * the admission binder stamps: `pin_recorded`, repair / human-gate
 * events, etc.), the node the event was about (the graph node id
 * for runtime events, the version id for `pin_recorded`), the
 * human-readable detail, and the wall-clock time the coordinator
 * stamped the event at.
 *
 * `--json` path — no Ink, one machine envelope on stdout.
 */
import { Text, useApp } from "ink"
import React, { useEffect, useState } from "react"
import {
  ComukiClient,
  type ProcedureTraceResponse,
  type TraceEventDto,
} from "../lib/client"
import { whoAmI } from "../lib/auth"
import { describeError } from "./chat"
import type { ResolvedConfig } from "../lib/config"
import { tableRow } from "../lib/format"
import { JsonOutput } from "../machine/output"
import { machineErrorFrom } from "../machine/mapping"
import { colors, palette, symbols } from "../theme"
import { StatusLine } from "../components/StatusLine"

export interface ProceduresTraceCommandProps {
  readonly config: ResolvedConfig
  readonly runId: string
}

/** `--json` path — no Ink, one machine envelope on stdout. */
export async function printProcedureTraceJson(
  config: ResolvedConfig,
  runId: string
): Promise<void> {
  const out = new JsonOutput()
  out.start("procedureTrace")
  try {
    const client = new ComukiClient(config)
    const trace = await client.procedureRunTrace(runId)
    out.complete({ ...trace })
  } catch (reason) {
    out.fail(machineErrorFrom(reason))
    process.exitCode = out.exitCode
  }
}

/**
 * Ink renderer for the trace. Each event is a row, ordered by the
 * `at` field the host stamps (chronological); the pinned version id
 * sits in the header so the operator knows which version's planned-vs-
 * observed the timeline reports.
 */
export function ProceduresTraceApp({
  config,
  runId,
}: ProceduresTraceCommandProps) {
  const { exit } = useApp()
  const [identity, setIdentity] = useState("…")
  const [trace, setTrace] = useState<ProcedureTraceResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  useEffect(() => {
    let disposed = false
    void (async () => {
      const client = new ComukiClient(config)
      const who = await whoAmI(client).catch(() => null)
      if (disposed) {
        return
      }
      if (who) {
        setIdentity(who.label)
      }

      try {
        const result = await client.procedureRunTrace(runId)
        if (!disposed) {
          setTrace(result)
        }
      } catch (reason) {
        if (!disposed) {
          setError(describeError(reason))
        }
      }
      if (!disposed) {
        setDone(true)
      }
    })()
    return () => {
      disposed = true
    }
  }, [config, runId])

  useEffect(() => {
    if (done) {
      exit()
    }
  }, [done, exit])

  useEffect(() => {
    if (error) {
      process.exitCode = 1
    }
  }, [error])

  if (error) {
    return (
      <>
        <StatusLine identity={identity} />
        <Text color={palette.error}>
          {"  "}
          {symbols.cross} {error}
        </Text>
      </>
    )
  }

  if (trace === null) {
    return (
      <>
        <StatusLine identity={identity} extra={`run: ${runId}`} />
        <Text dimColor>
          {"     "}
          {symbols.bullet} fetching trace…
        </Text>
      </>
    )
  }

  return (
    <>
      <StatusLine
        identity={identity}
        extra={`run: ${runId} · pinned ${shorten(trace.pinnedVersionId, 12)}`}
      />
      <Text dimColor>
        {"  "}
        {tableRow([
          { text: "at", width: 10 },
          { text: "event", width: 22 },
          { text: "node", width: 22 },
          { text: "detail", width: 0 },
        ])}
      </Text>
      {trace.events.map((event, index) => (
        <TraceRow key={`${event.at}-${index}`} event={event} />
      ))}
      <Text dimColor>
        {"  "}
        {symbols.bullet} {trace.events.length} event{trace.events.length === 1 ? "" : "s"}
      </Text>
    </>
  )
}

function TraceRow({ event }: { event: TraceEventDto }) {
  return (
    <Text>
      {"  "}
      {tableRow([
        {
          text: colors.dim + shortIsoTime(event.at) + colors.reset,
          width: 10,
        },
        { text: colors.muted + event.eventType + colors.reset, width: 22 },
        {
          text: colors.faint + shorten(event.nodeId, 22) + colors.reset,
          width: 22,
        },
        { text: event.detail, width: 0 },
      ])}
    </Text>
  )
}

function shortIsoTime(value: string): string {
  // The host stamps full ISO-8601; the table is narrow, take the HH:MM:SS
  // slice so rows line up without losing the day stamp.
  const match = value.match(/T(\d{2}:\d{2}:\d{2})/)
  return match?.[1] ?? value
}

function shorten(value: string, max: number): string {
  return value.length <= max ? value : value.slice(0, max - 1) + "…"
}