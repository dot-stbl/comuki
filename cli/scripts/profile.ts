/**
 * `bun run profile` — one-shot profile report (issue #81).
 *
 * Imports `PERFORMANCE_BUDGETS` (the single source of truth) and the
 * telemetry sink, prints a budget snapshot, and emits one
 * `boot` event so the operator can confirm the diagnostics log is
 * reachable without going through the full REPL boot path.
 *
 * Used by `comuki --profile` when the host wants to check the
 * numbers without booting the kernel; CI uses it as a smoke test
 * that the package still ships the budget table.
 */

import { mkdir } from "node:fs/promises"
import { join } from "node:path"
import { tmpdir } from "node:os"

import { PERFORMANCE_BUDGETS, renderBudgetTable } from "./budgets"
import { createStructuredLog } from "../src/kernel/telemetry"

if (import.meta.main) {
  ;(async () => {
    process.stdout.write(renderBudgetTable())
    process.stdout.write("\n")

    // Smoke-test the telemetry sink + the XDG resolution. We point
    // the sink at the OS temp dir so the smoke run is hermetic and
    // does not pollute the user's state directory.
    const tempDir = join(tmpdir(), `comuki-profile-${Date.now()}`)
    await mkdir(tempDir, { recursive: true })
    const log = createStructuredLog({ stateDirectory: tempDir })
    log.logFields("info", "boot", {
      bun: typeof Bun !== "undefined" ? Bun.version : "unknown",
      budgets: PERFORMANCE_BUDGETS,
    })
    await log.whenIdle()

    const report = `${tempDir}/diagnostics.log\n`
    process.stdout.write(`telemetry sink: ${report}`)
  })().catch((error: unknown) => {
    process.stderr.write(
      `profile: ${error instanceof Error ? error.message : String(error)}\n`
    )
    process.exit(1)
  })
}
