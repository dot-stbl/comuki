// scripts/lib/storybook-ready-signal.ts
//
// Shared "is this story actually finished?" signal for a Storybook preview
// page, independent of `#storybook-root` gaining DOM children — the thing
// that breaks for a story whose content portals into `document.body`
// (react-aria-components' `Modal`/`Dialog`, and everything built on it:
// `BottomSheet`, `ConfirmDialog`, `FormDialog`, `Dialog` itself).
//
// Extracted from WS17's `ui-probe.ts` (`installStorybookReadySignal` /
// `waitForStoryReady`) so WS16's `.storybook/test-runner.ts` can reuse the
// exact same technique for its own portal-story workaround rather than a
// second hand-rolled copy — see that file's `preVisit` and
// `storybook-tests/README.md` "Portal-based stories" section.
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

/**
 * (Re)installs a minimal, working `window.__test(storyId)` — the same
 * global `@storybook/test-runner@0.23.0`'s own generated per-story Jest test
 * calls via `page.evaluate(() => __test(id))` (see its `setup-page-script.mjs`,
 * and `.storybook/test-runner.ts`'s docblock on `PORTAL_TAG`).
 *
 * A `page.goto()` full navigation wipes whatever `@storybook/test-runner`
 * itself injected there — its own `setup-page-script.mjs` blob, added via
 * `page.addScriptTag` once at the start of the file, does not survive a
 * navigation, and neither does anything else on `window`. `preVisit`
 * navigating the page directly for a portal story (see
 * `installStoryReadySignal` above) therefore leaves the *next*
 * `page.evaluate(() => __test(id))` — the harness's own, for THIS story, and
 * every later story in the same file that does not itself navigate — with
 * no `__test` to call at all (`ReferenceError`). This reinstalls it.
 *
 * Deliberately not a no-op stub: a later story in the same file that did not
 * trigger a navigation of its own still goes through this same
 * `window.__test`, so it must actually drive Storybook's real render
 * (`setCurrentStory` + wait for the finished/error event), not merely
 * report success. For the *same* story `preVisit` just pre-rendered,
 * Storybook's own preview core recognises the repeat `setCurrentStory` as a
 * no-op selection and answers with `storyUnchanged` rather than rendering
 * again (`Preview.renderSelection`'s `currentSelection`/`currentRender`
 * shortcut — verified against `@storybook/core`'s own source in
 * `node_modules`), so this resolves immediately rather than repeating
 * whatever made the first attempt (via the harness's own transition) hang.
 */
export function installTestBridge(page: Page): Promise<unknown> {
  return page.evaluate(() => {
    interface ChannelLike {
      on(event: string, listener: (...args: never[]) => void): void
      off(event: string, listener: (...args: never[]) => void): void
      emit(event: string, payload?: unknown): void
    }

    ;(window as unknown as { __test: (storyId: string) => Promise<void> }).__test = (storyId: string) =>
      new Promise<void>((resolve, reject) => {
        const channel = (window as unknown as { __STORYBOOK_ADDONS_CHANNEL__?: ChannelLike })
          .__STORYBOOK_ADDONS_CHANNEL__
        if (!channel) {
          reject(new Error("no Storybook channel on this page"))
          return
        }
        // Narrowed once into its own binding: the nested `cleanup` closure
        // below doesn't retain the `if (!channel)` guard's narrowing on the
        // outer `channel` across a function boundary.
        const activeChannel = channel

        const listeners: Record<string, (...args: never[]) => void> = {
          storyFinished: (data: { status?: string }) => {
            cleanup()
            if (data?.status === "error") {
              reject(new Error("story finished with an error"))
            } else {
              resolve()
            }
          },
          storyUnchanged: () => {
            cleanup()
            resolve()
          },
          storyMissing: (id: unknown) => {
            cleanup()
            reject(new Error(`story not found: ${String(id)}`))
          },
          storyErrored: (payload: { description?: string }) => {
            cleanup()
            reject(new Error(payload?.description ?? "storyErrored"))
          },
          storyThrewException: (error: { message?: string }) => {
            cleanup()
            reject(new Error(error?.message ?? "storyThrewException"))
          },
          playFunctionThrewException: (error: { message?: string }) => {
            cleanup()
            reject(new Error(error?.message ?? "playFunctionThrewException"))
          },
          unhandledErrorsWhilePlaying: (errors: { message?: string }[]) => {
            cleanup()
            reject(new Error(errors?.[0]?.message ?? "unhandledErrorsWhilePlaying"))
          },
        }

        function cleanup(): void {
          for (const [event, listener] of Object.entries(listeners)) {
            activeChannel.off(event, listener)
          }
        }

        for (const [event, listener] of Object.entries(listeners)) {
          activeChannel.on(event, listener)
        }
        activeChannel.emit("setCurrentStory", { storyId, viewMode: "story" })
      })
  })
}
