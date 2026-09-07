import { describe, expect, it } from "vitest"

import {
  mapApiKeyViewToSeed,
  mapApiKeysPageToSeedKeys,
  mapGrantsPageToSeedGrants,
  mapIdentityUsersPageToSeedUsers,
  mapLoginRequestFromInput,
  mapLoginResponseToSessionUser,
  mapMeResponseToSessionUser,
  mapOidcStartToAuthorizationUrl,
  mapRoleAssignmentViewToSeed,
  mapUserAccountViewToSeed,
} from "@/domains/identity/api/mappers"
import type { ApiKeyView } from "@/shared/api/_generated/types/ApiKeyView"
import type { MeResponse } from "@/shared/api/_generated/types/MeResponse"
import type { RoleAssignmentView } from "@/shared/api/_generated/types/RoleAssignmentView"
import type { UserAccountView } from "@/shared/api/_generated/types/UserAccountView"

/**
 * Wire → domain for the auth surface.
 *
 * Pin the four contracts the kubb wire has to keep:
 *
 * - `mapLoginRequestFromInput` is the trivial identity — both fields land
 *   on the wire under the names the screen collected them.
 * - `mapLoginResponseToSessionUser` is a sparse projection: id, email,
 *   displayName. Roles are empty here; the host did not answer with them.
 * - `mapMeResponseToSessionUser` is the projection the rail reads. It
 *   filters roles to platform-scope, falls back to `subjectId` for api-key
 *   calls (where `userId` is null), and leaves `projectRoles` empty with
 *   a documented reason — the wire does not carry per-project roles.
 * - `mapOidcStartToAuthorizationUrl` accepts a string and rejects
 *   anything else, with a message that names the kubb follow-redirect
 *   behaviour so the next reader is not confused.
 */

describe("mapLoginRequestFromInput", () => {
  it("carries email and password through verbatim", () => {
    const request = mapLoginRequestFromInput("user@comuki.local", "p4ssw0rd")

    expect(request).toEqual({ email: "user@comuki.local", password: "p4ssw0rd" })
  })

  it("does not trim, lowercase, or normalise either field", () => {
    // The screen's job to validate input shape; the mapper's job is to keep
    // what the operator typed so a leading space in a paste survives the
    // round-trip and surfaces as a 401 rather than a silent match.
    const request = mapLoginRequestFromInput("  USER@Comuki.Local  ", " ok ")

    expect(request.email).toBe("  USER@Comuki.Local  ")
    expect(request.password).toBe(" ok ")
  })
})

describe("mapLoginResponseToSessionUser", () => {
  it("carries id, email and displayName through and leaves roles empty", () => {
    const session = mapLoginResponseToSessionUser({
      userId: "00000000-0000-0000-0000-000000000001",
      email: "duty@comuki.local",
      displayName: "Duty Engineer",
    })

    expect(session).toEqual({
      id: "00000000-0000-0000-0000-000000000001",
      name: "Duty Engineer",
      email: "duty@comuki.local",
      platformRoles: [],
      projectRoles: {},
    })
  })
})

