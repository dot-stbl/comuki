import { useCallback, useRef } from "react"
import { useBlocker } from "@tanstack/react-router"

export interface UnsavedGuard {
  /** A navigation is being held while the operator answers. */
  asking: boolean
  /** Leave anyway. What was typed is gone. */
  discard: () => void
  /** Stay on the form, with everything still in it. */
  keep: () => void
  /**
   * Wrap a departure the operator actually asked for — pressing cancel, or a
   * submit that succeeded. Neither is an accident, so neither is questioned.
   */
  leave: (go: () => void) => void
}

/**
 * What happens to half a filled-in form when somebody leaves the page.
 *
 * A modal had no answer to give, because there was nothing to leave: the form
 * was on top of the screen it belonged to and the only ways out were its own
 * two buttons. A page has a rail, a breadcrumb, a back button and a URL bar,
 * and every one of them can take an operator away from three fields they just
 * typed. So the decision, stated: **an unsaved form asks before it is
 * abandoned, and never asks about a departure that was the point.**
 *
 * "Unsaved" is deliberately coarse — any field touched at all. A form this
 * short has no drafts worth diffing, and a guard that tried to be clever about
 * which edits matter is a guard that will one day silently drop the one that
 * did.
 *
 * `leaving` is a ref rather than state because `shouldBlockFn` is called by
 * the router at navigation time, after the handler that set it has already
 * run: a state update would not have landed yet, and the operator's own cancel
 * would be met with "are you sure you want to cancel".
 *
 * ## Why it lives with the shell
 *
 * It was written twice, once in `domains/identity` and once in
 * `domains/projects`, both noting that the pair was the promotion candidate.
 * The promotion goes beside `FormPage` rather than into `shared/hooks` for one
 * concrete reason: nothing under `shared/` imports the router today, and this
 * hook is nothing *but* the router — it is `@tanstack/react-router`'s blocker,
 * wrapped in the one decision this product has made about leaving a form. A
 * kit hook that reached for the app's router would make every kit consumer
 * need one.
 *
 * Known limit, recorded rather than papered over: `@tanstack/history` only
 * consults blockers on push and replace, so the browser's own back button
 * leaves without asking. `enableBeforeUnload` still covers a closed tab or a
 * typed URL. Cancel is wired to `history.back()` for exactly this reason and
 * gets the same free pass either way.
 */
export function useUnsavedGuard(dirty: boolean): UnsavedGuard {
  const leaving = useRef(false)

  const shouldBlockFn = useCallback(() => dirty && !leaving.current, [dirty])

  const blocker = useBlocker({
    shouldBlockFn,
    enableBeforeUnload: dirty,
    withResolver: true,
  })

  const leave = useCallback((go: () => void) => {
    leaving.current = true
    go()
  }, [])

  return {
    asking: blocker.status === "blocked",
    discard: () => blocker.proceed?.(),
    keep: () => blocker.reset?.(),
    leave,
  }
}
