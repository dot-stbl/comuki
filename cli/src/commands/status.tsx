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
import {
  fetchStatusSnapshot,
  renderStatusLine,
  statusLines,
  type StatusLine,
} from "../lib/status"
import { symbols } from "../theme"
import { StatusLine as IdentityLine } from "../components/StatusLine"

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
