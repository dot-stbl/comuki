import type { SearchTarget } from "@/app/search"

/**
 * The dock's state, held outside the component tree on purpose.
 *
 * The dock mounts inside `AppShell`, and every screen renders its own shell —
 * so navigating is a remount for the trigger and the sheet both. The
 * conversation, the half-typed message and the open sheet are the operator's,
 * not the box's; they live here and every mount reads them back, so walking
 * between screens with the sheet open (or closed on a draft) changes nothing
 * the operator can feel. A reload is the one thing this does not survive — a
 * fresh shift — which is the same honesty the chat store itself keeps.
 */
export interface ChatDockMemory {
  open: boolean
  chosenId: string | null
  draft: string
  seed: SearchTarget | null
}

export const chatDockMemory: ChatDockMemory = {
  open: false,
  chosenId: null,
  draft: "",
  seed: null,
}

/** Back to a closed, empty dock — used by tests, like the store's resets. */
export function resetChatDockMemory(): void {
  chatDockMemory.open = false
  chatDockMemory.chosenId = null
  chatDockMemory.draft = ""
  chatDockMemory.seed = null
}
