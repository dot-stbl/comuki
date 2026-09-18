/**
 * `comuki status` — one platform snapshot, one screen, no chrome:
 * health, provider + queue, projects, knowledge corpus, run ledger.
 * Each source fails independently (403 on one permission must not
 * blank the screen); a failed line prints its reason dimmed instead.
 *
 * The snapshot itself lives in `lib/status.ts` so the REPL `/status`
 * reuses the same fetch + the same lines.
 */
import { Text, useApp } from "ink"
import React, { useEffect, useState } from "react"
import { ComukiClient } from "../lib/client"
import { whoAmI } from "../lib/auth"
import type { ResolvedConfig } from "../lib/config"
import { describeError } from "./chat"
import {
  fetchStatusSnapshot,
  renderStatusLine,
  statusLines,
  type StatusLine,
} from "../lib/status"
import { mapStatusJson, printJson, type StatusJsonError } from "../lib/jsonout"
import { symbols } from "../theme"
import { StatusLine as IdentityLine } from "../components/StatusLine"

/** `--json` path — no Ink, stdout only. */
export async function printStatusJson(config: ResolvedConfig): Promise<void> {
  const client = new ComukiClient(config)
  const who = await whoAmI(client)
  const [compute, projects, knowledge, runs] = await Promise.allSettled([
    client.compute(),
    client.projects(),
    client.knowledgeDocuments(1, 100),
    client.runs(1, 100),
  ])
  const errors: StatusJsonError = {}
  if (compute.status === "rejected") {
    errors.compute = describeError(compute.reason)
  }
  if (projects.status === "rejected") {
    errors.projects = describeError(projects.reason)
  }
  if (knowledge.status === "rejected") {
    errors.knowledge = describeError(knowledge.reason)
  }
  if (runs.status === "rejected") {
    errors.runs = describeError(runs.reason)
  }
  printJson(
    mapStatusJson({
      who,
      compute: compute.status === "fulfilled" ? compute.value : undefined,
      projects: projects.status === "fulfilled" ? projects.value : undefined,
      knowledge:
        knowledge.status === "fulfilled" ? knowledge.value : undefined,
      runs: runs.status === "fulfilled" ? runs.value : undefined,
      errors,
    })
  )
}

export function StatusApp({ config }: { config: ResolvedConfig }) {
  const { exit } = useApp()
  const [identity, setIdentity] = useState("…")
  const [lines, setLines] = useState<StatusLine[]>([])
  const [done, setDone] = useState(false)

  useEffect(() => {
    let disposed = false
    void (async () => {
      const client = new ComukiClient(config)
      const who = await whoAmI(client)
      if (disposed) {
        return
      }
      setIdentity(who.label)

      const snapshot = await fetchStatusSnapshot(client)
      if (disposed) {
        return
      }
      setLines(statusLines(snapshot))
      setDone(true)
    })()
    return () => {
      disposed = true
    }
  }, [config])

  useEffect(() => {
    if (done) {
      exit()
    }
  }, [done, exit])

  return (
    <>
      <IdentityLine identity={identity} />
      {lines.length === 0 ? (
        <Text dimColor>
          {"     "}
          {symbols.bullet} fetching…
        </Text>
      ) : null}
      {lines.map((line, index) => (
        <Text key={index}>
          {"  "}
          {renderStatusLine(line)}
        </Text>
      ))}
    </>
  )
}
