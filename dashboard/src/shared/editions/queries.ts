import { useQuery } from "@tanstack/react-query"

import type { EditionView } from "@/shared/api/_generated/types/EditionView"
import type { FeatureAvailabilityView } from "@/shared/api/_generated/types/FeatureAvailabilityView"
import type { LimitUsageView } from "@/shared/api/_generated/types/LimitUsageView"

import { getApiV1Edition } from "@/shared/api/_generated/clients/getApiV1Edition"
import { env } from "@/shared/config/env"

import {
  COMMUNITY_EDITION_SNAPSHOT,
  type EditionSnapshot,
  type EditionStatus,
  type EditionTier,
} from "@/shared/editions/model"

/** The host's `GET /api/v1/edition` is the only editions read; one key. */
export const editionQueryKey = ["edition"] as const

/**
 * Wire `expiresAt` is `string | null` in the kubb schema but `JsonIgnore(WhenWritingNull)`
 * on the host side means it is OMITTED from the JSON when `status == "absent"`.
 * Treat a null and an absent field identically on the page — both are
 * "no expiry applies" — and project to `undefined` so the page branches
 * on presence, not on value.
 */
export function wireToSnapshot(wire: EditionView): EditionSnapshot {
  return {
    tier: wire.tier as EditionTier,
    status: wire.status as EditionStatus,
    features: wire.features.map((feature: FeatureAvailabilityView) => ({
      key: feature.key,
      available: feature.available,
    })),
    limits: wire.limits.map((limit: LimitUsageView) => ({
      key: limit.key,
      current: Number(limit.current),
      cap: Number(limit.cap),
    })),
    version: wire.version,
    expiresAt: wire.expiresAt ?? undefined,
  }
}

/**
 * The edition snapshot the dashboard renders.
 *
 * Mock mode serves a hand-written Community snapshot so mock-first screens
 * have something to render without standing up the host. Real mode fetches
 * `/api/v1/edition` through the kubb-generated client and maps the wire to
 * the page's domain projection at the boundary — the page never imports
 * a kubb DTO.
 */
export function useEdition() {
  return useQuery({
    queryKey: editionQueryKey,
    queryFn: async (): Promise<EditionSnapshot> => {
      if (env.useMock) {
        return COMMUNITY_EDITION_SNAPSHOT
      }

      const wire = await getApiV1Edition()
      return wireToSnapshot(wire)
    },
  })
}

/**
 * `true` when the snapshot is loaded and the named feature is covered,
 * `false` when the snapshot is loaded and the feature is not covered,
 * `undefined` while the snapshot is still loading.
 *
 * `undefined` is the load state on purpose: the page can render a real
 * affordance (the feature is on, show it) instead of a "locked" panel
 * during the first paint. The same component reads this hook and an
 * optimistic loading state; if you want a pessimistic gate, branch on
 * `isLoading` in the consumer.
 */
export function useFeature(
  snapshot: EditionSnapshot | undefined,
  feature: string,
): boolean | undefined {
  if (snapshot === undefined) {
    return undefined
  }

  const row = snapshot.features.find((row) => row.key === feature)
  return row?.available
}
