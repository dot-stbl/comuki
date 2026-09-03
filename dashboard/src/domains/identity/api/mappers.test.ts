import { describe, expect, it } from "vitest"

import {
  mapLoginRequestFromInput,
  mapLoginResponseToSessionUser,
  mapMeResponseToSessionUser,
  mapOidcStartToAuthorizationUrl,
} from "@/domains/identity/api/mappers"
import type { MeResponse } from "@/shared/api/_generated/types/MeResponse"

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