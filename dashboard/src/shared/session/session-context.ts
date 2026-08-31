import { createContext, useContext, useMemo } from "react"

import {
  needsLabel,
  permissionScope,
  roleGrants,
  type Permission,
  type Role,
} from "./permissions"

export interface ProjectRef {
  id: string
  /** The short handle the operator types and reads — `comuki`, `plexor`. */
  key: string
  name: string
}

export interface SessionUser {
  id: string
  name: string
  email: string
  /** Roles that hold on every project, and the only ones platform acts read. */
  platformRoles: Role[]
  /** Roles granted on a single project, by project id. */
  projectRoles: Record<string, Role[]>
}

/**
 * The signed-in shift.
 *
 * There is no "current project" here, and that absence is the design: the duty
 * engineer watches the whole swarm at once, so a project is an attribute of a
 * row — a column and a filter beside `app` and `profile` — not a mode the
 * whole application is in. Projects are created and configured in their own
 * section, like any other entity on the platform.
 */
export interface Session {
  user: SessionUser
  /** Every project this session can see — the source for names and filters. */
  projects: ProjectRef[]
}

export interface PermissionCheck {
  allowed: boolean
  /**
   * `null` when allowed, otherwise the sentence to show. Written so a call site
   * reads `denied={check.denial}` and cannot forget the denied case.
   */
  denial: string | null
}

const ALLOWED: PermissionCheck = { allowed: true, denial: null }

export const SessionContext = createContext<Session | null>(null)

export function useSession(): Session {
  const session = useContext(SessionContext)
  if (!session) {
    throw new Error("useSession must be used inside <SessionProvider>")
  }
  return session
}

/** The roles in force on one project: its own grants plus the platform's. */
export function rolesFor(session: Session, projectId?: string): Role[] {
  const granted = projectId ? (session.user.projectRoles[projectId] ?? []) : []
  return [...new Set([...session.user.platformRoles, ...granted])]
}

export function projectOf(
  session: Session,
  projectId?: string
): ProjectRef | null {
  if (!projectId) {
    return null
  }
  return session.projects.find((entry) => entry.id === projectId) ?? null
}

/**
 * Answers a permission, on one project or anywhere.
 *
 * Three cases, and the third is the one that makes a rail item work. A platform
 * act reads platform roles alone — being project-admin of one project must
 * never open Identity. A project act named with a `projectId` reads that
 * project's grants plus the platform's. A project act named *without* one is
 * the rail's question — "may this person do it at all, somewhere?" — and it is
 * true if any project they hold would allow it. Hiding Approvals from someone
 * who approves on one project out of three would be a lie of omission.
 */
export function can(
  session: Session,
  permission: Permission,
  projectId?: string
): boolean {
  if (permissionScope(permission) === "platform") {
    return session.user.platformRoles.some((role) =>
      roleGrants(role, permission)
    )
  }
  if (projectId) {
    return rolesFor(session, projectId).some((role) =>
      roleGrants(role, permission)
    )
  }
  if (session.user.platformRoles.some((role) => roleGrants(role, permission))) {
    return true
  }
  return Object.values(session.user.projectRoles).some((roles) =>
    roles.some((role) => roleGrants(role, permission))
  )
}

/**
 * The one call-site shape for a gated act.
 *
 * Returns the denial sentence rather than a boolean alone because of the rule
 * this module exists to serve: navigation a role cannot use is hidden, but an
 * *action* it cannot use stays visible and explains itself. A check that only
 * said `false` would quietly push every call site toward hiding, which is the
 * wrong half of the rule.
 *
 * Pass the row's `projectId` wherever a list mixes projects — which is every
 * list in the product. The sentence then names the project the act was refused
 * on, so a row that says "needs approver on plexor" cannot be misread as a flat
 * no by someone who approves on comuki all day.
 */
export function useCan(
  permission: Permission,
  projectId?: string
): PermissionCheck {
  const session = useSession()
  return useMemo(() => {
    if (can(session, permission, projectId)) {
      return ALLOWED
    }
    const scope = permissionScope(permission)
    const where =
      scope === "project" ? projectOf(session, projectId)?.key : undefined
    return { allowed: false, denial: needsLabel(permission, where) }
  }, [session, permission, projectId])
}
