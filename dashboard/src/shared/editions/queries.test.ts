import type { EditionView } from "@/shared/api/_generated/types/EditionView"
import type { FeatureAvailabilityView } from "@/shared/api/_generated/types/FeatureAvailabilityView"
import type { LimitUsageView } from "@/shared/api/_generated/types/LimitUsageView"

import { useFeature, wireToSnapshot } from "@/shared/editions/queries"
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
    limits: Partial<LimitUsageView>[]
    features: Partial<FeatureAvailabilityView>[]
  }> = {},
): EditionView {
  return {
    tier: "team",
    status: "valid",
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

  it("Projects null expiresAt into undefined (omitted) so the page branches on presence", () => {
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
