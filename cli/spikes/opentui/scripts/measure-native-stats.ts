/**
 * Native renderer frame stats for the Core spike.
 *
 * Drives the renderer through a handful of state changes, then
 * dumps the `getNativeStats()` keys and a sample of their values.
 * These stats come from the native OpenTUI zig layer — the spike
 * never touches the byte counts directly.
 */
import { createTestRenderer } from "@opentui/core/testing"
import { createChatShell } from "../src/core/chat-shell.js"

async function main() {
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

  // Trigger a couple of state updates so the renderer has done real
  // work before we sample.
  shell.setDraft("frame-stats probe — first")
  await setup.waitForVisualIdle()
  shell.setDraft("frame-stats probe — second")
  await setup.waitForVisualIdle()
  shell.setDraft("frame-stats probe — third")
  await setup.waitForVisualIdle()

  const stats = setup.getNativeStats()
  const sample: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(stats as Record<string, unknown>)) {
    sample[k] = v
  }

  console.log(JSON.stringify({ nativeStats: sample }, null, 2))

  await shell.destroy()
  process.exit(0)
}

main().catch((err) => {
  console.error("measure-native-stats failed:", err)
  process.exit(1)
})
