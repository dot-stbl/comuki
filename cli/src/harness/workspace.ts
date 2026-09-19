/**
 * ClientWorkspace — the one real client-owned aggregate (issue #83).
 *
 * Everything here is what the CLI owns and must survive a restart:
 * open session references (pending → remote, one-directional — the
 * reducer only ever adopts, never demotes), the active session,
 * recoverable per-session drafts, durable stream cursors
 * (`lastSeenAt`), and the explicitly-safe outbound command log used
 * for idempotency. Server-owned chat/approval/run state stays in
 * projections and is never persisted as workspace truth.
 *
 * The durable form is the versioned `WorkspaceDocument` (version 2).
 * Documents written by the previous CLI shape — the flat
 * `sessions.json` with `{ id, name, status, createdAt, … }` entries
 * and no version field — migrate forward losslessly; user files never
 * break. Pure data + pure functions only: no Ink, no React, no I/O.
 */
import { isJsonObject } from "../lib/json"
import type { HarnessSession, HarnessState, SessionKey } from "./state"
import { pendingSessionId, sessionId, turnRequestId } from "./state"

export const WORKSPACE_DOCUMENT_VERSION = 2

export type WorkspaceLastStatus =
  | "idle"
  | "thinking"
  | "awaiting-approval"
  | "failed"
  | "done"

/** A durable open-session reference (pending or remote). */
export interface WorkspaceSessionRef {
  /** `local-…` while pending, the server UUID once adopted. */
  readonly id: string
  readonly title: string
  readonly renamed: boolean
  readonly createdAtUnixMs: number
  readonly lastStatus: WorkspaceLastStatus
  readonly history: readonly string[]
}

export interface WorkspaceDraft {
  readonly sessionId: string
  readonly text: string
  readonly updatedAtUnixMs: number
}

export interface WorkspaceOutboundCommand {
  readonly commandId: string
  readonly requestId: string
  readonly sessionId: string
  readonly kind: "turn" | "approval"
  readonly message: string
  readonly state: "queued" | "in-flight" | "settled"
}

/** The client-owned aggregate, extracted from kernel state. */
export interface ClientWorkspace {
  readonly activeSessionId: SessionKey | null
  readonly sessions: readonly WorkspaceSessionRef[]
  readonly drafts: readonly WorkspaceDraft[]
  readonly cursors: Readonly<Record<string, number>>
  readonly outbound: readonly WorkspaceOutboundCommand[]
}

/** The durable on-disk form. Versioned; v1 is the legacy sessions doc. */
export interface WorkspaceDocument {
  readonly version: typeof WORKSPACE_DOCUMENT_VERSION
  readonly activeSessionId: string | null
  readonly sessions: readonly WorkspaceSessionRef[]
  readonly drafts: readonly WorkspaceDraft[]
  readonly cursors: Readonly<Record<string, number>>
  readonly outbound: readonly WorkspaceOutboundCommand[]
}

/** Settled outbound commands kept in the document — enough for
 * idempotency after a restart, small enough to stay cheap. */
const MAX_PERSISTED_OUTBOUND = 50

function lastStatusOf(session: HarnessSession): WorkspaceLastStatus {
  if (session.turn.kind === "thinking") {
    return "thinking"
  }
  if (session.turn.kind === "awaiting-approval") {
    return "awaiting-approval"
  }
  if (session.turn.kind === "failed") {
    return "failed"
  }
  if (session.transcript.length > 0 || session.history?.length) {
    return "done"
  }
  return "idle"
}

/** Extracts the client-owned aggregate from kernel state. */
export function workspaceFromState(state: HarnessState): ClientWorkspace {
  return {
    activeSessionId: state.activeSessionId,
    sessions: state.sessions.map((session) => ({
      id: session.identity.id,
      title: session.title,
      renamed: session.renamed,
      createdAtUnixMs: session.createdAtUnixMs,
      lastStatus: lastStatusOf(session),
      history: session.history ?? [],
    })),
    drafts: state.drafts,
    cursors: state.cursors,
    outbound: state.outbound.map((command) => ({
      commandId: command.commandId,
      requestId: command.requestId,
      sessionId: command.sessionId,
      kind: command.kind,
      message: command.message,
      state: command.state,
    })),
  }
}

