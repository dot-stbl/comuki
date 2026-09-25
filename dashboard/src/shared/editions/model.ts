/**
 * The closed vocabulary of edition statuses the host emits. Mirrors the
 * backend `LicenseStatus` smart-type's lowercase `Value` form so the
 * snapshot can be compared against this union without translation.
 */
export type EditionStatus = "valid" | "grace" | "expired" | "absent"

/**
 * The closed vocabulary of tier codes the host emits. New tiers are added
 * on the backend by appending rows to the `EditionTiers` catalog — the
 * UI treats every value as a closed string.
 */
export type EditionTier = "community" | (string & {})

/**
 * One row in the edition's feature list. `available` reflects what the
 * current license covers; the page renders a lock affordance on `false`
 * rather than hiding the entry outright, so a Community reader can see
 * every paid capability the upgrade would unlock.
 */
export interface EditionFeatureAvailability {
  readonly key: string
  readonly available: boolean
}

/**
 * One row in the edition's limit list. `current` is the best-effort count
 * from the matching provider (0 when no provider is registered);
 * `cap` is the effective numeric cap at the current tier.
 */
export interface EditionLimitUsage {
  readonly key: string
  readonly current: number
  readonly cap: number
}

/**
 * The page's projection of the host's `/api/v1/edition` response.
 *
 * `expiresAt` is intentionally optional here, not nullable: a Community
 * license omits the property entirely so the page can branch on its
 * presence rather than its value. The kubb-generated wire DTO carries
 * it as `string | null` because the kubb JSON schema does not model
 * "property omitted" the way `JsonIgnoreCondition.WhenWritingNull`
 * does at the C# layer; the mapper at the edge translates the absent
 * wire to `undefined`.
 */
export interface EditionSnapshot {
  readonly tier: EditionTier
  readonly status: EditionStatus
  readonly features: ReadonlyArray<EditionFeatureAvailability>
  readonly limits: ReadonlyArray<EditionLimitUsage>
  readonly version: string
  readonly expiresAt?: string
}

/**
 * The Community snapshot the dashboard renders when the mock plane is
 * active. Mirrors what the host would serve for a fresh boot with no
 * `Host:License:Path` configured: every paid feature `available: false`,
 * every limit at the Community cap. The single source of truth for the
 * mock-first edition read — `useEdition` serves it verbatim when
 * `env.useMock` is on, so mock screens and tests never hand-roll a copy.
 */
export const COMMUNITY_EDITION_SNAPSHOT: EditionSnapshot = {
  tier: "community",
  status: "absent",
  features: [
    { key: "enterprise-sso", available: false },
    { key: "scale-isolation", available: false },
    { key: "infra-memory", available: false },
    { key: "background-llm-watchers", available: false },
    { key: "agenteval", available: false },
    { key: "white-label", available: false },
    { key: "multi-repo", available: false },
    { key: "worker-commit-attribution", available: false },
  ],
  limits: [{ key: "projects", current: 0, cap: 1 }],
  version: "dev",
}
