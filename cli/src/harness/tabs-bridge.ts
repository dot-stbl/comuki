/**
 * Bridge from `HarnessState` (the reducer's source of truth) to
 * `SessionsState` (the Ink renderer's model).
 *
 * Direction is harness → legacy: the reducer owns the focus / unread
 * invariant; this function projects that onto the legacy `tabs` state
 * while leaving presentation-only fields (`blocks`, `liveText`,
 * `runsFeed`, …) untouched, because they have no harness source in
 * step 1.
 *
 * Mirror rule (this is the migration's "deletion test" boundary):
 *   order   — `harness.sessions` order preserved
 *   active  — `harness.activeSessionId` resolves to its index in
 *             `tabs.sessions`; `-1` if none
 *   unread  — true when `harness.sessions[i].unread` is true
 *   hydrated — true when `harness.sessions[i].transcriptLoad.kind
 *              === "loaded"`
 *
 * Everything else for a given session id is kept from the incoming
 * `tabs` (so transcript blocks, recall history, last user message,
 * renames, etc. survive untouched until later steps address them).
 */
import type { Session, SessionsState } from "../lib/session-state"
import type { HarnessSession, HarnessState, SessionKey } from "./state"

function sessionKeyOf(identity: HarnessSession["identity"]): SessionKey {
  return identity.id
}

/**
 * Builds the next `SessionsState` from the latest `HarnessState` and
 * the previous tab model. Pure: no React, no Ink, no I/O.
 */
export function mirrorTabsFromHarness(
  harness: HarnessState,
  tabs: SessionsState,
): SessionsState {
  // Drop sessions that no longer exist in the harness (mirrors the
  // kernel's own close handling — the reducer already chose the
  // neighbour to focus on).
  const sessions: Session[] = []
  for (const session of harness.sessions) {
    const key = String(sessionKeyOf(session.identity))
    const previous = tabs.sessions.find((candidate) => candidate.id === key)
    sessions.push(projectSession(session, previous))
  }
  const activeIndex =
    harness.activeSessionId === null
      ? -1
      : sessions.findIndex(
          (candidate) => candidate.id === String(harness.activeSessionId),
        )
  return { sessions, activeIndex }
}

function projectSession(
  session: HarnessSession,
  previous: Session | undefined,
): Session {
  if (!previous) {
    return synthesiseNew(session)
  }
  return {
    ...previous,
    name: session.title.length > 0 ? session.title : previous.name,
    unread: session.unread,
    hydrated: session.transcriptLoad.kind === "loaded",
    // The reducer is the truth for the legacy fields it owns. Anything
    // the reducer does not model (transcript blocks, runsFeed,
    // blocksExpanded, recall history) carries over untouched.
  }
}

function synthesiseNew(session: HarnessSession): Session {
  return {
    id: String(session.identity.id),
    name: session.title.length > 0 ? session.title : "session",
    status: "idle",
    createdAt: session.createdAtUnixMs,
    unread: session.unread,
    awaitingApproval: false,
    pendingPlan: null,
    blocks: [],
    liveText: "",
    hydrated: session.transcriptLoad.kind === "loaded",
    blocksExpanded: false,
    lastUserMessage: null,
    renamed: session.renamed,
    history: session.history ?? [],
  }
}