/** Builds the durable document from kernel state. */
export function workspaceDocumentFromState(
  state: HarnessState
): WorkspaceDocument {
  const workspace = workspaceFromState(state)
  return {
    ...workspace,
    version: WORKSPACE_DOCUMENT_VERSION,
    activeSessionId: workspace.activeSessionId,
    outbound: workspace.outbound.slice(-MAX_PERSISTED_OUTBOUND),
  }
}

/**
 * Restores kernel state from the aggregate. Restored sessions restart
 * idle (no turn is actually in flight after a restart) with
 * not-loaded transcripts — lazy hydration refills them.
 */
export function applyWorkspaceToState(
  state: HarnessState,
  workspace: ClientWorkspace
): HarnessState {
  const sessions: readonly HarnessSession[] = workspace.sessions.map((ref) => ({
    identity: ref.id.startsWith("local-")
      ? { kind: "pending" as const, id: pendingSessionId(ref.id) }
      : { kind: "remote" as const, id: sessionId(ref.id) },
    projectId: null,
    title: ref.title,
    createdAtUnixMs: ref.createdAtUnixMs,
    renamed: ref.renamed,
    unread: false,
    turn: { kind: "idle" as const },
    transcriptLoad: { kind: "not-loaded" as const },
    transcript: [],
    queue: [],
    history: ref.history,
    lastUserMessage: null,
    pendingPlan: null,
  }))
  const active =
    workspace.activeSessionId ??
    (sessions.length > 0 ? sessions[0]?.identity.id ?? null : null)
  return {
    ...state,
    sessions,
    activeSessionId: active,
    drafts: workspace.drafts.map((draft) => ({
      sessionId: sessionKeyOf(draft.sessionId),
      text: draft.text,
      updatedAtUnixMs: draft.updatedAtUnixMs,
    })),
    cursors: workspace.cursors,
    outbound: workspace.outbound.map((command) => ({
      commandId: command.commandId,
      requestId: turnRequestId(command.requestId),
      sessionId: sessionKeyOf(command.sessionId),
      kind: command.kind,
      message: command.message,
      // A restart voids in-flight wire state — commands that never
      // settled resume as settled (the user re-sends manually).
      state: "settled" as const,
    })),
  }
}

function sessionKeyOf(value: string): SessionKey {
  return value.startsWith("local-")
    ? pendingSessionId(value)
    : sessionId(value)
}

// ---------------------------------------------------------------------------
// Decoding + migration (v1 legacy sessions.json → v2 workspace document)
// ---------------------------------------------------------------------------

const LAST_STATUSES: readonly WorkspaceLastStatus[] = [
  "idle",
  "thinking",
  "awaiting-approval",
  "failed",
  "done",
]

function isLastStatus(value: unknown): value is WorkspaceLastStatus {
  return (
    typeof value === "string" &&
    LAST_STATUSES.some((status) => status === value)
  )
}

function stringArray(value: unknown): readonly string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string")
    : []
}

function numberMap(value: unknown): Readonly<Record<string, number>> {
  if (!isJsonObject(value)) {
    return {}
  }
  const cursors: Record<string, number> = {}
  for (const [key, entry] of Object.entries(value)) {
    if (typeof entry === "number" && Number.isFinite(entry)) {
      cursors[key] = entry
    }
  }
  return cursors
}

function decodeSessionRef(entry: unknown): WorkspaceSessionRef | null {
  if (!isJsonObject(entry) || typeof entry.id !== "string" || entry.id.length === 0) {
    return null
  }
  // v2 writes `title`; the legacy document wrote `name`.
  const rawTitle =
    typeof entry.title === "string" && entry.title.length > 0
      ? entry.title
      : typeof entry.name === "string"
        ? entry.name
        : ""
  return {
    id: entry.id,
    title: rawTitle.length > 0 ? rawTitle : "session",
    renamed: entry.renamed === true,
    createdAtUnixMs:
      typeof entry.createdAtUnixMs === "number" &&
      Number.isFinite(entry.createdAtUnixMs)
        ? entry.createdAtUnixMs
        : typeof entry.createdAt === "number" && Number.isFinite(entry.createdAt)
          ? entry.createdAt
          : 0,
    lastStatus: isLastStatus(entry.lastStatus)
      ? entry.lastStatus
      : legacyStatus(entry.status),
    history: stringArray(entry.history),
  }
}