describe("mapMeResponseToSessionUser", () => {
  function meFixture(overrides: Partial<MeResponse> = {}): MeResponse {
    return {
      userId: "00000000-0000-0000-0000-000000000001",
      subjectType: "user",
      subjectId: "00000000-0000-0000-0000-000000000001",
      email: "duty@comuki.local",
      displayName: "Duty Engineer",
      roles: ["operator", "approver", "viewer"],
      permissions: { platform: [], projects: {} },
      ...overrides,
    }
  }

  it("uses userId as the session id for user-typed calls", () => {
    const session = mapMeResponseToSessionUser(meFixture())

    expect(session.id).toBe("00000000-0000-0000-0000-000000000001")
  })

  it("falls back to subjectId when userId is null (api-key call)", () => {
    const session = mapMeResponseToSessionUser(
      meFixture({ userId: null, subjectId: "key-uuid-here" }),
    )

    expect(session.id).toBe("key-uuid-here")
  })

  it("filters roles to platform-scope for platformRoles", () => {
    const session = mapMeResponseToSessionUser(
      meFixture({ roles: ["operator", "approver", "viewer", "platform-admin"] }),
    )

    expect(session.platformRoles).toEqual(["operator", "platform-admin"])
    expect(session.platformRoles).not.toContain("approver")
    expect(session.platformRoles).not.toContain("viewer")
  })

  it("leaves projectRoles empty — the wire does not carry per-project roles", () => {
    const session = mapMeResponseToSessionUser(
      meFixture({
        roles: ["approver", "viewer"],
        permissions: {
          platform: [],
          projects: { "p_comuki": ["plans.approve", "runs.view"] },
        },
      }),
    )

    expect(session.projectRoles).toEqual({})
  })

  it("treats missing email and displayName as empty strings rather than null", () => {
    const session = mapMeResponseToSessionUser(
      meFixture({ email: undefined, displayName: undefined }),
    )

    expect(session.email).toBe("")
    expect(session.name).toBe("")
  })

  it("carries an empty roles array through as no platform roles", () => {
    const session = mapMeResponseToSessionUser(meFixture({ roles: [] }))

    expect(session.platformRoles).toEqual([])
  })
})

describe("mapOidcStartToAuthorizationUrl", () => {
  it("returns a string response as the authorization URL", () => {
    const url = mapOidcStartToAuthorizationUrl(
      "https://idp.example.com/auth?state=abc",
    )

    expect(url).toBe("https://idp.example.com/auth?state=abc")
  })

  it("throws on non-string responses with a message that names the kubb follow-redirect behaviour", () => {
    // Object / null / undefined: anything kubb could conceivably surface
    // that is not a string. An HTML page *body* (`"<!doctype html>…")` is
    // a string and is accepted as-is — the mapper cannot tell a real URL
    // from a string that merely looks like one, and the kubb follow-
    // redirect behaviour means real mode will land here often.
    expect(() => mapOidcStartToAuthorizationUrl({ location: "/" })).toThrow(
      /OIDC start did not return a string URL/i,
    )
    expect(() => mapOidcStartToAuthorizationUrl(null)).toThrow(
      /OIDC start did not return a string URL/i,
    )
    expect(() => mapOidcStartToAuthorizationUrl(undefined)).toThrow(
      /OIDC start did not return a string URL/i,
    )
    expect(() => mapOidcStartToAuthorizationUrl(42)).toThrow(
      /OIDC start did not return a string URL/i,
    )
  })
})

/**
 * Wire → domain for the identity-admin list endpoints (F13 / issue #45).
 *
 * The kubb wire carries less than the seed the snapshot builder expects;
 * these tests pin the gap-fill behaviour documented in each mapper.
 * Anything that becomes a "real" wire column tomorrow is a one-test edit.
 */

const SAMPLE_USER_VIEW: UserAccountView = {
  id: { value: "u_alice" },
  email: "alice@example.com",
  displayName: "Alice",
  disabled: false,
  tokensVersion: 1,
  createdAt: "2026-01-01T00:00:00+00:00",
}

describe("mapUserAccountViewToSeed", () => {
  it("carries id, displayName->name, email and createdAt through", () => {
    const seed = mapUserAccountViewToSeed(SAMPLE_USER_VIEW)

    expect(seed.id).toBe("u_alice")
    expect(seed.name).toBe("Alice")
    expect(seed.email).toBe("alice@example.com")
    expect(seed.createdAt).toBe("2026-01-01T00:00:00+00:00")
  })

  it("fills the disabled -> status gap with 'disabled'", () => {
    const seed = mapUserAccountViewToSeed({ ...SAMPLE_USER_VIEW, disabled: true })

    expect(seed.status).toBe("disabled")
  })

  it("fills the disabled -> status gap with 'active'", () => {
    const seed = mapUserAccountViewToSeed(SAMPLE_USER_VIEW)

    expect(seed.status).toBe("active")
  })

  it("sets oidcSubject and lastSeenAt to null because the wire does not carry them", () => {
    const seed = mapUserAccountViewToSeed(SAMPLE_USER_VIEW)

    expect(seed.oidcSubject).toBeNull()
    expect(seed.lastSeenAt).toBeNull()
  })
})

