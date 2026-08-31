import { useMemo, type ReactNode } from "react"

import {
  SessionContext,
  type ProjectRef,
  type Session,
  type SessionUser,
} from "./session-context"

export interface SessionProviderProps {
  user: SessionUser
  projects: ProjectRef[]
  children: ReactNode
}

/**
 * Holds the shift.
 *
 * Deliberately stateless: there is no current project to remember, because a
 * project is a column and a filter rather than a mode. What a screen shows is
 * the screen's own state, and it belongs in the screen's own filters where the
 * operator can see it and clear it.
 */
export function SessionProvider({
  user,
  projects,
  children,
}: SessionProviderProps) {
  const value = useMemo<Session>(() => ({ user, projects }), [user, projects])

  return <SessionContext value={value}>{children}</SessionContext>
}
