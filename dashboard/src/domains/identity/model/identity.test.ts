import { describe, expect, it } from "vitest"

import {
  API_KEYS_SEED,
  ROLE_ASSIGNMENTS_SEED,
  USERS_SEED,
} from "@/shared/api/mock/identity.seed"
import { PLATFORM_PROJECTS_SEED } from "@/shared/api/mock/projects.seed"

import { buildIdentitySnapshot, daysUntil } from "./identity"

/** A fixed day, so an expiry test is about two dates and not about today. */
const TODAY = new Date("2026-08-30T09:00:00Z")

function snapshot() {
  return buildIdentitySnapshot(
    USERS_SEED,
    ROLE_ASSIGNMENTS_SEED,
    API_KEYS_SEED,
    PLATFORM_PROJECTS_SEED,
    TODAY
  )
}

describe("how long a key has left", () => {
  it("counts whole days to the expiry", () => {
    expect(daysUntil("2026-09-02", TODAY)).toBe(3)
    expect(daysUntil("2026-08-30", TODAY)).toBe(0)
  })

  it("goes negative once the day has passed", () => {
    expect(daysUntil("2026-08-27", TODAY)).toBe(-3)
  })

  it("says nothing about a key with no expiry", () => {
    expect(daysUntil(null, TODAY)).toBeNull()
  })
})

describe("the three lists, joined", () => {
  it("resolves a project grant to the slug the product shows", () => {
    const { grants } = snapshot()
    const onAtlas = grants.find((grant) => grant.id === "g_duty_atlas")

    expect(onAtlas?.scopeLabel).toBe("atlas")
    expect(onAtlas?.role).toBe("project-admin")
  })

  it("calls a platform grant by its scope and not by a blank", () => {
    const { grants } = snapshot()

    expect(
      grants.find((grant) => grant.id === "g_rhea_platform")?.scopeLabel
    ).toBe("platform")
  })

  it("shows somebody who holds two projects and no platform standing", () => {
    const { users } = snapshot()
    const nadia = users.find((user) => user.id === "u_nadia")

    expect(nadia?.scopes).toEqual(["comuki", "atlas"])
    expect(nadia?.scopes).not.toContain("platform")
  })

  it("keeps a disabled account's grant and marks it inert", () => {
    // Disabling somebody and un-granting them are different acts. The row has
    // to carry both facts at once or the screen is lying about one of them.
    const { users, grants } = snapshot()

    expect(users.find((user) => user.id === "u_tomas")?.status).toBe("disabled")
    const grant = grants.find((entry) => entry.id === "g_tomas_platform")
    expect(grant).toBeDefined()
    expect(grant?.subjectInactive).toBe(true)
  })

  it("identifies a key subject by its prefix, which is all there is", () => {
    const { grants } = snapshot()
    const ci = grants.find((grant) => grant.id === "g_ci_comuki")

    expect(ci?.subjectKind).toBe("api-key")
    expect(ci?.subjectLabel).toBe("cmk_4e9c")
  })

  it("summarises what each key opens", () => {
    const { keys } = snapshot()

    expect(keys.find((key) => key.id === "k_ci")?.grants).toEqual([
      "member on comuki",
    ])
    expect(keys.find((key) => key.id === "k_legacy")?.grants).toEqual([])
  })

  it("carries the awkward keys: never used, and days from the end", () => {
    const { keys } = snapshot()

    expect(keys.find((key) => key.id === "k_mcp")?.lastUsedAt).toBeNull()
    expect(keys.find((key) => key.id === "k_audit")?.expiresInDays).toBe(3)
    expect(keys.find((key) => key.id === "k_ci")?.expiresInDays).toBeNull()
  })

  it("falls back to the id for a scope the registry no longer lists", () => {
    const { grants } = buildIdentitySnapshot(
      USERS_SEED,
      [
        {
          id: "g_ghost",
          subjectKind: "user",
          subjectId: "u_duty",
          role: "viewer",
          projectId: "p_gone",
          grantedAt: "2026-01-01",
        },
      ],
      API_KEYS_SEED,
      PLATFORM_PROJECTS_SEED,
      TODAY
    )

    // A worse label than a slug, and a far better one than a blank cell that
    // reads as a broken join.
    expect(grants[0].scopeLabel).toBe("p_gone")
  })
})
