import type { Role } from "@/shared/session"

import {
  API_KEYS_SEED,
  ROLE_ASSIGNMENTS_SEED,
  USERS_SEED,
  type SeedApiKey,
  type SeedRoleAssignment,
  type SeedSubjectKind,
  type SeedUser,
} from "./identity.seed"

/**
 * Mutable mock store for users, grants and keys.
 *
 * Same shape and same reason as `runs.store.ts`: a seed is a constant, and a
 * constant cannot record a decision — disabling a user or revoking a key would
 * survive the click and then be undone by the refetch behind it. Three lists
 * live here rather than three stores because they reference each other: a grant
 * names a user or a key, and revoking a key has to leave its grants pointing at
 * something that still exists.
 *
 * Session-scoped and in-memory by design: a reload is a fresh platform.
 *
 * One rule this file exists to hold: **the plaintext of a key is returned once
 * and never stored.** `createSeedApiKey` hands the caller the whole secret and
 * keeps only its visible head. There is no read path back to it — not here, not
 * in a query, not in a component — because the moment there is one, "shown
 * exactly once" becomes a property of the UI's memory rather than of the store.
 */

function cloneUser(user: SeedUser): SeedUser {
  return { ...user }
}

function cloneKey(key: SeedApiKey): SeedApiKey {
  return { ...key }
}

function cloneGrant(grant: SeedRoleAssignment): SeedRoleAssignment {
  return { ...grant }
}

let users: SeedUser[] = USERS_SEED.map(cloneUser)
let keys: SeedApiKey[] = API_KEYS_SEED.map(cloneKey)
let grants: SeedRoleAssignment[] = ROLE_ASSIGNMENTS_SEED.map(cloneGrant)

/* Users ------------------------------------------------------------------ */

export function listSeedUsers(): SeedUser[] {
  return users
}

export interface CreateSeedUserInput {
  name: string
  email: string
  /**
   * `true` sends an invitation and leaves the account waiting; `false` creates
   * a usable local account outright. Both are in §13 and they are genuinely
   * different states, not two spellings of one.
   */
  invite: boolean
}

export function createSeedUser(input: CreateSeedUserInput): SeedUser {
  const created: SeedUser = {
    id: `u_${input.email.split("@")[0].replace(/[^a-z0-9]/gi, "").toLowerCase()}`,
    name: input.name,
    email: input.email,
    oidcSubject: null,
    status: input.invite ? "invited" : "active",
    lastSeenAt: null,
    createdAt: new Date().toISOString().slice(0, 10),
  }
  users = [...users, created]
  return created
}

/** OIDC says who you are; this is the record that it is the same person. */
export function linkSeedOidcSubject(userId: string, subject: string): void {
  users = users.map((user) =>
    user.id === userId ? { ...user, oidcSubject: subject } : user
  )
}

/**
 * Switching an account off, and back on.
 *
 * Disabling deliberately leaves the grants alone: an account that is off holds
 * nothing, and the day it comes back it should come back as itself rather than
 * as a stranger who has to be re-granted everything.
 */
export function setSeedUserDisabled(userId: string, disabled: boolean): void {
  users = users.map((user) =>
    user.id === userId
      ? { ...user, status: disabled ? "disabled" : "active" }
      : user
  )
}

/* Role assignments -------------------------------------------------------- */

export function listSeedRoleAssignments(): SeedRoleAssignment[] {
  return grants
}

export interface GrantSeedRoleInput {
  subjectKind: SeedSubjectKind
  subjectId: string
  role: Role
  /** `null` is platform scope. */
  projectId: string | null
}

export function grantSeedRole(input: GrantSeedRoleInput): SeedRoleAssignment {
  const created: SeedRoleAssignment = {
    id: `g_${input.subjectId}_${input.projectId ?? "platform"}_${input.role}`,
    subjectKind: input.subjectKind,
    subjectId: input.subjectId,
    role: input.role,
    projectId: input.projectId,
    grantedAt: new Date().toISOString().slice(0, 10),
  }
  // Granting the same role in the same scope twice is not an error and not a
  // duplicate row — it is the grant that is already there.
  const already = grants.find((grant) => grant.id === created.id)
  if (already) {
    return already
  }
  grants = [...grants, created]
  return created
}

export function revokeSeedRole(grantId: string): void {
  grants = grants.filter((grant) => grant.id !== grantId)
}

/* API keys ---------------------------------------------------------------- */

export function listSeedApiKeys(): SeedApiKey[] {
  return keys
}

export interface CreateSeedApiKeyInput {
  name: string
  /** ISO day, or `null` for a key with no expiry. */
  expiresAt: string | null
}

export interface CreatedSeedApiKey {
  key: SeedApiKey
  /**
   * The whole secret, returned exactly once.
   *
   * It is a return value rather than a field on the stored key on purpose:
   * there is nowhere for a second reader to find it, so "shown once" is a fact
   * about the data and not a promise the UI has to keep.
   */
  plaintext: string
}

/** Four hex characters, which is what the visible head of a key is made of. */
function hex(length: number): string {
  let out = ""
  while (out.length < length) {
    out += Math.floor(Math.random() * 16).toString(16)
  }
  return out
}

export function createSeedApiKey(
  input: CreateSeedApiKeyInput
): CreatedSeedApiKey {
  const prefix = `cmk_${hex(4)}`
  const created: SeedApiKey = {
    id: `k_${prefix.slice(4)}`,
    name: input.name,
    prefix,
    status: "active",
    createdAt: new Date().toISOString().slice(0, 10),
    lastUsedAt: null,
    expiresAt: input.expiresAt,
  }
  keys = [...keys, created]
  return { key: created, plaintext: `${prefix}_${hex(32)}` }
}

/**
 * Revoking a key leaves the row and kills the secret.
 *
 * Deleting it would erase the fact that it existed, which is the one thing an
 * audit of a leaked key actually needs. Its grants go with it: a revoked key
 * that still reads as holding `member` on a project is a lie in the one table
 * whose whole job is saying who holds what.
 */
export function revokeSeedApiKey(keyId: string): void {
  keys = keys.map((key) =>
    key.id === keyId ? { ...key, status: "revoked" } : key
  )
  grants = grants.filter(
    (grant) => !(grant.subjectKind === "api-key" && grant.subjectId === keyId)
  )
}

/** Back to the seeded platform — used by tests and stories. */
export function resetSeedIdentity(): void {
  users = USERS_SEED.map(cloneUser)
  keys = API_KEYS_SEED.map(cloneKey)
  grants = ROLE_ASSIGNMENTS_SEED.map(cloneGrant)
}
