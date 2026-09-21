/**
 * Reconnect orchestrator (issue #77).
 *
 * Owns the **transport-level** connection state separate from the
 * kernel's `connection` field (which tracks the realtime hub). The
 * orchestrator emits three coarse states — Offline, Recovering,
 * Online — and lets the kernel know when mutating sends are
 * allowed. While Offline or Recovering, `dispatch()` rejects
 * mutating intents with `{ ok: false, reason: "offline", draft }`
 * so the host can show "draft retained" without losing the user's
 * typed text.
 *
 * The orchestrator does not own the realtime hub itself — that
 * lives in the feed adapter. It is the kernel-side counterpart:
 * when the feed reports `connection-lost`, the kernel calls
 * `reconnect.markOffline()`. When the feed reports `connection-
 * established` (reconnect succeeded), the kernel calls
 * `reconnect.markOnline()`. During the gap, `reconnect.mark-
 * Recovering()` lets the host show "recovering" without blocking
 * dispatches permanently.
 *
 * Listeners match the `addEventListener` shape used elsewhere
 * (snapshot + subscribe); they fire on every transition.
 */
import type { Unsubscribe } from "./kernel"

// ---------------------------------------------------------------------------
// Reconnect state — the orchestrator's typed view-model
// ---------------------------------------------------------------------------

export type ReconnectState = "offline" | "recovering" | "online"

/** The events the orchestrator emits. */
export type ReconnectEvent = ReconnectState

export type ReconnectListener = (state: ReconnectState) => void

/**
 * The orchestrator's public surface. The kernel owns one instance
 * per process; the host reads its state for badges and the kernel
 * checks it before allowing mutating dispatches.
 */
export interface ReconnectOrchestrator {
  /** The current coarse connection state. */
  state(): ReconnectState

  /** Subscribe to transitions; matches `addEventListener` shape. */
  addEventListener(listener: ReconnectListener): Unsubscribe

  /** Mark the transport offline (the realtime hub dropped). */
  markOffline(): void

  /** Mark the transport mid-reconnect (we are trying to come back). */
  markRecovering(): void

  /** Mark the transport online (a reconnect succeeded). */
  markOnline(): void

  /**
   * Whether mutating sends should be allowed right now. `true`
   * only when the state is `online`; false otherwise.
   */
  canDispatch(): boolean

  /** Tear down listeners. Idempotent. */
  destroy(): void
}

// ---------------------------------------------------------------------------
// Factory
// ---------------------------------------------------------------------------

/**
 * Build a fresh orchestrator. The state starts `offline` — the
 * kernel flips it to `online` once the feed's first
 * `connection-established` event arrives. The transitions are
 * idempotent: setting the same state twice is a no-op (no listener
 * fires).
 */
export function createReconnectOrchestrator(): ReconnectOrchestrator {
  let current: ReconnectState = "offline"
  const listeners = new Set<ReconnectListener>()

  function transition(next: ReconnectState): void {
    if (current === next) {
      return
    }
    current = next
    for (const listener of [...listeners]) {
      listener(current)
    }
  }

  return {
    state() {
      return current
    },
    addEventListener(listener) {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    markOffline() {
      transition("offline")
    },
    markRecovering() {
      transition("recovering")
    },
    markOnline() {
      transition("online")
    },
    canDispatch() {
      return current === "online"
    },
    destroy() {
      listeners.clear()
    },
  }
}
