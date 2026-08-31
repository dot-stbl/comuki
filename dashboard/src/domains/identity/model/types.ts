import type { Role } from "@/shared/session"

/** Local account, invited and not yet accepted, or switched off. */
export type UserStatus = "active" | "invited" | "disabled"

export type SubjectKind = "user" | "api-key"

export type ApiKeyStatus = "active" | "revoked"

export interface UserRow {
  id: string
  name: string
  email: string
  /** The identity provider's subject, once linked. `null` while local-only. */
  oidcSubject: string | null
  status: UserStatus
  lastSeenAt: string | null
  createdAt: string
  /**
   * Where this person holds something, already resolved to scope labels —
   * `platform` and project slugs. Empty is a real answer and a common one: an
   * account can exist and hold nothing at all.
   */
  scopes: string[]
}

export interface GrantRow {
  id: string
  subjectKind: SubjectKind
  subjectId: string
  /** The value that identifies the subject: an address, or a key prefix. */
  subjectLabel: string
  /** Prose beside it: a person's name, or what the key is for. */
  subjectName: string
  role: Role
  projectId: string | null
  /** `platform`, or the project slug. */
  scopeLabel: string
  grantedAt: string
  /** The grant is real and inert: the subject is disabled or revoked. */
  subjectInactive: boolean
}

export interface ApiKeyRow {
  id: string
  name: string
  /** The visible head of the secret — the only part that survived creation. */
  prefix: string
  status: ApiKeyStatus
  createdAt: string
  lastUsedAt: string | null
  expiresAt: string | null
  /**
   * Days until it expires. Negative once it has passed, `null` when the key
   * has no expiry at all — three different facts, and a column that renders
   * them as one number would say the wrong thing twice.
   */
  expiresInDays: number | null
  /** What it grants, one entry per assignment: `member on comuki`. */
  grants: string[]
}

export interface InviteUserInput {
  name: string
  email: string
  /** `true` sends an invitation; `false` creates a usable local account. */
  invite: boolean
}

export interface LinkOidcInput {
  userId: string
  subject: string
}

export interface SetUserDisabledInput {
  userId: string
  disabled: boolean
}

export interface GrantRoleInput {
  subjectKind: SubjectKind
  subjectId: string
  role: Role
  /** `null` is platform scope. */
  projectId: string | null
}

export interface CreateApiKeyInput {
  name: string
  /** ISO day, or `null` for a key with no expiry. */
  expiresAt: string | null
}

/**
 * Everything the screen holds, in one payload.
 *
 * The three lists reference each other — a grant names a user or a key, a key's
 * row summarises its grants — so they are fetched and invalidated together. Two
 * queries would let the screen render a grant against a key that had already
 * been revoked in the other half of it.
 */
export interface IdentitySnapshot {
  users: UserRow[]
  grants: GrantRow[]
  keys: ApiKeyRow[]
  /** Project scopes a grant may be written against, newest registry first. */
  projects: Array<{ id: string; slug: string; name: string }>
}
