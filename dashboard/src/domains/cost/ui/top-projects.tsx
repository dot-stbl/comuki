import { Link } from "@tanstack/react-router"

import type { CostTopProject } from "@/domains/cost/model/cost"
import { projectAxis, projectShare } from "@/domains/cost/model/cost"
import { RankedTable, type RankedRow } from "@/domains/cost/ui/ranked-table"

import styles from "./top-projects.module.css"

export interface TopProjectsProps {
  rows: CostTopProject[]
  /** How many rows to show. The seed ships 14 projects; the screen caps at 7. */
  limit?: number
  className?: string
}

/** The columns, beside the project the row names. */
const COLUMNS = [
  { label: "share" },
  /* Spend is what the rows are ordered by, and the cap the column cannot fit
     rides in the cell's `title`. */
  { label: "spend", strong: true, help: true },
] as const

/**
 * The top-N projects by spend for the active period.
 *
 * A ranking, in the report's one ranking construction — the same `RankedTable`
 * the per-app and per-model breakdowns take, because it is the same question
 * asked about a different noun: which one is expensive, and by how much against
 * the one above it.
 *
 * Clicking the project key hands the operator off to `/projects/$id`
 * — the project's own surface — because a list that names a project and
 * then asks the operator to go looking for it has a row whose right end
 * is missing.
 */
export function TopProjects({ rows, limit = 7, className }: TopProjectsProps) {
  const visible = rows.slice(0, limit)
  const axis = projectAxis(visible)
  const total = rows.reduce((sum, row) => sum + row.spend, 0)

  const ranked: RankedRow[] = visible.map((row) => ({
    id: row.projectId,
    label: (
      <Link
        to="/projects/$projectId"
        params={{ projectId: row.projectId }}
        className={styles.projectKey}
      >
        {row.projectKey}
      </Link>
    ),
    share: projectShare(row, axis),
    figures: [
      {
        value: total > 0 ? `${Math.round((row.spend / total) * 100)}%` : "0%",
        "data-test": "top-projects-share",
      },
      {
        value: `$${row.spend.toFixed(0)}`,
        title: row.cap > 0 ? `of $${row.cap.toFixed(0)} cap` : "no cap",
        "data-test": "top-projects-spend",
      },
    ],
  }))

  return (
    <RankedTable
      label="project"
      columns={COLUMNS}
      rows={ranked}
      empty="no project spend this period"
      data-test="top-projects"
      rowTest="top-projects-row"
      emptyTest="top-projects-empty"
      className={className}
    />
  )
}
