/**
 * Dogfood test (issue #81) — run the offline harness against an
 * in-memory kernel and assert every step exits 0.
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"
import { rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"

import { runDogfood, type DogfoodReport } from "./dogfood"

async function mkTempDir(label: string): Promise<string> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`
  const dir = join(tmpdir(), `${label}-${stamp}`)
  await rm(dir, { recursive: true, force: true })
  return dir
}

describe("dogfood — offline harness", () => {
  let tempDir: string
  let report: DogfoodReport | null = null

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-dogfood")
    report = await runDogfood({
      mock: true,
      stateDirectory: tempDir,
      argv: ["--explain-mode"],
    })
  })

  afterEach(async () => {
    await rm(tempDir, { recursive: true, force: true })
    report = null
  })

  test("every step exits 0", () => {
    expect(report).not.toBeNull()
    expect(report?.ok).toBe(true)
    for (const step of report?.steps ?? []) {
      expect({ name: step.name, exit: step.exit }).toEqual({
        name: step.name,
        exit: 0,
      })
    }
  })

  test("report covers every shipped surface (8 steps)", () => {
    const names = (report?.steps ?? []).map((step) => step.name)
    expect(names).toEqual([
      "explain-mode",
      "machine-envelope",
      "ink-host-slash",
      "opentui-host",
      "palette-sessions",
      "palette-swarm-canvas",
      "transcript-entry",
      "budgets",
    ])
  })

  test("explain-mode asserts linear=off when the flag is set", () => {
    const step = (report?.steps ?? []).find((s) => s.name === "explain-mode")
    expect(step).not.toBeUndefined()
    // argv passed `--explain-mode` without `--mode linear`, so the
    // resolved modes must report `linear=off` and the step must
    // exit 0.
    expect(step?.exit).toBe(0)
    expect(step?.status).toBe("ok")
  })

  test("transcript-entry wrote and read the NDJSON session file", async () => {
    const step = (report?.steps ?? []).find((s) => s.name === "transcript-entry")
    expect(step?.exit).toBe(0)
    expect(step?.detail).toContain("dogfood-session-1")
  })

  test("budgets table is reachable and stable", () => {
    const step = (report?.steps ?? []).find((s) => s.name === "budgets")
    expect(step?.exit).toBe(0)
    expect(step?.detail).toContain("startupMs=")
  })
})
