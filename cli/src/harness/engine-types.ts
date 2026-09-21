/**
 * The `HarnessEngineInterface` — the renderer-facing surface of the new
 * harness engine (the `reduceHarness → runEffect → ports` triad). The
 * single Ink component (`commands/chat.tsx`) talks to the engine only
 * through this interface; it never imports `reduceHarness`,
 * `runEffect`, or `HarnessState` directly in step 1 beyond reading the
 * state shape for the bridge subscriber.
 *
 * Scope: this is the engine introduced by HarnessEngine migration
 * step 1. Only `session-focused` and `session-closed` flow through it
 * today; everything else still flows through the existing
 * `ClientKernel`. Future steps add more events.
 */
import type { HarnessEvent } from "./events"
import type { HarnessState } from "./state"

/** Listener that fires once per `dispatch` after effects settle. */
export type HarnessListener = (state: HarnessState) => void

export interface HarnessEngineInterface {
  /** Fire one reducer event; run resulting effects through the port
   *  layer; confirmation events (`sessions-persisted`,
   *  `subscriptions-set`, …) feed back through `dispatch` so the
   *  listener receives the post-effect state. */
  dispatch(event: HarnessEvent): Promise<void>

  /** Push-subscribe to state. Returns the unsubscribe function. */
  subscribe(listener: HarnessListener): () => void

  /** Sync accessor — same shape selectors read. */
  readonly snapshot: HarnessState
}
