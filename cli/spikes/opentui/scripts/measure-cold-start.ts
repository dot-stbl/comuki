/**
 * Cold-start + first-frame measurement for the Core spike.
 *
 * Builds a `createTestRenderer(80x24, memory mode)` and a Core chat
 * shell, then captures:
 *   - cold-start time (renderer + shell + first paint)
 *   - first frame byte size
 *   - native stats keys present
 *
 * Short-lived: under a second on the spike's hardware.
 */
import { performance } from "node:perf_hooks"
import { createTestRenderer } from "@opentui/core/testing"
import { createChatShell } from "../src/core/chat-shell.js"

async function main() {
  const start = performance.now()

  const setup = await createTestRenderer({
    width: 80,
    height: 24,
    kittyKeyboard: false,
    otherModifiersMode: true,
  })
  const setupMs = performance.now() - start

  const paintStart = performance.now()
  await createChatShell(
    { width: 80, height: 24, focusMode: true },
    { renderer: setup.renderer, memoryMode: true },
  )
  await setup.waitForVisualIdle()
  const firstPaintMs = performance.now() - paintStart

  const firstFrame = setup.captureCharFrame()
  const stats = setup.getNativeStats()

  const totalMs = performance.now() - start

  console.log(
    JSON.stringify(
      {
        setup: "createTestRenderer(80x24, memory mode)",
        totalMs: round(totalMs),
        setupMs: round(setupMs),
        firstPaintMs: round(firstPaintMs),
        firstFrameBytes: firstFrame.length,
        firstFrameRows: firstFrame.split("\n").length,
        focusMode: true,
        nativeStatsPresent: !!stats,
        nativeStatsSample: stats
          ? Object.keys(stats as object).slice(0, 8)
          : [],
      },
      null,
      2,
    ),
  )

  await setup.renderer.destroy()
  process.exit(0)
}

function round(n: number): number {
  return Math.round(n * 100) / 100
}

main().catch((err) => {
  console.error("measure-cold-start failed:", err)
  process.exit(1)
})