const SAMPLE_GRANT_VIEW: RoleAssignmentView = {
  id: { value: "g1" },
  role: "platform-admin",
  scopeLevel: "platform",
  scopeProjectId: null,
  subjectType: "user",
  subjectId: "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  grantedBy: null,
  createdAt: "2026-02-02T00:00:00+00:00",
  revokedAt: null,
  isActive: true,
}

describe("mapRoleAssignmentViewToSeed", () => {
  it("carries subject kind/id, role, scope and grantedAt through", () => {
    const seed = mapRoleAssignmentViewToSeed(SAMPLE_GRANT_VIEW)

    expect(seed.id).toBe("g1")
    expect(seed.subjectKind).toBe("user")
    expect(seed.subjectId).toBe(SAMPLE_GRANT_VIEW.subjectId)
    expect(seed.role).toBe("platform-admin")
    expect(seed.projectId).toBeNull()
    expect(seed.grantedAt).toBe("2026-02-02T00:00:00+00:00")
  })

  it("maps api-key subjects through to the seed kinds", () => {
    const seed = mapRoleAssignmentViewToSeed({
      ...SAMPLE_GRANT_VIEW,
      subjectType: "api-key",
      subjectId: "k1",
    })

    expect(seed.subjectKind).toBe("api-key")
    expect(seed.subjectId).toBe("k1")
  })

  it("maps a project-scope grant to a non-null project id", () => {
    const seed = mapRoleAssignmentViewToSeed({
      ...SAMPLE_GRANT_VIEW,
      scopeLevel: "project",
      scopeProjectId: "p_comuki",
    })

    expect(seed.projectId).toBe("p_comuki")
  })
})

const SAMPLE_KEY_VIEW: ApiKeyView = {
  id: "k1",
  userId: "u1",
  name: "ci-pipeline",
  prefix: "cmk_4e9c",
  tenantProjectId: null,
  createdAt: "2026-03-03T00:00:00+00:00",
  lastUsedAt: "2026-03-04T00:00:00+00:00",
  revokedAt: null,
  isActive: true,
}

describe("mapApiKeyViewToSeed", () => {
  it("carries id, name, prefix and createdAt through and never exposes a secret", () => {
    const seed = mapApiKeyViewToSeed(SAMPLE_KEY_VIEW)

    expect(seed.id).toBe("k1")
    expect(seed.name).toBe("ci-pipeline")
    expect(seed.prefix).toBe("cmk_4e9c")
    expect(seed.createdAt).toBe("2026-03-03T00:00:00+00:00")
    expect(seed.lastUsedAt).toBe("2026-03-04T00:00:00+00:00")
    // The wire view carries no field that exposes a secret — we leave it
    // at the type-level to make the contract explicit in unit tests.
    expect("Plaintext" in (seed as unknown as Record<string, unknown>)).toBe(false)
  })

  it("maps isActive onto the active / revoked status", () => {
    const active = mapApiKeyViewToSeed(SAMPLE_KEY_VIEW)
    const revoked = mapApiKeyViewToSeed({ ...SAMPLE_KEY_VIEW, isActive: false })

    expect(active.status).toBe("active")
    expect(revoked.status).toBe("revoked")
  })

  it("fills expiresAt with null because the wire does not carry an expiry", () => {
    const seed = mapApiKeyViewToSeed(SAMPLE_KEY_VIEW)

    expect(seed.expiresAt).toBeNull()
  })
})

describe("page-level list mappers", () => {
  it("maps an identity users page to seed users", () => {
    expect(mapIdentityUsersPageToSeedUsers({ items: [SAMPLE_USER_VIEW] })).toHaveLength(1)
  })

  it("maps a grants page to seed grants", () => {
    expect(mapGrantsPageToSeedGrants({ items: [SAMPLE_GRANT_VIEW] })).toHaveLength(1)
  })

  it("maps an api-keys page to seed keys", () => {
    expect(mapApiKeysPageToSeedKeys({ items: [SAMPLE_KEY_VIEW] })).toHaveLength(1)
  })
})