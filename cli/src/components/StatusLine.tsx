/**
 * The one-shot header line: `comuki v0.2 · <identity> · project: nova ·
 * <host> · <connection> · <42ms>`. Printed at the top of every command's
 * output; pure text, accent separators, no chrome.
 *
 * The chat REPL feeds the live segments — hub connection state, server
 * host, chat-send EMA — while one-shot commands (status, runs, login)
 * omit them and keep the classic look. Colours stay subtle: the state
 * and latency chips reuse the theme's green/yellow vocabulary through
 * Ink colour names.
 */
import { Box, Text } from "ink"
import React from "react"
import { contextMeterLabel, DEFAULT_CONTEXT_WINDOW } from "../lib/context"
import type { HubConnectionState } from "../lib/signalr"
import { palette } from "../theme"

export interface StatusLineProps {
  readonly identity: string
  readonly project?: string
  readonly extra?: string
  /** Hub connection chip (REPL only): live / connecting… / … */
  readonly connection?: HubConnectionState
  /** Server base URL — only the host part is shown, never the full URL. */
  readonly serverUrl?: string
  /** EMA of the last chat POST round-trips; null/undefined hides the badge. */
  readonly latencyMs?: number | null
  /**
   * Summed tokensIn+tokensOut of the active session. Hidden when
   * undefined (no token data on any message meta).
   */
  readonly contextUsed?: number
  /** Context window for the meter; default 128k. */
  readonly contextWindow?: number
}

export const CLI_VERSION = "0.2.0"

/** `https://api.comuki.dev/ws/runs` → `api.comuki.dev`; unparsable → null. */
export function hostFromUrl(url: string): string | null {
  try {
    return new URL(url).host || null
  } catch {
    return null
  }
}

export type LatencyTone = "ok" | "waiting" | "error"

/** Lavender under 200ms, waiting under 1s, yellow at or above. */
export function latencyTone(latencyMs: number): LatencyTone {
  if (latencyMs < 200) {
    return "ok"
  }
  if (latencyMs < 1000) {
    return "waiting"
  }
  return "error"
}

/** Deck hex for a latency tone — colour pairs with the ms value. */
export function latencyColor(tone: LatencyTone): string {
  return tone === "ok" ? palette.ok : tone === "waiting" ? palette.waiting : palette.error
}

/** `42.4` → `42ms`. */
export function latencyLabel(latencyMs: number): string {
  return `${Math.round(latencyMs)}ms`
}

const CONNECTION_LABELS: Record<HubConnectionState, string> = {
  live: "live",
  connecting: "connecting…",
  reconnecting: "reconnecting…",
  offline: "offline",
}

/** live pops ok-lavender, reconnecting waits yellow, the rest stay dim. */
function connectionTone(state: HubConnectionState): string | undefined {
  if (state === "live") {
    return palette.ok
  }
  if (state === "reconnecting") {
    return palette.waiting
  }
  return undefined
}

/** Status-bar label for a hub state (with the ellipsis forms). */
export function connectionLabel(state: HubConnectionState): string {
  return CONNECTION_LABELS[state]
}

export function StatusLine({
  identity,
  project,
  extra,
  connection,
  serverUrl,
  latencyMs,
  contextUsed,
  contextWindow,
}: StatusLineProps) {
  const host = serverUrl === undefined ? null : hostFromUrl(serverUrl)
  const parts: React.ReactNode[] = [`comuki v${CLI_VERSION}`, identity]
  if (project) {
    parts.push(`project: ${project}`)
  }
  if (extra) {
    parts.push(extra)
  }
  if (host) {
    parts.push(host)
  }
  if (connection) {
    parts.push(
      <Text color={connectionTone(connection)} backgroundColor={palette.rail}>
        {CONNECTION_LABELS[connection]}
      </Text>
    )
  }
  if (typeof latencyMs === "number") {
    parts.push(
      <Text
        color={latencyColor(latencyTone(latencyMs))}
        backgroundColor={palette.rail}
      >
        {latencyLabel(latencyMs)}
      </Text>
    )
  }
  if (typeof contextUsed === "number") {
    parts.push(
      contextMeterLabel(contextUsed, contextWindow ?? DEFAULT_CONTEXT_WINDOW)
    )
  }
  return (
    <Box width="100%" justifyContent="flex-start">
      <Text dimColor backgroundColor={palette.rail}>
        {"  "}
        {parts.map((part, index) => (
          <React.Fragment key={index}>
            {index > 0 ? (
              <Text color={palette.brand} backgroundColor={palette.rail}>
                {" · "}
              </Text>
            ) : null}
            {part}
          </React.Fragment>
        ))}
      </Text>
    </Box>
  )
}
