import { ROLES, type Role, type SessionUser } from "@/shared/session"
import type { LoginRequest } from "@/shared/api/_generated/types/LoginRequest"
import type { LoginResponse } from "@/shared/api/_generated/types/LoginResponse"
import type { MeResponse } from "@/shared/api/_generated/types/MeResponse"
import type { ApiKeyView } from "@/shared/api/_generated/types/ApiKeyView"
import type { RoleAssignmentView } from "@/shared/api/_generated/types/RoleAssignmentView"
import type { UserAccountView } from "@/shared/api/_generated/types/UserAccountView"
import type {
  SeedApiKey,
  SeedRoleAssignment,
  SeedSubjectKind,
  SeedUser,
} from "@/shared/api/mock/identity.seed"

/**
 * kubb wire → domain mappers for the auth surface.
 *
 * Mirrors the runs domain's split: every wire shape the host emits gets one
 * mapper here, and the rest of the dashboard sees only the domain projection.
 * Adding a column to `MeResponse` is a one-file change — the screens keep
 * reading `SessionUser` and the new field is documented in the mapper.
 *
 * The wire here is sparse by design. `MeResponse` does not carry per-project
 * role assignments — `roles` is a flat list of role keys across all scopes,
 * and `permissions.projects` is an effective-permission view that cannot be
 * safely reversed into roles. The mapper fills `projectRoles` with `{}` and
 * documents the limitation in `mapMeResponseToSessionUser`'s note; a future
 * backend shape (a `/me/roles?projectId=…` or a `projectRoles` field on
 * `MeResponse`) is the path to populating it.
 */

const PLATFORM_SCOPE_ROLES: ReadonlySet<string> = new Set<Role>([
  "operator",
  "platform-admin",
])

/** The six roles, as a membership test. `ROLES` is the vocabulary; this reads it. */
const KNOWN_ROLES: ReadonlySet<string> = new Set<Role>(ROLES)

/**
 * Is this wire word one of the six roles?
 *
 * The file already narrowed `string` → `Role` with a `Set` when it filtered
 * `me.roles`; the grants mapper answered the same question with a cast 115
 * lines later. One guard, both callers — a cast cannot fail, and a role the
 * dashboard has never heard of must not become one it thinks it knows.
 */
function isRole(value: string): value is Role {
  return KNOWN_ROLES.has(value)
}

/** The two subject kinds a grant can name; the wire types them as `string`. */
const SUBJECT_KINDS: ReadonlySet<string> = new Set<SeedSubjectKind>([
  "user",
  "api-key",
])

function isSubjectKind(value: string): value is SeedSubjectKind {
  return SUBJECT_KINDS.has(value)
}

/**
 * Email + password → login wire.
 *
 * The kubb wire shape is the same tuple the screen collects; the mapper exists
 * so callers do not import a kubb type into the form layer. The screen's
 * `useLoginMutation(email, password)` calls this once and forgets about it.
 */
export function mapLoginRequestFromInput(
  email: string,
  password: string,
): LoginRequest {
  return { email, password }
}

/**
 * Login wire → domain session projection.
 *
 * The login endpoint answers only with the basic identity (id, email,
 * displayName); the role/permission read lives at `/me`. This mapper is the
 * one the screen can use immediately after a 200 — it carries the welcome
 * banner — but the `me` query is the one that powers the rail and the guard.
 *
 * Roles are empty here by design; the host did not answer with them.
 */
export function mapLoginResponseToSessionUser(
  response: LoginResponse,
): SessionUser {
  return {
    id: response.userId,
    name: response.displayName,
    email: response.email,
    platformRoles: [],
    projectRoles: {},
  }
}

