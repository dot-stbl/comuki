import type {
  SeedApiKey,
  SeedRoleAssignment,
  SeedUser,
} from "@/shared/api/mock/identity.seed"
import type { SeedProject } from "@/shared/api/mock/projects.seed"

import type { ApiKeyRow, GrantRow, IdentitySnapshot, UserRow } from "./types"

/**
 * The three lists, joined once.
 *
 * Users, grants and keys are stored apart and read together, because none of
 * them answers a question on its own: a grant is only legible once you know
 * whose it is, and a key is only safe to leave alone once you know what it
 * opens. The join happens here rather than in three column files so the same
 * words — a subject label, a scope label — are produced in one place and cannot
 * drift between the tables that show them.
 */

/** The scope column's value. A grant is either platform-wide or on a project. */
export const PLATFORM_SCOPE = "platform"

/**
 * How many days until a key expires, from a day this function is given.
 *
 * `today` is a parameter rather than a `new Date()` inside, because the answer
 * is a fact about a pair of dates and a function that reads the clock cannot be
 * asked about a pair.
 */
export function daysUntil(expiresAt: string | null, today: Date): number | null {
  if (!expiresAt) {
    return null
  }
  const end = Date.parse(`${expiresAt}T00:00:00Z`)
  if (Number.isNaN(end)) {
    return null
  }
  const start = Date.UTC(
    today.getUTCFullYear(),
    today.getUTCMonth(),
    today.getUTCDate()
  )
  return Math.round((end - start) / 86_400_000)
}

/** Within a week of the end, and not past it: the window worth marking. */
export const EXPIRY_SOON_DAYS = 7

function scopeLabel(
  projectId: string | null,
  projects: readonly SeedProject[]
): string {
  if (!projectId) {
    return PLATFORM_SCOPE
  }
  // A grant against a project the registry no longer lists still has to render
  // as a row: the id is a worse label than the slug and a far better one than
  // a blank cell that reads as a broken join.
  return projects.find((entry) => entry.id === projectId)?.slug ?? projectId
}

export function buildIdentitySnapshot(
  users: readonly SeedUser[],
  grants: readonly SeedRoleAssignment[],
  keys: readonly SeedApiKey[],
  projects: readonly SeedProject[],
  today: Date
): IdentitySnapshot {
  const userById = new Map(users.map((user) => [user.id, user]))
  const keyById = new Map(keys.map((key) => [key.id, key]))

  const grantRows: GrantRow[] = grants.map((grant) => {
    const user =
      grant.subjectKind === "user" ? userById.get(grant.subjectId) : undefined
    const key =
      grant.subjectKind === "api-key" ? keyById.get(grant.subjectId) : undefined

    return {
      id: grant.id,
      subjectKind: grant.subjectKind,
      subjectId: grant.subjectId,
      subjectLabel: user?.email ?? key?.prefix ?? grant.subjectId,
      subjectName: user?.name ?? key?.name ?? "",
      role: grant.role,
      projectId: grant.projectId,
      scopeLabel: scopeLabel(grant.projectId, projects),
      grantedAt: grant.grantedAt,
      // A grant on a disabled account or a revoked key is a real row and an
      // inert one. Saying so is the whole reason this screen is worth reading:
      // disabling somebody and un-granting them are different acts.
      subjectInactive:
        user?.status === "disabled" || key?.status === "revoked",
    }
  })

  const userRows: UserRow[] = users.map((user) => ({
    id: user.id,
    name: user.name,
    email: user.email,
    oidcSubject: user.oidcSubject,
    status: user.status,
    lastSeenAt: user.lastSeenAt,
    createdAt: user.createdAt,
    scopes: grantRows
      .filter(
        (grant) =>
          grant.subjectKind === "user" && grant.subjectId === user.id
      )
      .map((grant) => grant.scopeLabel),
  }))

  const keyRows: ApiKeyRow[] = keys.map((key) => ({
    id: key.id,
    name: key.name,
    prefix: key.prefix,
    status: key.status,
    createdAt: key.createdAt,
    lastUsedAt: key.lastUsedAt,
    expiresAt: key.expiresAt,
    expiresInDays: daysUntil(key.expiresAt, today),
    grants: grantRows
      .filter(
        (grant) =>
          grant.subjectKind === "api-key" && grant.subjectId === key.id
      )
      .map((grant) => `${grant.role} on ${grant.scopeLabel}`),
  }))

  return {
    users: userRows,
    grants: grantRows,
    keys: keyRows,
    projects: projects.map((project) => ({
      id: project.id,
      slug: project.slug,
      name: project.name,
    })),
  }
}
