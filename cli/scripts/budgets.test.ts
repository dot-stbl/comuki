/**
 * Budgets round-trip — read the script's JSON output, parse it, and
 * verify every field is a positive finite number. The script itself is
 * the source of truth; this test exists so a typo (`idleCpuPercent:
 * -1`) fails in CI before the value reaches the README.
 */

import { afterAll, beforeAll, describe, expect, test } from "bun:test"
import { spawn, type Subprocess } from "bun"
import { join } from "node:path"

// `import.meta.dir` of a test file under cli/scripts is the scripts/
// dir itself; SCRIPT_PATH points at the budget source next to it.
const SCRIPT_PATH = join(import.meta.dir, "budgets.ts")

interface CapturedRun {
  readonly stdout: string
  readonly exitCode: number
}

let captured: CapturedRun | null = null

describe("scripts/budgets.ts", () => {
  beforeAll(async () => {
    const proc: Subprocess = spawn({
      cmd: ["bun", SCRIPT_PATH, "--json"],
      stdout: "pipe",
      stderr: "pipe",
    })
    const out = await new Response(proc.stdout).text()
    const exitCode = await proc.exited
    captured = { stdout: out, exitCode }
  })

  afterAll(() => {
    captured = null
  })

  test("script exits 0", () => {
    expect(captured?.exitCode).toBe(0)
  })

  test("every field is a positive finite number", () => {
    const parsed = JSON.parse(captured?.stdout ?? "{}") as Record<string, unknown>
    const expected = [
      "startupMs",
      "rssMb",
      "idleCpuPercent",
      "renderLatencyP95Ms",
      "eventBacklogMax",
      "longSessionHours",
    ] as const
    for (const key of expected) {
      const value = parsed[key]
      expect(typeof value).toBe("number")
      expect(Number.isFinite(value as number)).toBe(true)
      expect(value as number).toBeGreaterThan(0)
    }
  })

  test("script renders a stable human-readable table by default", async () => {
    const proc: Subprocess = spawn({
      cmd: ["bun", SCRIPT_PATH],
      stdout: "pipe",
      stderr: "pipe",
    })
    const out = await new Response(proc.stdout).text()
    const exitCode = await proc.exited
    expect(exitCode).toBe(0)
    expect(out).toContain("performance budgets")
    expect(out).toContain("startupMs")
    expect(out).toContain("idleCpuPercent")
    expect(out).toContain("longSessionHours")
  })
})
