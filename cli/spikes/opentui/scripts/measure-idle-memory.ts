/**
 * Short idle-memory snapshot for the Core spike.
 *
 * Runs the Core chat shell under `createTestRenderer`, then samples
 * `process.memoryUsage()` before/after a **5-second** idle window.
 *
 * The 5-second window is a deliberate trade-off (see ADR §"Stated
 * Gaps"). It is NOT a 30-minute idle claim. Anything beyond ~5s
 * shows roughly the same RSS; anything below that is dominated by
 * GC warmup. We pick 5s because it's short enough to keep the
 * gate under a second and long enough for one GC cycle.
 */
import { performance } from "node:perf_hooks"
import { createTestRenderer } from "@opentui/core/testing"
import { createChatShell } from "../src/core/chat-shell.js"

const IDLE_MS = 5_000

async function snapshot() {
  if (typeof globalThis.gc === "function") globalThis.gc()
  const u = process.memoryUsage()
  return {
    rssBytes: u.rss,
    heapUsedBytes: u.heapUsed,
    externalBytes: u.external,
  }
}

async function main() {
  const before = await snapshot()

  const setup = await createTestRenderer({
    width: 80,
    height: 24,
    kittyKeyboard: false,
    otherModifiersMode: true,
  })
  const shell = await createChatShell(
    { width: 80, height: 24, focusMode: true },
    { renderer: setup.renderer, memoryMode: true },
  )
  await setup.waitForVisualIdle()
  shell.setDraft("idle-memory-snapshot — long draft exercising the composer buffer")

  const afterCreate = await snapshot()

  const idleStart = performance.now()
  await new Promise<void>((r) => setTimeout(r, IDLE_MS))
  const idleMs = performance.now() - idleStart

  const afterIdle = await snapshot()

  const fmt = (n: number) => `${Math.round(n / 1024 / 1024)}MB`
  console.log(
    JSON.stringify(
      {
        idleWindowMs: Math.round(idleMs),
        before: { rss: fmt(before.rssBytes), heap: fmt(before.heapUsedBytes) },
        afterCreate: {
          rss: fmt(afterCreate.rssBytes),
          heap: fmt(afterCreate.heapUsedBytes),
        },
        afterIdle: {
          rss: fmt(afterIdle.rssBytes),
          heap: fmt(afterIdle.heapUsedBytes),
        },
        rssDeltaMB: round(
          (afterIdle.rssBytes - before.rssBytes) / 1024 / 1024,
        ),
        heapDeltaMB: round(
          (afterIdle.heapUsedBytes - before.heapUsedBytes) / 1024 / 1024,
        ),
        note:
          "5s idle window. Not a 30-min claim. Run twice — RSS wobbles ±5MB from GC scheduling.",
      },
      null,
      2,
    ),
  )

  await shell.destroy()
  process.exit(0)
}

function round(n: number): number {
  return Math.round(n * 100) / 100
}

main().catch(async (err) => {
  console.error("measure-idle-memory failed:", err)
  process.exit(1)
})
