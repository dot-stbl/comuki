/**
 * Where the metrics and the logs live — and, deliberately, nothing more.
 *
 * Fictional, like every other seed in this folder: no host, org, uid or
 * timestamp below came from a real Grafana.
 *
 * The shape follows the FE requirements (§15 Observability), and the section's
 * whole design decision is a *negative* one: **do not embed a Grafana iframe**.
 * So there is no panel id here, no time range, no datasource — nothing an embed
 * would need — because the data this seed carries is the data a page of links
 * carries, and a shape that quietly kept the embed's fields would be an
 * invitation to build the thing the requirements ruled out.
 *
 * Two snapshots are exported on purpose. `OBSERVABILITY_SEED` is a platform
 * where somebody already did the work; `OBSERVABILITY_UNCONFIGURED_SEED` is one
 * where nobody has, and it is a seeded state rather than a story-only prop
 * because the first thing any new installation sees is the second one.
 */

/** The boards the platform ships definitions for. Three, and no more in v1. */
export const BOARD_KINDS = ["runs", "workers", "cost"] as const

export type SeedBoardKind = (typeof BOARD_KINDS)[number]

export interface SeedBoard {
  kind: SeedBoardKind
  /** What it is called in Grafana. */
  title: string
  /** What question it answers, in one line. */
  summary: string
  /** Grafana's own identifier for the dashboard — a value, not prose. */
  uid: string
  /**
   * Where to open it. `null` when the definition exists in our repo but has not
   * been imported into this Grafana yet — which is a different problem from
   * having no Grafana at all, and the page says so differently.
   */
  url: string | null
  /** When the definition in our repo last changed. */
  updatedAt: string
}

export interface SeedGrafana {
  baseUrl: string
  org: string
  /** The version the boards were authored against. */
  version: string
}

export interface SeedBoardsRepo {
  repo: string
  path: string
  url: string
}

export interface SeedObservabilitySnapshot {
  /** `null` when no Grafana is configured for this platform at all. */
  grafana: SeedGrafana | null
  boards: SeedBoard[]
  /** Where the board definitions live — ours, not the client's. */
  boardsRepo: SeedBoardsRepo
}

const BOARDS_REPO: SeedBoardsRepo = {
  repo: "comuki/comuki",
  path: "deploy/grafana/dashboards",
  url: "https://github.com/comuki/comuki/tree/main/deploy/grafana/dashboards",
}

export const OBSERVABILITY_SEED: SeedObservabilitySnapshot = {
  grafana: {
    baseUrl: "https://grafana.comuki.internal",
    org: "comuki",
    version: "11.4",
  },
  boards: [
    {
      kind: "runs",
      title: "Comuki · runs",
      summary:
        "throughput, stage latency and failure rate across every project, by profile.",
      uid: "cmk-runs",
      url: "https://grafana.comuki.internal/d/cmk-runs/comuki-runs",
      updatedAt: "2026-08-21",
    },
    {
      kind: "workers",
      title: "Comuki · workers",
      summary:
        "container starts, lease age, idle pool depth and the provider's allocatable.",
      uid: "cmk-workers",
      url: "https://grafana.comuki.internal/d/cmk-workers/comuki-workers",
      updatedAt: "2026-08-26",
    },
    {
      // Defined in our repo, never imported here. A different failure from
      // "no Grafana", and it takes a different sentence.
      kind: "cost",
      title: "Comuki · cost",
      summary:
        "tokens and spend per project, per profile and per model endpoint.",
      uid: "cmk-cost",
      url: null,
      updatedAt: "2026-08-29",
    },
  ],
  boardsRepo: BOARDS_REPO,
}

/**
 * A platform with no boards at all — the state every new installation opens in.
 *
 * Grafana is `null` rather than an empty string: nothing is configured, which
 * is not the same as a Grafana that is configured and has nothing in it. The
 * boards still list, because their definitions live in our repository and exist
 * whether or not anyone has imported them; every one of them has a `url` of
 * `null`, so the page has three things to say how to import and nowhere to send
 * anybody yet.
 */
export const OBSERVABILITY_UNCONFIGURED_SEED: SeedObservabilitySnapshot = {
  grafana: null,
  boards: OBSERVABILITY_SEED.boards.map((board) => ({ ...board, url: null })),
  boardsRepo: BOARDS_REPO,
}
