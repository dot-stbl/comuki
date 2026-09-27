// scripts/lib/storybook-ready-signal.ts
//
// Shared "is this story actually finished?" signal for a Storybook preview
// page, independent of `#storybook-root` gaining DOM children — the thing
// that breaks for a story whose content portals into `document.body`
// (react-aria-components' `Modal`/`Dialog`, and everything built on it:
// `BottomSheet`, `ConfirmDialog`, `FormDialog`, `Dialog` itself).
//
// Used by `scripts/ui-probe.ts` only. The vitest stories harness
// (`@storybook/addon-vitest`'s `storybookTest()` plugin,
// `vitest.config.ts` `stories` project) composes and renders stories
// directly in the test iframe and never navigates, so it needs no
// ready-signal of its own.
//
// Storybook's own channel emits `storyFinished` (or, on older builds,
// `storyRendered`) once loading, rendering *and* any play function have
// completed — on both the success and the error path (`PreparedStory.render`
// in `@storybook/core` wraps the whole render body and always emits it in a
// `finally`-equivalent fashion). That is driven by Storybook's render
// lifecycle, not by where in the DOM the story happened to mount its
// content, so it resolves correctly for a portal-based story where
// `#storybook-root` itself never gains children.

import type { Page } from "playwright"

/** What `__STORYBOOK_ADDONS_CHANNEL__` looks like from the page side — just
 *  enough of its shape for `.on()`, never imported at runtime (this type
 *  only exists inside a function Playwright serializes and evaluates in the
 *  browser; TS type annotations are erased before that happens). */
interface StorybookChannelLike {
  on(event: string, listener: (...args: never[]) => void): void
}

export interface StoryReadyState {
  readonly finished: boolean
  readonly error: string | null
}

/** The `window` property the installed listener records into, and the
 *  Node side polls. A literal string, not a `Symbol`, so it survives
 *  `page.addInitScript`/`page.evaluate` serialization. */
const READY_STATE_KEY = "__storybookReadySignal"

/**
 * Installed via `page.addInitScript` so it runs before Storybook's own
 * preview bundle on every navigation, and records channel events into a
 * global the Node side polls for with `waitForFunction`. `storyFinished` is
 * Storybook's own terminal event for a render — see the module docblock —
 * so waiting on it is not tied to `#storybook-root` gaining children at all.
 */
export async function installStoryReadySignal(page: Page): Promise<void> {
  await page.addInitScript((stateKey: string) => {
    const state = { finished: false, error: null as string | null }
    ;(window as unknown as Record<string, typeof state>)[stateKey] = state

    const install = (): void => {
      const channel = (window as unknown as { __STORYBOOK_ADDONS_CHANNEL__?: StorybookChannelLike })
        .__STORYBOOK_ADDONS_CHANNEL__
      if (!channel) {
        setTimeout(install, 10)
        return
      }
      channel.on("storyFinished", (data: { status?: string }) => {
        state.finished = true
        if (data?.status === "error" && state.error === null) {
          state.error = "story finished with an error (see console.json / interactions panel)"
        }
      })
      // Fires instead of `storyFinished` when the requested story is
      // already the current one (a no-op re-selection) — the shape a
      // caller that pre-rendered the story itself via a direct navigation
      // (see `.storybook/test-runner.ts`'s `preVisit`) sees when something
      // downstream re-asks for the same story.
      channel.on("storyUnchanged", () => {
        state.finished = true
      })
      channel.on("storyMissing", (id: unknown) => {
        state.finished = true
        state.error = `story not found: ${String(id)}`
      })
      // These fire before `storyFinished` on the error path and carry the
      // actual message — `storyFinished` only carries a status flag.
      channel.on("storyErrored", (payload: { description?: string }) => {
        state.error = payload?.description ?? "storyErrored"
      })
      channel.on("storyThrewException", (error: { message?: string }) => {
        state.error = error?.message ?? "storyThrewException"
      })
      channel.on("playFunctionThrewException", (error: { message?: string }) => {
        state.error = error?.message ?? "playFunctionThrewException"
      })
      channel.on("unhandledErrorsWhilePlaying", (errors: { message?: string }[]) => {
        state.error = errors?.[0]?.message ?? "unhandledErrorsWhilePlaying"
      })
    }
    install()
  }, READY_STATE_KEY)
}

/** Polls the state `installStoryReadySignal` records, up to `timeoutMs`. */
export async function waitForStoryReady(page: Page, timeoutMs: number): Promise<{ error: string | null }> {
  try {
    await page.waitForFunction(
      (stateKey: string) =>
        (window as unknown as Record<string, StoryReadyState | undefined>)[stateKey]?.finished === true,
      READY_STATE_KEY,
      { timeout: timeoutMs }
    )
  } catch {
    return { error: `timed out waiting for storyFinished after ${timeoutMs}ms` }
  }

  const state = await page.evaluate(
    (stateKey: string) => (window as unknown as Record<string, StoryReadyState>)[stateKey],
    READY_STATE_KEY
  )
  return { error: state.error }
}

