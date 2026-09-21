/**
 * Performance budgets (issue #81) — single source of truth for every
 * number the README, the `comuki --explain-budgets` flag, and the
 * profile script read.
 *
 * The defaults match the brief:
 *
 *   startupMs           1500 ms p95
 *   rssMb               200 MB p95
 *   idleCpuPercent      1.0 %
 *   renderLatencyP95Ms  30 ms
 *   eventBacklogMax     256
 *   longSessionHours    72
 *
 * Tests parse the JSON output of this script and lock the shape (a
 * positive finite number per field); updates flow through PR review.
 *
 * Run `bun run scripts/budgets.ts` to print the resolved table — the
 * README's "Performance budgets" section is hand-checked against this
 * output.
 */

export interface PerformanceBudgets {
  readonly startupMs: number
  readonly rssMb: number
  readonly idleCpuPercent: number
  readonly renderLatencyP95Ms: number
  readonly eventBacklogMax: number
  readonly longSessionHours: number
}

/** Source of truth — every number the README + CI gates reference. */
export const PERFORMANCE_BUDGETS: PerformanceBudgets = Object.freeze({
  startupMs: 1500,
  rssMb: 200,
  idleCpuPercent: 1.0,
  renderLatencyP95Ms: 30,
  eventBacklogMax: 256,
  longSessionHours: 72,
})

/** Human-readable table — printed by the script and the `comuki --explain-budgets` flag. */
export function renderBudgetTable(budgets: PerformanceBudgets = PERFORMANCE_BUDGETS): string {
  const rows: ReadonlyArray<readonly [string, string, string]> = [
    ["startupMs", String(budgets.startupMs), "ms p95 — process start to first frame"],
    ["rssMb", String(budgets.rssMb), "MB p95 — `process.memoryUsage().rss`"],
    ["idleCpuPercent", budgets.idleCpuPercent.toFixed(1), "% — sampled every 1s with no input"],
    ["renderLatencyP95Ms", String(budgets.renderLatencyP95Ms), "ms — `captureCharFrame` round-trip p95"],
    ["eventBacklogMax", String(budgets.eventBacklogMax), "frames — pending SignalR hub queue"],
    ["longSessionHours", String(budgets.longSessionHours), "hours — time since `kernel.start()`"],
  ]
  const nameWidth = Math.max(...rows.map((row) => row[0].length))
  const valueWidth = Math.max(...rows.map((row) => row[1].length))
  const lines: string[] = [
    `comuki · performance budgets (issue #81)`,
    "",
    `${"budget".padEnd(nameWidth)}  ${"value".padEnd(valueWidth)}  source`,
    `${"-".repeat(nameWidth)}  ${"-".repeat(valueWidth)}  ${"-".repeat(20)}`,
  ]
  for (const row of rows) {
    lines.push(`${row[0].padEnd(nameWidth)}  ${row[1].padEnd(valueWidth)}  ${row[2]}`)
  }
  return `${lines.join("\n")}\n`
}

if (import.meta.main) {
  // Default: print the human-readable table. `--json` flips to JSON
  // output so tests / scripts can parse the same data without scraping
  // human text.
  const json = process.argv.includes("--json")
  if (json) {
    process.stdout.write(`${JSON.stringify(PERFORMANCE_BUDGETS, null, 2)}\n`)
  } else {
    process.stdout.write(renderBudgetTable())
  }
}
