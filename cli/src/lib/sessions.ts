/**
 * Multi-session state model: N parallel brain sessions switched like
 * browser tabs. Everything here is pure data + pure functions — the
 * React shell in `commands/chat.tsx` only dispatches and renders, so
 * create/switch/close/unread logic is testable without Ink.
 *
 * A tab is either *pending* (id `local-…`, no server session yet — free
 * until the first message) or *live* (server UUID; turns, hub groups and
 * `sessions.json` restore all address it). Closing a tab never touches
 * the server — the task keeps running there.
 */
import type { ChatMessageView } from "./client"
import { readJsonFile, writeJsonFile } from "./json"
import { sessionsFilePath } from "./config"

export type SessionStatus = "idle" | "thinking" | "running" | "done"

export interface MessageBlock {
  readonly kind: "message"
  readonly key: string
  readonly message: ChatMessageView
}

export interface LinesBlock {
  readonly kind: "lines"
  readonly key: string
  readonly lines: readonly string[]
}

export type ChatBlock = MessageBlock | LinesBlock

export interface Session {
  readonly id: string
  /** User-defined or auto from the first message (first 20 chars). */
  readonly name: string
  readonly status: SessionStatus
  readonly createdAt: number
  /** Background tab received output the user has not seen yet. */
  readonly unread: boolean
  readonly awaitingApproval: boolean
  readonly pendingPlan: unknown
  readonly blocks: readonly ChatBlock[]
  /** Tail of the live chunk stream while a turn is in flight. */
  readonly liveText: string
  /** Transcript fetched from the server (restored tabs start false). */
  readonly hydrated: boolean
}

export const PENDING_PREFIX = "local-"

// ---------------------------------------------------------------------------
// Pure state operations
// ---------------------------------------------------------------------------

/** Tab label from the first user message: collapsed whitespace, ≤20 chars. */
export function sessionNameFromMessage(message: string): string {
  const collapsed = message.replace(/\s+/g, " ").trim()
  if (collapsed.length === 0) {
    return "session"
  }
  return collapsed.length <= 20 ? collapsed : collapsed.slice(0, 20)
}

/** A fresh pending tab — no server session until the first message. */
export function newPendingSession(
  createdAt: number = Date.now(),
  seq: number = 0
): Session {
  return {
    id: `${PENDING_PREFIX}${createdAt}-${seq}`,
    name: "new",
    status: "idle",
    createdAt,
    unread: false,
    awaitingApproval: false,
    pendingPlan: null,
    blocks: [],
    liveText: "",
    hydrated: false,
  }
}

export interface SessionsState {
  readonly sessions: readonly Session[]
  readonly activeIndex: number
}

/** Appends a tab and activates it. */
export function addSession(
  state: SessionsState,
  session: Session
): SessionsState {
  return {
    sessions: [...state.sessions, session],
    activeIndex: state.sessions.length,
  }
}

/**
 * Removes the tab at `index`; the neighbour to the right (or the last
 * tab when the closed one was rightmost) takes the focus. Empty list →
 * activeIndex −1.
 */
export function removeSession(
  state: SessionsState,
  index: number
): SessionsState {
  if (index < 0 || index >= state.sessions.length) {
    return state
  }
  const sessions = state.sessions.filter((_, current) => current !== index)
  const activeIndex =
    sessions.length === 0 ? -1 : Math.min(index, sessions.length - 1)
  return { sessions, activeIndex }
}

/** next/previous tab with wrap-around; `count === 0` → −1. */
export function stepActive(
  count: number,
  activeIndex: number,
  delta: 1 | -1
): number {
  if (count <= 0) {
    return -1
  }
  return (activeIndex + delta + count) % count
}

/** Shallow-merges `patch` into the session with id `id`; unknown id → as-is. */
export function patchSession(
  sessions: readonly Session[],
  id: string,
  patch: Partial<Omit<Session, "id" | "blocks">>
): readonly Session[] {
  return sessions.map((session) =>
    session.id === id ? { ...session, ...patch } : session
  )
}

