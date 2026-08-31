import type { Role } from "@/shared/session"

/**
 * Who exists on the platform, what they hold, and the keys that act for them.
 *
 * Fictional, like every other seed in this folder. The addresses, subjects and
 * key prefixes are invented; no prefix here is a truncation of a real secret,
 * because no real secret was ever generated to truncate.
 *
 * `u_duty` is deliberately the same person `session.seed.ts` signs in, holding
 * exactly the grants that file gives them — member on the platform, approver on
 * `p_comuki`, viewer on `p_plexor`, project-admin on `p_atlas`. Identity is the
 * screen where those grants are *administered*, so it would be a strange first
 * lesson if the screen and the session disagreed about them.
 *
 * The awkward rows are seeded on purpose, because they are the ones a layout
 * that only ever sees the happy case renders as a broken cell:
 *
 * - `u_nadia` holds roles on two projects and none on the platform.
 * - `u_tomas` is disabled and still carries a live platform grant.
 * - `u_ines` was invited and has never signed in — no subject, no last seen.
 * - `k_mcp` has never been used.
 * - `k_audit` expires in three days.
 * - `k_legacy` is already revoked, and stays in the list as the audit trail.
 */

/** Local account, invited and not yet accepted, or switched off. */
export type SeedUserStatus = "active" | "invited" | "disabled"

export interface SeedUser {
  id: string
  name: string
  email: string
  /**
   * The identity provider's subject, once linked. `null` while the account is
   * local-only — OIDC says who you are, and linking is a separate act from
   * existing here.
   */
  oidcSubject: string | null
  status: SeedUserStatus
  /** ISO minute, or `null` for an account that has never been used. */
  lastSeenAt: string | null
  createdAt: string
}

export type SeedSubjectKind = "user" | "api-key"

export interface SeedRoleAssignment {
  id: string
  subjectKind: SeedSubjectKind
  /** A `SeedUser.id` or a `SeedApiKey.id`, per `subjectKind`. */
  subjectId: string
  /**
   * One of the six. Roles live in code and only ever get assigned — there is
   * no role table to write to, which is why this is `Role` from the session
   * module rather than a string this file could invent a seventh value for.
   */
  role: Role
  /** `null` is platform scope; otherwise a project id. */
  projectId: string | null
  grantedAt: string
}

export type SeedApiKeyStatus = "active" | "revoked"

export interface SeedApiKey {
  id: string
  /** What it is for, in the operator's words. */
  name: string
  /**
   * The visible head of the secret, and the only part of it that survives
   * creation. The rest is hashed the moment it is shown, which is why the list
   * can identify a key and can never re-display one.
   */
  prefix: string
  status: SeedApiKeyStatus
  createdAt: string
  /** `null` for a key that has never authenticated a request. */
  lastUsedAt: string | null
  /** `null` for a key with no expiry. */
  expiresAt: string | null
}

export const USERS_SEED: SeedUser[] = [
  {
    id: "u_rhea",
    name: "Rhea Okafor",
    email: "rhea@comuki.local",
    oidcSubject: "oidc|comuki|4f21ba9c",
    status: "active",
    lastSeenAt: "2026-08-30 08:12",
    createdAt: "2026-03-04",
  },
  {
    id: "u_duty",
    name: "Duty Engineer",
    email: "duty@comuki.local",
    oidcSubject: "oidc|comuki|8f2a41d0",
    status: "active",
    lastSeenAt: "2026-08-30 09:47",
    createdAt: "2026-03-11",
  },
  {
    // Two projects, no platform standing. Reads as an empty scope column on
    // every platform-shaped assumption a layout might make.
    id: "u_nadia",
    name: "Nadia Ferrer",
    email: "nadia@plexor.dev",
    oidcSubject: null,
    status: "active",
    lastSeenAt: "2026-08-29 17:03",
    createdAt: "2026-05-22",
  },
  {
    // Switched off, grant intact: revoking access and revoking a role are two
    // different acts, and the screen has to show both facts at once.
    id: "u_tomas",
    name: "Tomas Lindqvist",
    email: "tomas@comuki.local",
    oidcSubject: "oidc|comuki|c7d90e13",
    status: "disabled",
    lastSeenAt: "2026-07-18 11:26",
    createdAt: "2026-04-02",
  },
  {
    id: "u_ines",
    name: "Inés Moreau",
    email: "ines@atlas.example",
    oidcSubject: null,
    status: "invited",
    lastSeenAt: null,
    createdAt: "2026-08-26",
  },
]