/** The legacy doc persisted UI statuses; map them onto last-status. */
function legacyStatus(value: unknown): WorkspaceLastStatus {
  if (value === "thinking" || value === "running") {
    return "thinking"
  }
  if (value === "done") {
    return "done"
  }
  return "idle"
}

function decodeDraft(entry: unknown): WorkspaceDraft | null {
  if (
    !isJsonObject(entry) ||
    typeof entry.sessionId !== "string" ||
    typeof entry.text !== "string"
  ) {
    return null
  }
  return {
    sessionId: entry.sessionId,
    text: entry.text,
    updatedAtUnixMs:
      typeof entry.updatedAtUnixMs === "number" &&
      Number.isFinite(entry.updatedAtUnixMs)
        ? entry.updatedAtUnixMs
        : 0,
  }
}

function decodeOutbound(entry: unknown): WorkspaceOutboundCommand | null {
  if (
    !isJsonObject(entry) ||
    typeof entry.commandId !== "string" ||
    typeof entry.requestId !== "string" ||
    typeof entry.sessionId !== "string" ||
    (entry.kind !== "turn" && entry.kind !== "approval")
  ) {
    return null
  }
  return {
    commandId: entry.commandId,
    requestId: entry.requestId,
    sessionId: entry.sessionId,
    kind: entry.kind,
    message: typeof entry.message === "string" ? entry.message : "",
    state:
      entry.state === "queued" || entry.state === "in-flight"
        ? "queued"
        : "settled",
  }
}

const EMPTY_WORKSPACE: ClientWorkspace = {
  activeSessionId: null,
  sessions: [],
  drafts: [],
  cursors: {},
  outbound: [],
}

/**
 * Decodes an unknown on-disk document into the workspace aggregate.
 * Version 2 documents decode directly; version-1 (legacy) sessions
 * documents — `{ activeSessionId?, sessions: [{ id, name, status,
 * createdAt, renamed?, history? }] }` with no version field — migrate
 * forward losslessly. Unrecognised shapes yield the empty workspace,
 * never a throw: a corrupt file must not brick the CLI.
 */
export function migrateWorkspaceDocument(value: unknown): ClientWorkspace {
  if (!isJsonObject(value) || !Array.isArray(value.sessions)) {
    return EMPTY_WORKSPACE
  }

  const sessions = value.sessions.flatMap((entry): readonly WorkspaceSessionRef[] => {
    const ref = decodeSessionRef(entry)
    return ref ? [ref] : []
  })

  if (value.version === WORKSPACE_DOCUMENT_VERSION) {
    return {
      activeSessionId:
        typeof value.activeSessionId === "string"
          ? sessionKeyOf(value.activeSessionId)
          : sessions[0]?.id !== undefined
            ? sessionKeyOf(sessions[0].id)
            : null,
      sessions,
      drafts: Array.isArray(value.drafts)
        ? value.drafts.flatMap((entry) => {
            const draft = decodeDraft(entry)
            return draft ? [draft] : []
          })
        : [],
      cursors: numberMap(value.cursors),
      outbound: Array.isArray(value.outbound)
        ? value.outbound.flatMap((entry) => {
            const command = decodeOutbound(entry)
            return command ? [command] : []
          })
        : [],
    }
  }

  // Legacy v1: names map to titles, UI status maps to last-status,
  // drafts/cursors/outbound did not exist.
  return {
    activeSessionId:
      typeof value.activeSessionId === "string"
        ? sessionKeyOf(value.activeSessionId)
        : sessions[0]?.id !== undefined
          ? sessionKeyOf(sessions[0].id)
          : null,
    sessions,
    drafts: [],
    cursors: {},
    outbound: [],
  }
}