/** Appends transcript blocks to a session, keys from a per-session counter. */
export function appendBlocks(
  sessions: readonly Session[],
  id: string,
  blocks:
    | readonly { readonly kind: "message"; readonly message: ChatMessageView }[]
    | readonly { readonly kind: "lines"; readonly lines: readonly string[] }[]
): readonly Session[] {
  return sessions.map((session) => {
    if (session.id !== id) {
      return session
    }
    const start = session.blocks.length
    return {
      ...session,
      blocks: [
        ...session.blocks,
        ...blocks.map((block, index) =>
          block.kind === "message"
            ? {
                kind: "message" as const,
                key: `${id}-b${start + index}`,
                message: block.message,
              }
            : {
                kind: "lines" as const,
                key: `${id}-b${start + index}`,
                lines: block.lines,
              }
        ),
      ],
    }
  })
}

/** Replaces a session's transcript wholesale (lazy hydration on restore). */
export function setBlocks(
  sessions: readonly Session[],
  id: string,
  blocks: readonly ChatBlock[]
): readonly Session[] {
  return sessions.map((session) =>
    session.id === id ? { ...session, blocks } : session
  )
}

/** Appends streamed chunk text to a session's live tail (capped at 4000). */
export function appendLiveText(
  sessions: readonly Session[],
  id: string,
  text: string
): readonly Session[] {
  return sessions.map((session) =>
    session.id === id
      ? { ...session, liveText: (session.liveText + text).slice(-4000) }
      : session
  )
}

/** Sets the unread dot on a background tab (`id !== activeId`). */
export function markUnread(
  sessions: readonly Session[],
  id: string,
  activeId: string | undefined
): readonly Session[] {
  if (id === activeId) {
    return sessions
  }
  return sessions.map((session) =>
    session.id === id ? { ...session, unread: true } : session
  )
}

/** Swaps a pending tab's local id for the server session UUID. */
export function adoptServerId(
  state: SessionsState,
  localId: string,
  serverId: string,
  name: string
): SessionsState {
  return {
    sessions: state.sessions.map((session) =>
      session.id === localId ? { ...session, id: serverId, name } : session
    ),
    activeIndex: state.activeIndex,
  }
}

// ---------------------------------------------------------------------------
// Persistence — `~/.config/comuki/sessions.json`
// ---------------------------------------------------------------------------

export interface PersistedSession {
  readonly id: string
  readonly name: string
  readonly status: SessionStatus
  readonly createdAt: number
}

export interface PersistedSessions {
  readonly activeSessionId?: string
  readonly sessions: readonly PersistedSession[]
}

/** Strips transcripts down to what restore needs (server tabs only). */
export function toPersisted(state: SessionsState): PersistedSessions {
  const persisted = state.sessions
    .filter((session) => !session.id.startsWith(PENDING_PREFIX))
    .map((session) => ({
      id: session.id,
      name: session.name,
      status: session.status,
      createdAt: session.createdAt,
    }))
  const active = state.sessions[state.activeIndex]
  return {
    ...(active && !active.id.startsWith(PENDING_PREFIX)
      ? { activeSessionId: active.id }
      : {}),
    sessions: persisted,
  }
}

/** Rebuilds tab state from disk; every restored tab awaits lazy hydration. */
export function fromPersisted(persisted: PersistedSessions): SessionsState {
  const sessions: Session[] = persisted.sessions
    .filter(
      (session) => typeof session.id === "string" && session.id.length > 0
    )
    .map((session) => ({
      id: session.id,
      name: session.name || "session",
      status: session.status,
      createdAt: session.createdAt,
      unread: false,
      awaitingApproval: false,
      pendingPlan: null,
      blocks: [],
      liveText: "",
      hydrated: false,
    }))
  const activeIndex = Math.max(
    0,
    sessions.findIndex((session) => session.id === persisted.activeSessionId)
  )
  return { sessions, activeIndex: sessions.length > 0 ? activeIndex : -1 }
}

export async function readSessionsFile(
  path: string = sessionsFilePath()
): Promise<PersistedSessions> {
  const contents = await readJsonFile<Partial<PersistedSessions>>(path)
  if (!contents || !Array.isArray(contents.sessions)) {
    return { sessions: [] }
  }
  return { sessions: contents.sessions } as PersistedSessions
}

export async function writeSessionsFile(
  state: SessionsState,
  path: string = sessionsFilePath()
): Promise<void> {
  await writeJsonFile(path, toPersisted(state))
}
