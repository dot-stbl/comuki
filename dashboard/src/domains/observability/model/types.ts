/**
 * Observability as the screen sees it — and the shape is a *negative* design
 * decision made structural.
 *
 * The requirements are explicit: **do not embed a Grafana iframe.** So there is
 * no panel id here, no time range, no datasource, no refresh interval — nothing
 * an embed would need. A record that quietly kept those fields would be an
 * invitation to build the thing that was ruled out, and the reason it was ruled
 * out is not laziness: infra logs and run timelines are read on different
 * clocks, by different people, answering different questions, and a surface
 * that showed both would teach an operator to look for a run's story in a
 * metrics board.
 *
 * What is left is exactly what a page of links needs: where the board is, what
 * question it answers, and what to do when it is not there yet.
 */

/** The boards the platform ships definitions for. Three, and no more in v1. */
export type BoardKind = "runs" | "workers" | "cost"

export interface Board {
  kind: BoardKind
  title: string
  /** What question it answers, in one line. */
  summary: string
  /** Grafana's own identifier for the dashboard — a value, not prose. */
  uid: string
  /**
   * Where to open it. `null` when the definition exists in our repo but has not
   * been imported into this Grafana yet — a different problem from having no
   * Grafana at all, and the page says so differently.
   */
  url: string | null
  updatedAt: string
}

export interface Grafana {
  baseUrl: string
  org: string
  version: string
}

/** Where the board definitions live — ours, not the client's. */
export interface BoardsRepo {
  repo: string
  path: string
  url: string
}

export interface ObservabilitySnapshot {
  /** `null` when no Grafana is configured for this platform at all. */
  grafana: Grafana | null
  boards: Board[]
  boardsRepo: BoardsRepo
}