export const API_KEYS_SEED: SeedApiKey[] = [
  {
    id: "k_ci",
    name: "ci-pipeline",
    prefix: "cmk_4e9c",
    status: "active",
    createdAt: "2026-06-02",
    lastUsedAt: "2026-08-30 09:41",
    expiresAt: null,
  },
  {
    // Created, granted, never once presented. The most common way a key leaks
    // is by being forgotten, so a key with no last use is worth seeing.
    id: "k_mcp",
    name: "mcp-bridge",
    prefix: "cmk_1a77",
    status: "active",
    createdAt: "2026-08-11",
    lastUsedAt: null,
    expiresAt: null,
  },
  {
    id: "k_audit",
    name: "nightly-audit",
    prefix: "cmk_b0d2",
    status: "active",
    createdAt: "2026-02-20",
    lastUsedAt: "2026-08-30 02:00",
    expiresAt: "2026-09-02",
  },
  {
    id: "k_legacy",
    name: "legacy-import",
    prefix: "cmk_77aa",
    status: "revoked",
    createdAt: "2026-01-09",
    lastUsedAt: "2026-05-30 14:52",
    expiresAt: null,
  },
]

export const ROLE_ASSIGNMENTS_SEED: SeedRoleAssignment[] = [
  {
    id: "g_rhea_platform",
    subjectKind: "user",
    subjectId: "u_rhea",
    role: "platform-admin",
    projectId: null,
    grantedAt: "2026-03-04",
  },
  {
    id: "g_duty_platform",
    subjectKind: "user",
    subjectId: "u_duty",
    role: "member",
    projectId: null,
    grantedAt: "2026-03-11",
  },
  {
    id: "g_duty_comuki",
    subjectKind: "user",
    subjectId: "u_duty",
    role: "approver",
    projectId: "p_comuki",
    grantedAt: "2026-03-11",
  },
  {
    id: "g_duty_plexor",
    subjectKind: "user",
    subjectId: "u_duty",
    role: "viewer",
    projectId: "p_plexor",
    grantedAt: "2026-05-19",
  },
  {
    id: "g_duty_atlas",
    subjectKind: "user",
    subjectId: "u_duty",
    role: "project-admin",
    projectId: "p_atlas",
    grantedAt: "2026-06-27",
  },
  {
    id: "g_nadia_comuki",
    subjectKind: "user",
    subjectId: "u_nadia",
    role: "approver",
    projectId: "p_comuki",
    grantedAt: "2026-05-22",
  },
  {
    id: "g_nadia_atlas",
    subjectKind: "user",
    subjectId: "u_nadia",
    role: "project-admin",
    projectId: "p_atlas",
    grantedAt: "2026-06-28",
  },
  {
    id: "g_tomas_platform",
    subjectKind: "user",
    subjectId: "u_tomas",
    role: "operator",
    projectId: null,
    grantedAt: "2026-04-02",
  },
  {
    id: "g_ines_atlas",
    subjectKind: "user",
    subjectId: "u_ines",
    role: "viewer",
    projectId: "p_atlas",
    grantedAt: "2026-08-26",
  },
  {
    id: "g_ci_comuki",
    subjectKind: "api-key",
    subjectId: "k_ci",
    role: "member",
    projectId: "p_comuki",
    grantedAt: "2026-06-02",
  },
  {
    id: "g_audit_platform",
    subjectKind: "api-key",
    subjectId: "k_audit",
    role: "viewer",
    projectId: null,
    grantedAt: "2026-02-20",
  },
  {
    id: "g_mcp_atlas",
    subjectKind: "api-key",
    subjectId: "k_mcp",
    role: "member",
    projectId: "p_atlas",
    grantedAt: "2026-08-11",
  },
]
