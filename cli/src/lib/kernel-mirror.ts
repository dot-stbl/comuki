/**
 * One-way mirror from ClientKernel snapshots into the legacy
 * `SessionsState` tab model — the seam of the #83 strangler cut.
 *
 * The kernel owns the crown flow (bootstrap, open/adopt, submit,
 * stream, completion, approval, reconnect, close); the legacy tab
 * state stays the render model for everything else. This module is
 * pure: snapshot in, next tab state + mirror memory out. It never
 * dispatches intents and never mutates the inputs.
 *
 * Memory rules:
 * - transcript growth is tracked per kernel session id, so replayed
 *   or duplicated snapshots never append blocks twice;
 * - adoption (pending → remote id swap) rebinds the legacy tab id in
 *   place — the kernel preserves list order through adoption;
 * - a kernel-owned session that disappears closes the local tab; a
 *   legacy-only tab (e.g. a /branch fork) is never touched.
 */
import type {
  CliError,
  HarnessState,
  HarnessMessage,
} from "../harness/state"
import { PENDING_PREFIX, adoptServerId, type ChatBlock } from "./session-state"
import {
  newPendingSession,
  type Session,
  type SessionsState,
} from "./session-state"

function turnKindOf(
  session: HarnessState["sessions"][number]
): MirroredSessionState["lastTurnKind"] {
  return session.turn.kind === "idle" ||
    session.turn.kind === "thinking" ||
    session.turn.kind === "awaiting-approval" ||
    session.turn.kind === "failed"
    ? session.turn.kind
    : "idle"
}

export interface MirroredSessionState {
  readonly transcriptLength: number
  readonly lastTurnKind: "idle" | "thinking" | "awaiting-approval" | "failed"
  readonly tabCreated: boolean
}

export interface MirrorMemory {
  /** kernel session id → what the mirror already applied. */
  readonly known: ReadonlyMap<string, MirroredSessionState>
  /** kernel id → legacy tab id (rebound on adoption). */
  readonly bindings: ReadonlyMap<string, string>
}

export interface MirrorResult {
  readonly tabs: SessionsState
  readonly memory: MirrorMemory
  /** Titles of sessions whose turn finished on this pass (bell/toast). */
  readonly completedTitles: readonly string[]
  /** Legacy tab ids whose in-flight turn was aborted (/stop mark). */
  readonly stoppedSessionIds: readonly string[]
  /** Turns that ended in failure — the renderer prints alert lines. */
  readonly failedTurns: readonly {
    readonly tabId: string
    readonly error: CliError
  }[]
}

export function emptyMirrorMemory(): MirrorMemory {
  return { known: new Map(), bindings: new Map() }
}

function messageBlock(
  message: HarnessMessage,
  key: string,
  now: () => number
): ChatBlock {
  return {
    kind: "message",
    key,
    message:
      message.view ?? {
        id: message.id,
        role: message.role,
        content: message.content,
        toolName: null,
        parts: null,
        meta: null,
        // `??` (not `||`): a zero timestamp is a value, not "missing".
        createdAt: new Date(message.createdAtUnixMs ?? now()).toISOString(),
      },
  }
}

function turnStatusOf(
  session: HarnessState["sessions"][number]
): Pick<Session, "status" | "liveText" | "awaitingApproval"> {
  switch (session.turn.kind) {
    case "thinking":
      return {
        status: "thinking",
        liveText: session.turn.accumulatedText,
        awaitingApproval: false,
      }
    case "awaiting-approval":
      return { status: "done", liveText: "", awaitingApproval: true }
    case "failed":
      return {
        status: session.turn.error.kind === "aborted" ? "done" : "idle",
        liveText: "",
        awaitingApproval: false,
      }
    default:
      return { status: "done", liveText: "", awaitingApproval: false }
  }
}

export interface MirrorOptions {
  /**
   * Clock for synthesized message timestamps. The mirror is pure:
   * the clock is injected so tests get deterministic output. Defaults
   * to `Date.now` at the call site's discretion.
   */
  readonly now?: () => number
}

