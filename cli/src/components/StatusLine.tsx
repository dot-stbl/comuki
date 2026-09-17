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
import { Text } from "ink"
import React from "react"
import type { HubConnectionState } from "../lib/signalr"

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

export type LatencyTone = "green" | "yellow" | "red"

/** Green under 200ms, yellow under 1s, red at or above. */
export function latencyTone(latencyMs: number): LatencyTone {
  if (latencyMs < 200) {
    return "green"
  }
  if (latencyMs < 1000) {
    return "yellow"
  }
  return "red"
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

/** live pops green, reconnecting warns yellow, the rest stay dim. */
const CONNECTION_TONES: Record<HubConnectionState, string | undefined> = {
  live: "green",
  connecting: undefined,
  reconnecting: "yellow",
  offline: undefined,
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
      <Text color={CONNECTION_TONES[connection]}>
        {CONNECTION_LABELS[connection]}
      </Text>
    )
  }
  if (typeof latencyMs === "number") {
    parts.push(
      <Text color={latencyTone(latencyMs)}>{latencyLabel(latencyMs)}</Text>
    )
  }
  return (
    <Text dimColor>
      {"  "}
      {parts.map((part, index) => (
        <React.Fragment key={index}>
          {index > 0 ? <Text color="#8787f3"> · </Text> : null}
          {part}
        </React.Fragment>
      ))}
    </Text>
  )
}
