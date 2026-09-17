/**
 * `comuki runs list` — the run ledger as a fixed-column table:
 * id (short) · status (colored) · project · age. Project names resolve
 * through `GET /api/v1/projects`; an unresolvable id falls back to its
 * short form rather than failing the table.
 */
import { Text, useApp } from "ink"
import React, { useEffect, useState } from "react"
import { ComukiClient, type RunView } from "../lib/client"
import { whoAmI } from "../lib/auth"
import { describeError } from "./chat"
import type { ResolvedConfig } from "../lib/config"
import { ageFromIso, paintStatus, tableRow } from "../lib/format"
import { colors, symbols } from "../theme"
import { StatusLine } from "../components/StatusLine"

export interface RunsCommandProps {
  readonly config: ResolvedConfig
  readonly page: number
  readonly pageSize: number
  readonly filter?: string
}

export function RunsApp({ config, page, pageSize, filter }: RunsCommandProps) {
  const { exit } = useApp()
  const [identity, setIdentity] = useState("…")
  const [rows, setRows] = useState<string[]>([])
  const [total, setTotal] = useState<number | null>(null)
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
        const [runsPage, projects] = await Promise.all([
          client.runs(page, pageSize, filter),
          client.projects().catch(() => []),
        ])
        if (disposed) {
          return
        }
        const nameByProject = new Map(
          projects.map((item) => [item.id, item.slug])
        )
        setRows(runsPage.items.map((run) => runRow(run, nameByProject)))
        setTotal(runsPage.total)
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
  }, [config, page, pageSize, filter])

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
        <Text color="red">
          {"  "}
          {symbols.cross} {error}
        </Text>
      </>
    )
  }

  return (
    <>
      <StatusLine
        identity={identity}
        extra={filter ? `filter: ${filter}` : undefined}
      />
      <Text dimColor>
        {"  "}
        {tableRow([
          { text: "id", width: 14 },
          { text: "status", width: 13 },
          { text: "project", width: 16 },
          { text: "age", width: 0 },
        ])}
      </Text>
      {rows.map((row, index) => (
        <Text key={index}>
          {"  "}
          {row}
        </Text>
      ))}
      {total === null ? (
        <Text dimColor>
          {"     "}
          {symbols.bullet} fetching…
        </Text>
      ) : (
        <Text dimColor>
          {"  "}
          {symbols.bullet} {total} total
        </Text>
      )}
    </>
  )
}

function runRow(run: RunView, names: ReadonlyMap<string, string>): string {
  return tableRow([
    { text: colors.muted + run.id.slice(0, 13) + colors.reset, width: 14 },
    { text: paintStatus(run.status), width: 13 },
    { text: names.get(run.projectId) ?? run.projectId.slice(0, 8), width: 16 },
    { text: ageFromIso(run.updatedAt), width: 0 },
  ])
}
