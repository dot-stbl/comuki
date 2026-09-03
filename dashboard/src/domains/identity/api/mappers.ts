import type { Role, SessionUser } from "@/shared/session"
import type { LoginRequest } from "@/shared/api/_generated/types/LoginRequest"
import type { LoginResponse } from "@/shared/api/_generated/types/LoginResponse"
import type { MeResponse } from "@/shared/api/_generated/types/MeResponse"

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

const PLATFORM_SCOPE_ROLES = new Set<Role>(["operator", "platform-admin"])

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
    PLATFORM_SCOPE_ROLES.has(role as Role),
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
    return start
  }
  throw new Error(
    "OIDC start did not return a string URL — kubb follows 302 redirects and the response body is the IdP's page. Use window.location.href against /api/v1/auth/oidc/{provider}/start directly.",
  )
}