/**
 * `MeResponse` → `SessionUser`.
 *
 * The host returns the active subject and its roles, plus an effective
 * permission view per scope. The dashboard's `SessionUser` projects roles
 * per project; the wire's `roles` is a flat list of role keys and the
 * permissions view is not safely reversible. We:
 *
 * - take `userId` as the session id, falling back to `subjectId` for
 *   api-key requests (`userId` is `null` on those), so the rail still
 *   renders an avatar initial rather than crashing;
 * - filter `roles` to platform-scope roles for `platformRoles`;
 * - leave `projectRoles` empty with a documented reason — the dashboard
 *   falls back to platform roles for project-scope checks until a
 *   per-project role endpoint lands.
 */
export function mapMeResponseToSessionUser(me: MeResponse): SessionUser {
  const id = me.userId ?? me.subjectId
  const platformRoles = me.roles.filter((role): role is Role =>
    PLATFORM_SCOPE_ROLES.has(role),
  )

  return {
    id,
    name: me.displayName ?? "",
    email: me.email ?? "",
    platformRoles,
    // `projectRoles` is intentionally empty: the host's `/me` does not
    // expose per-project role assignments (only effective permissions).
    // Project-scope checks fall back to platform roles until a
    // per-project roles endpoint exists.
    projectRoles: {},
  }
}

/**
 * OIDC start wire → authorization URL.
 *
 * The kubb-generated `/api/v1/auth/oidc/:provider/start` client returns
 * `any` because the endpoint answers a 302 — kubb's transport follows
 * redirects and the response body is whatever the IdP returned. For a
 * browser-driven OIDC flow the right path is `window.location.href = …`,
 * which does not need this mapper.
 *
 * The mapper is kept so callers that do receive a string response (e.g. an
 * internal proxy that pre-resolves the redirect) can extract the URL;
 * anything else throws with a message that points at the kubb follow-
 * redirect behaviour.
 */
export function mapOidcStartToAuthorizationUrl(start: unknown): string {
  if (typeof start === "string") {
    return start;
  }
  throw new Error(
    "OIDC start did not return a string URL — kubb follows 302 redirects and the response body is the IdP's page. Use window.location.href against /api/v1/auth/oidc/{provider}/start directly.",
  );
}

// ---------------------------------------------------------------------------
// Identity-admin lists (issue #45 / F13).
//
// The kubb-generated wire shapes (`UserAccountView`, `RoleAssignmentView`,
// `ApiKeyView`) deliberately differ from the seed types the
// `buildIdentitySnapshot` helper consumes — the wire is narrower (no
// OIDC subject, no `lastSeenAt`, no `invited` user state) because the
// host has not grown the columns yet. The mappers below carry the same
// gap fill the runs / projects mappers use on their wire shapes: tolerate
// the missing fields, fall back to the honest default (`null`, "active",
// missing date), and document what the wire has dropped.
//
// The seed-shaped output here (`SeedUser` / `SeedRoleAssignment` /
// `SeedApiKey`) is deliberate: the existing `buildIdentitySnapshot` is
// the one place that knows how to join users ↔ grants ↔ keys; rather than
// refactor it, the wire rows adopt the seed shapes and the screen
// receives the same `IdentitySnapshot` it always did.
// ---------------------------------------------------------------------------

/**
 * Wire `UserAccountView` → seed-shaped `SeedUser`.
 *
 * The kubb view carries email, displayName, disabled flag, tokens version
 * and createdAt only. The dashboard's account row also wants:
 *
 * - `oidcSubject`: not on the wire yet (a future OIDC-list endpoint or a
 *   column on `UserAccountView` would close the gap); `null` is the
 *   honest answer until then.
 * - `lastSeenAt`: not on the wire — the host has no last-seen column on
 *   the `users` table. `null` until a `last_seen_at` migration ships.
 * - `status`: only `disabled` is on the wire. The dashboard renders
 *   "active" for everything that is not disabled, "disabled" for
 *   everything that is. The "invited" state carried by the mock store is
 *   not in the host's user row yet — when invited accounts land in the
 *   schema this mapper is the one place to flip them.
 * - `name`: the wire calls it `displayName`; the seed-row column is `name`.
 *   The same field carries the same value; the rename is a wire-vs-screen
 *   vocabulary choice the dashboard pays no mind to.
 */