export function mirrorKernelSnapshot(
  tabs: SessionsState,
  state: HarnessState,
  memory: MirrorMemory,
  options: MirrorOptions = {}
): MirrorResult {
  const now = options.now ?? (() => Date.now())
  let sessions = tabs.sessions
  let changed = false
  const known = new Map(memory.known)
  const bindings = new Map(memory.bindings)
  const completedTitles: string[] = []
  const stoppedSessionIds: string[] = []
  const failedTurns: { tabId: string; error: CliError }[] = []

  const kernelIds = new Set<string>(
    state.sessions.map((session) => String(session.identity.id))
  )

  // -- adoption rebinding -----------------------------------------------
  // A pending id that vanished while its legacy tab still exists and
  // a new remote id appeared: the kernel adopted the server session.
  // (Bindings for vanished ids are NOT deleted here — the removals
  // pass below owns that, after adoption has re-bound survivors.)
  const lostPendingIds: string[] = []
  for (const kernelId of memory.bindings.keys()) {
    if (kernelIds.has(kernelId)) {
      continue
    }
    const legacyId = memory.bindings.get(kernelId) ?? kernelId
    const tab = sessions.find((candidate) => candidate.id === legacyId)
    if (tab && tab.id.startsWith(PENDING_PREFIX)) {
      lostPendingIds.push(tab.id)
    }
  }
  const freshRemoteIds = state.sessions
    .filter(
      (session) =>
        session.identity.kind === "remote" && !memory.bindings.has(session.identity.id)
    )
    .map((session) => String(session.identity.id))
  while (lostPendingIds.length > 0 && freshRemoteIds.length > 0) {
    const legacyId = lostPendingIds.shift() as string
    const kernelId = freshRemoteIds.shift() as string
    const kernelSession = state.sessions.find(
      (candidate) => String(candidate.identity.id) === kernelId
    )
    // The legacy tab id swaps to the server id — from here on the
    // binding is the identity.
    sessions = adoptServerId(
      { sessions, activeIndex: tabs.activeIndex },
      legacyId,
      kernelId,
      kernelSession?.title?.length ? kernelSession.title : "session"
    ).sessions
    bindings.set(kernelId, kernelId)
    changed = true
  }

  // -- per-session reconcile ---------------------------------------------
  for (const session of state.sessions) {
    const kernelId = String(session.identity.id)
    let legacyId = bindings.get(kernelId)
    if (legacyId === undefined || !sessions.some((tab) => tab.id === legacyId)) {
      // First sighting (or the local tab vanished under us) — create,
      // then fall through so status/transcript apply immediately.
      if (legacyId !== undefined) {
        bindings.delete(kernelId)
      }
      sessions = [...sessions, createTabFor(kernelId, session)]
      legacyId = kernelId
      bindings.set(kernelId, legacyId)
      changed = true
    }
    const previous = known.get(kernelId)
    const status = turnStatusOf(session)
    const newMessages = session.transcript.slice(previous?.transcriptLength ?? 0)
    const tab = sessions.find((candidate) => candidate.id === legacyId) as Session
    let updated: Session = tab
    let sessionChanged = false
    const apply = (patch: Partial<Session>) => {
      updated = { ...updated, ...patch }
      sessionChanged = true
    }

    if (
      previous?.lastTurnKind === "thinking" &&
      session.turn.kind !== "thinking"
    ) {
      completedTitles.push(session.title || updated.name)
      if (session.turn.kind === "failed") {
        failedTurns.push({ tabId: updated.id, error: session.turn.error })
        if (session.turn.error.kind === "aborted") {
          stoppedSessionIds.push(updated.id)
        }
      }
    }

    if (updated.status !== status.status || updated.liveText !== status.liveText) {
      apply(status)
    }
    if (updated.unread !== session.unread) {
      apply({ unread: session.unread })
    }
    if (updated.awaitingApproval !== status.awaitingApproval) {
      apply({ awaitingApproval: status.awaitingApproval })
    }
    if (updated.pendingPlan !== (session.pendingPlan ?? null)) {
      apply({ pendingPlan: session.pendingPlan ?? null })
    }
    if (session.title.length > 0 && updated.name !== session.title) {
      apply({ name: session.title, renamed: session.renamed })
    }
    const nextLastUser = session.lastUserMessage ?? null
    if (nextLastUser !== null && updated.lastUserMessage !== nextLastUser) {
      apply({ lastUserMessage: nextLastUser })
    }
    const nextHistory = session.history ?? []
    if (
      nextHistory.length > 0 &&
      (updated.history ?? []).length !== nextHistory.length
    ) {
      apply({ history: nextHistory })
    }
    const queued = session.queue.map((turn) => turn.message)
    if ((updated.queued ?? []).join("\u0000") !== queued.join("\u0000")) {
      apply({ queued })
    }

    if (newMessages.length > 0) {
      const startIndex = updated.blocks.length
      updated = {
        ...updated,
        blocks: [
          ...updated.blocks,
          ...newMessages.map((message, index) =>
            messageBlock(
              message,
              `${updated.id}-k${startIndex + index}`,
              now
            )
          ),
        ],
      }
      sessionChanged = true
    }

    if (sessionChanged) {
      const targetId = updated.id
      sessions = sessions.map((tabCandidate) =>
        tabCandidate.id === targetId ? updated : tabCandidate
      )
      changed = true
    }
    known.set(kernelId, {
      transcriptLength: session.transcript.length,
      lastTurnKind: turnKindOf(session),
      tabCreated: true,
    })
  }

  // -- removals ------------------------------------------------------------
  for (const [kernelId, legacyId] of [...bindings.entries()]) {
    if (!kernelIds.has(kernelId)) {
      if (sessions.some((tab) => tab.id === legacyId)) {
        sessions = sessions.filter((tab) => tab.id !== legacyId)
        changed = true
      }
      bindings.delete(kernelId)
      known.delete(kernelId)
    }
  }

  // -- active tab ------------------------------------------------------------
  let activeIndex = sessions.length === 0 ? -1 : tabs.activeIndex
  if (state.activeSessionId !== null) {
    const index = sessions.findIndex(
      (tab) =>
        tab.id === (bindings.get(state.activeSessionId ?? "") ?? state.activeSessionId)
    )
    if (index >= 0 && index !== activeIndex) {
      activeIndex = index
      changed = true
    }
  }

  if (!changed) {
    return {
      tabs,
      memory,
      completedTitles: [],
      stoppedSessionIds: [],
      failedTurns: [],
    }
  }
  return {
    tabs: { sessions, activeIndex },
    memory: { known, bindings },
    completedTitles,
    stoppedSessionIds,
    failedTurns,
  }
}

function createTabFor(
  kernelId: string,
  session: HarnessState["sessions"][number]
): Session {
  if (session.identity.kind === "pending") {
    return {
      ...newPendingSession(session.createdAtUnixMs, 0),
      id: kernelId,
    }
  }
  return {
    ...newPendingSession(session.createdAtUnixMs, 0),
    id: kernelId,
    name: session.title.length > 0 ? session.title : "session",
    status: "done",
    renamed: session.renamed,
    hydrated: false,
    history: [...(session.history ?? [])],
  }
}
