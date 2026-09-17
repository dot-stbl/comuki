/**
 * `comuki status` — one platform snapshot, one screen, no chrome:
 * provider + queue, projects, knowledge corpus, run ledger. Each source
 * fails independently (403 on one permission must not blank the screen);
 * a failed line prints its reason dimmed instead.
 */
import { Text, useApp } from "ink"
import React, { useEffect, useState } from "react"
import { ComukiClient } from "../lib/client"
import { whoAmI } from "../lib/auth"
import { describeError } from "./chat"
import type { ResolvedConfig } from "../lib/config"
import { colors, symbols } from "../theme"
import { StatusLine } from "../components/StatusLine"

type Line = { ok: true; text: string } | { ok: false; label: string; reason: string }

const fmt = (value: number): string => value.toLocaleString("en-US")

export function StatusApp({ config }: { config: ResolvedConfig }) {
  const { exit } = useApp()
  const [identity, setIdentity] = useState("…")
  const [lines, setLines] = useState<Line[]>([])
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

      const [compute, projects, knowledge, runs] = await Promise.allSettled([
        client.compute(),
        client.projects(),
        client.knowledgeDocuments(1, 100),
        client.runs(1, 100),
      ])
      if (disposed) {
        return
      }

      const next: Line[] = []
      if (compute.status === "fulfilled") {
        const snapshot = compute.value
        const queued = snapshot.pools.reduce((sum, pool) => sum + pool.queued, 0)
        const running = snapshot.pools.reduce((sum, pool) => sum + pool.running, 0)
        next.push({
          ok: true,
          text: `${paint("provider:")} ${snapshot.provider}${paint(`  ·  queue: ${queued} queued ${symbols.bullet} ${running} running`)}`,
        })
      } else {
        next.push({ ok: false, label: "provider", reason: describeError(compute.reason) })
      }

      if (projects.status === "fulfilled") {
        next.push({ ok: true, text: `${paint("projects:")} ${projects.value.length} active` })
      } else {
        next.push({ ok: false, label: "projects", reason: describeError(projects.reason) })
      }

      if (knowledge.status === "fulfilled") {
        const chunks = knowledge.value.items.reduce(
          (sum, document) => sum + document.chunkCount,
          0
        )
        const note =
          knowledge.value.total > knowledge.value.items.length
            ? paint(` (+${fmt(knowledge.value.total - knowledge.value.items.length)} more, first page only)`)
            : ""
        next.push({
          ok: true,
          text: `${paint("knowledge:")} ${fmt(knowledge.value.total)} documents ${symbols.bullet} ${fmt(chunks)} chunks${note}`,
        })
      } else {
        next.push({ ok: false, label: "knowledge", reason: describeError(knowledge.reason) })
      }

      if (runs.status === "fulfilled") {
        const byStatus = new Map<string, number>()
        for (const run of runs.value.items) {
          byStatus.set(run.status, (byStatus.get(run.status) ?? 0) + 1)
        }
        const hot = ["queued", "running", "escalated", "awaiting_approval"]
          .map((status) => ({ status, count: byStatus.get(status) ?? 0 }))
          .filter((entry) => entry.count > 0)
          .map((entry) => `${entry.count} ${entry.status}`)
          .join(paint(` ${symbols.bullet} `))
        next.push({
          ok: true,
          text: `${paint("runs:")} ${fmt(runs.value.total)} total${hot ? paint(`  ·  `) + hot : ""}`,
        })
      } else {
        next.push({ ok: false, label: "runs", reason: describeError(runs.reason) })
      }

      setLines(next)
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
      <StatusLine identity={identity} />
      {lines.length === 0 ? (
        <Text dimColor>
          {"     "}
          {symbols.bullet} fetching…
        </Text>
      ) : null}
      {lines.map((line, index) =>
        line.ok ? (
          <Text key={index}>{"  "}{line.text}</Text>
        ) : (
          <Text key={index} dimColor>
            {"  "}
            {line.label}: {line.reason}
          </Text>
        )
      )}
    </>
  )
}

function paint(text: string): string {
  return colors.dim + text + colors.reset
}