export function mapUserAccountViewToSeed(view: UserAccountView): SeedUser {
  return {
    // The kubb wire models `UserId` / `RoleAssignmentId` as
    // `{ value?: string }` — the dashboard's seed-shape id is a flat
    // string. Unwrap before crossing the boundary so the snapshot join
    // (which uses the row id as the Map key) survives.
    id: view.id.value ?? "",
    name: view.displayName,
    email: view.email,
    oidcSubject: null,
    status: view.disabled ? "disabled" : "active",
    lastSeenAt: null,
    createdAt: view.createdAt,
  };
}

/** Wire users page → list of seed-shaped users. */
export function mapIdentityUsersPageToSeedUsers(
  page: { items: UserAccountView[] },
): SeedUser[] {
  return page.items.map(mapUserAccountViewToSeed);
}

/**
 * Wire `RoleAssignmentView` → seed-shaped `SeedRoleAssignment`.
 *
 * The kubb view carries `subjectType` as a wire string ("user" /
 * "api-key") and `subjectId` as a string id; the row matches the seed's
 * `subjectKind` + `subjectId`. Roles come back as a kebab-case string the
 * session module already enumerates (`Role`) — but a `string` is what the
 * wire actually promises, so both go through the guards above rather than a
 * cast. A word neither vocabulary contains degrades to the least-privileged
 * reading (`viewer`, `user`) instead of entering the session as a role no
 * permission table has a row for.
 */
export function mapRoleAssignmentViewToSeed(view: RoleAssignmentView): SeedRoleAssignment {
  return {
    // `RoleAssignmentId` is `{ value?: string }` on the wire — the seed
    // join keys by id as a flat string, so unwrap here.
    id: view.id.value ?? "",
    subjectKind: isSubjectKind(view.subjectType) ? view.subjectType : "user",
    subjectId: view.subjectId,
    role: isRole(view.role) ? view.role : "viewer",
    projectId: view.scopeProjectId,
    grantedAt: view.createdAt,
  };
}

/** Wire grants page → list of seed-shaped role assignments. */
export function mapGrantsPageToSeedGrants(
  page: { items: RoleAssignmentView[] },
): SeedRoleAssignment[] {
  return page.items.map(mapRoleAssignmentViewToSeed);
}

/**
 * Wire `ApiKeyView` → seed-shaped `SeedApiKey`.
 *
 * The wire carries the public projection only — no `KeyHmac`, no
 * plaintext, no `tenantProjectId`. The seed-shape drops `KeyHmac` and
 * tenant; `expiresAt` is not on the wire yet (the host has no expiry
 * column), so it lands as `null` until a future wire shape adds it.
 */
export function mapApiKeyViewToSeed(view: ApiKeyView): SeedApiKey {
  return {
    id: view.id,
    name: view.name,
    prefix: view.prefix,
    status: view.isActive ? "active" : "revoked",
    createdAt: view.createdAt,
    lastUsedAt: view.lastUsedAt,
    expiresAt: null,
  };
}

/** Wire keys page → list of seed-shaped api keys. */
export function mapApiKeysPageToSeedKeys(page: { items: ApiKeyView[] }): SeedApiKey[] {
  return page.items.map(mapApiKeyViewToSeed);
}

// Legacy names retained for callers that already imported them before the
// identity-list wiring (issue #45) landed — they simply call the seed
// mapper now, so the callsite signature stays.
export const mapApiKeyViewToKeyView = mapApiKeyViewToSeed;
export const mapIdentityAdminKeysToKeyView = mapApiKeysPageToSeedKeys;
export const mapIdentityUsersPageToUserRows = mapIdentityUsersPageToSeedUsers;
export const mapGrantsPageToGrantRows = mapGrantsPageToSeedGrants;
export const mapApiKeysPageToApiKeyRows = mapApiKeysPageToSeedKeys;