import { useQuery } from "@tanstack/react-query"

import type { EditionView } from "@/shared/api/_generated/types/EditionView"
import type { FeatureAvailabilityView } from "@/shared/api/_generated/types/FeatureAvailabilityView"
import type { LimitUsageView } from "@/shared/api/_generated/types/LimitUsageView"

import { getApiV1Edition } from "@/shared/api/_generated/clients/getApiV1Edition"
import { env } from "@/shared/config/env"

import type { FeatureKey } from "@/shared/editions/_generated/registry"
import {
  COMMUNITY_EDITION_SNAPSHOT,
  type EditionSnapshot,
  type EditionStatus,
  type EditionTier,
} from "@/shared/editions/model"

/** The host's `GET /api/v1/edition` is the only editions read; one key. */
export const editionQueryKey = ["edition"] as const

/**
 * The closed set of words the host's `EditionView.status` carries today.
 *
 * `EditionStatus` is a closed four-value union (the only one of the two
 * wire projections in this module that is fully closed), so an unknown
 * status can only mean the host has rolled out a status the FE has not
 * been taught — the same partial-rollout scenario
 * `normalizeRunStatus` covers for runs. The mapper below degrades to a
 * documented fallback constant rather than throwing, so a stale FE keeps
 * rendering the page instead of crashing it.
 */
const KNOWN_EDITION_STATUSES: ReadonlySet<string> = new Set<EditionStatus>([
  "valid",
  "grace",
  "expired",
  "absent",
])

/**
 * Narrow a wire `status` string to the closed `EditionStatus` union.
 *
 * Predicate rather than cast, so the closed set above is the only place
 * the four words are written down. Mirrors `isRunStatus` in
 * `domains/runs/api/mappers.ts`.
 */
export function isEditionStatus(value: string): value is EditionStatus {
  return KNOWN_EDITION_STATUSES.has(value)
}

/**
 * The word an unmapped wire `status` degrades to.
 *
 * `absent` is the only remaining word that is both **a real license state
 * the FE knows how to draw** (Community / no license / Settings row) AND
 * **the least misleading reading** for a status the page cannot read:
 * `valid` would tell the operator their license is paid when it is not,
 * `grace` would tell them a real clock is running when it is not, and
 * `expired` would imply a license once existed. `absent` matches the
 * Community fallback the host already uses for an unmapped tier.
 */
const UNKNOWN_EDITION_STATUS: EditionStatus = "absent"

/**
 * Normalise the wire `status` string to the closed `EditionStatus` union.
 *
 * Anything outside the four known words falls through to
 * `UNKNOWN_EDITION_STATUS` rather than throwing — the host may
 * have rolled out a status the FE has not been taught. A partial backend
 * rollout should degrade the row, not take down the screen.
 */
export function normalizeEditionStatus(value: string): EditionStatus {
  return isEditionStatus(value) ? value : UNKNOWN_EDITION_STATUS
}

/**
 * `EditionTier` is an open union: the closed-vocabulary part is
 * `"community"` and new tier codes are added on the backend by appending
 * rows to the catalog. The page treats every value as a closed string, so
 * a guard rather than a cast is the right shape — anything that comes
 * back as a string IS a tier code we are willing to show.
 */
export function isEditionTier(value: string): value is EditionTier {
  return typeof value === "string"
}

/**
 * Wire `expiresAt` is `string | null` in the kubb schema and is ALWAYS
 * present on the wire — the host used to `JsonIgnore(WhenWritingNull)` it,
 * but the OpenAPI document declares `expiresAt` as required+nullable and a
 * strict generated zod schema would reject a Community response that
 * omitted the property. The page projects `null` to `undefined` at the
 * wire→snapshot boundary so the page still branches on presence (a
 * `expiresAt: undefined` row reads as "no expiry applies").
 *
 * `wire.tier` and `wire.status` flow through narrow guards rather than
 * casts: the tier union is open (`"community" | (string & {})`), so a
 * `typeof === "string"` check is the honest shape; the status union is
 * closed (four values), so an unknown value degrades to a documented
 * fallback constant — see `normalizeEditionStatus` for the rationale.
 */
export function wireToSnapshot(wire: EditionView): EditionSnapshot {
  return {
    tier: isEditionTier(wire.tier) ? wire.tier : "community",
    status: normalizeEditionStatus(wire.status),
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
 *
 * The argument is the closed `FeatureKey` union from the codegen
 * registry, so a typo'd key fails the compiler rather than the runtime
 * gate. The snapshot is the source of truth: the kubb-generated wire
 * shape carries the key as a plain string (the host's response is not
 * typed against the codegen), so the comparison stays a string match.
 */
export function useFeature(
  snapshot: EditionSnapshot | undefined,
  feature: FeatureKey,
): boolean | undefined {
  if (snapshot === undefined) {
    return undefined
  }

  const row = snapshot.features.find((row) => row.key === feature)
  return row?.available
}
