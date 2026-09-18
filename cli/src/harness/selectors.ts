import type {
  HarnessSession,
  HarnessState,
  SessionKey,
  TurnState,
} from "./state"

export interface SessionSwitcherItem {
  readonly id: SessionKey
  readonly title: string
  readonly active: boolean
  readonly unread: boolean
  readonly busy: boolean
  readonly pending: boolean
}

export interface CompactHeaderModel {
  readonly sessionId: SessionKey | null
  readonly title: string | null
  readonly connection: HarnessState["connection"]["kind"]
  readonly auth: HarnessState["auth"]["kind"]
  readonly busy: boolean
  readonly attentionCount: number
}

export function activeSession(state: HarnessState): HarnessSession | null {
  return (
    state.sessions.find(
      (session) => session.identity.id === state.activeSessionId
    ) ?? null
  )
}

export function attentionCount(state: HarnessState): number {
  return state.sessions.filter(
    (session) => session.unread || session.turn.kind === "awaiting-approval"
  ).length
}

export function isBusy(state: HarnessState): boolean {
  return state.sessions.some((session) => isBusyTurn(session.turn))
}

export function sessionSwitcherItems(
  state: HarnessState
): readonly SessionSwitcherItem[] {
  return state.sessions.map((session) => ({
    id: session.identity.id,
    title: session.title,
    active: session.identity.id === state.activeSessionId,
    unread: session.unread,
    busy: isBusyTurn(session.turn),
    pending: session.identity.kind === "pending",
  }))
}

export function contextualWorkbenchVisible(state: HarnessState): boolean {
  const session = activeSession(state)
  return (
    state.overlay.kind === "workbench" &&
    session !== null &&
    state.overlay.sessionId === session.identity.id
  )
}

export function compactHeaderModel(state: HarnessState): CompactHeaderModel {
  const session = activeSession(state)
  return {
    sessionId: session?.identity.id ?? null,
    title: session?.title ?? null,
    connection: state.connection.kind,
    auth: state.auth.kind,
    busy: session ? isBusyTurn(session.turn) : false,
    attentionCount: attentionCount(state),
  }
}

function isBusyTurn(turn: TurnState): boolean {
  return turn.kind === "thinking" || turn.kind === "awaiting-approval"
}
