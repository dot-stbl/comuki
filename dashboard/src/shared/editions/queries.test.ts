import type { EditionView } from "@/shared/api/_generated/types/EditionView"
import type { FeatureAvailabilityView } from "@/shared/api/_generated/types/FeatureAvailabilityView"
import type { LimitUsageView } from "@/shared/api/_generated/types/LimitUsageView"

import {
  isEditionStatus,
  isEditionTier,
  normalizeEditionStatus,
  useFeature,
  wireToSnapshot,
} from "@/shared/editions/queries"
import {
  COMMUNITY_EDITION_SNAPSHOT,
  type EditionSnapshot,
} from "@/shared/editions/model"
import { renderHook } from "@testing-library/react"
import { describe, expect, it } from "vitest"

/** Mirror of the kubb-generated wire shape but with deliberate numeric-string
 *  coercion in the limit fields so the mapper's Number(...) path is
 *  exercised: the host's serializer sometimes hands numbers across as
 *  strings. */
function wire(
  overrides: Partial<{
    expiresAt: string | null
    tier: string
    status: string
    limits: Partial<LimitUsageView>[]
    features: Partial<FeatureAvailabilityView>[]
  }> = {},
): EditionView {
  return {
    tier: overrides.tier ?? "team",
    status: overrides.status ?? "valid",
    version: "1.2.3",
    features: (overrides.features ?? [
      { key: "multi-repo", available: true },
      { key: "white-label", available: false },
    ]) as FeatureAvailabilityView[],
    limits: (overrides.limits ?? [
      { key: "projects", current: 4, cap: 10 },
    ]) as LimitUsageView[],
    expiresAt: overrides.expiresAt === undefined ? "2099-12-31T00:00:00Z" : overrides.expiresAt,
  }
}

describe("edition wire -> snapshot mapper", () => {
  it("Round-trips every field, including expiresAt, when the wire has a value", () => {
    const snapshot = wireToSnapshot(wire({ expiresAt: "2099-12-31T00:00:00Z" }))

    expect(snapshot.tier).toBe("team")
    expect(snapshot.status).toBe("valid")
    expect(snapshot.version).toBe("1.2.3")
    expect(snapshot.expiresAt).toBe("2099-12-31T00:00:00Z")
    expect(snapshot.features).toHaveLength(2)
    expect(snapshot.features[0]?.key).toBe("multi-repo")
    expect(snapshot.features[0]?.available).toBe(true)
    expect(snapshot.features[1]?.key).toBe("white-label")
    expect(snapshot.features[1]?.available).toBe(false)
    expect(snapshot.limits).toHaveLength(1)
    expect(snapshot.limits[0]?.current).toBe(4)
    expect(snapshot.limits[0]?.cap).toBe(10)
  })

  it("Projects null expiresAt into undefined so the page branches on presence (null vs omitted are now both 'no expiry applies')", () => {
    const snapshot = wireToSnapshot(wire({ expiresAt: null }))

    expect(snapshot.expiresAt).toBeUndefined()
  })

  it("Coerces numeric limit fields from strings (the wire type is number | string)", () => {
    const snapshot = wireToSnapshot(
      wire({
        limits: [{ key: "projects", current: "7", cap: "10" }],
      }),
    )

    expect(snapshot.limits[0]?.current).toBe(7)
    expect(snapshot.limits[0]?.cap).toBe(10)
  })

  it("Passes any string tier through unchanged (the tier union is open — closed-vocabulary only at 'community')", () => {
    const snapshot = wireToSnapshot(wire({ tier: "team-extra" }))

    expect(snapshot.tier).toBe("team-extra")
  })

  it("Falls back to the documented 'absent' status when the wire carries an unknown word", () => {
    // A host that has rolled out a status the FE has not been taught
    // should degrade the row, not throw — the partial-rollout scenario
    // documented alongside normalizeEditionStatus.
    const snapshot = wireToSnapshot(wire({ status: "scheduled-grace-2" }))

    expect(snapshot.status).toBe("absent")
  })

  it("Passes every known status through the guard unchanged", () => {
    for (const known of ["valid", "grace", "expired", "absent"]) {
      expect(wireToSnapshot(wire({ status: known })).status).toBe(known)
    }
  })
})

describe("isEditionTier", () => {
  it("Accepts every string tier value (the union is open)", () => {
    expect(isEditionTier("community")).toBe(true)
    expect(isEditionTier("team")).toBe(true)
    expect(isEditionTier("anything-future")).toBe(true)
  })
})

describe("isEditionStatus", () => {
  it("Accepts every closed vocabulary word", () => {
    for (const known of ["valid", "grace", "expired", "absent"]) {
      expect(isEditionStatus(known)).toBe(true)
    }
  })

  it("Rejects unknown words so the mapper has somewhere to fall back", () => {
    expect(isEditionStatus("scheduled-grace-2")).toBe(false)
    expect(isEditionStatus("")).toBe(false)
  })
})

describe("normalizeEditionStatus", () => {
  it("Round-trips the four known words", () => {
    expect(normalizeEditionStatus("valid")).toBe("valid")
    expect(normalizeEditionStatus("grace")).toBe("grace")
    expect(normalizeEditionStatus("expired")).toBe("expired")
    expect(normalizeEditionStatus("absent")).toBe("absent")
  })

  it("Degrades unknown words to the documented 'absent' fallback (no throw)", () => {
    expect(normalizeEditionStatus("renewing")).toBe("absent")
    expect(normalizeEditionStatus("")).toBe("absent")
  })
})

describe("COMMUNITY_EDITION_SNAPSHOT", () => {
  it("Marks every paid feature unavailable and lands on Community / absent", () => {
    expect(COMMUNITY_EDITION_SNAPSHOT.tier).toBe("community")
    expect(COMMUNITY_EDITION_SNAPSHOT.status).toBe("absent")
    expect(COMMUNITY_EDITION_SNAPSHOT.expiresAt).toBeUndefined()
    expect(COMMUNITY_EDITION_SNAPSHOT.features.every((row) => row.available === false)).toBe(true)
    expect(COMMUNITY_EDITION_SNAPSHOT.limits).toContainEqual(
      expect.objectContaining({ key: "projects" }),
    )
  })
})

describe("useFeature", () => {
  it("Returns undefined while the snapshot is still loading", () => {
    const { result } = renderHook(() => useFeature(undefined, "multi-repo"))
    expect(result.current).toBeUndefined()
  })

  it("Returns the snapshot's answer for a feature the snapshot names (Community: every paid feature is false)", () => {
    const snapshot: EditionSnapshot = COMMUNITY_EDITION_SNAPSHOT
    const { result } = renderHook(() => useFeature(snapshot, "agenteval"))

    expect(result.current).toBe(false)
  })

  it("Returns undefined for a feature the snapshot does not name", () => {
    const { result } = renderHook(() => useFeature(COMMUNITY_EDITION_SNAPSHOT, "no-such-feature"))
    expect(result.current).toBeUndefined()
  })
})
