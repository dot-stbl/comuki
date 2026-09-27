import { Link } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

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
  const { t } = useTranslation("cost")
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
        /* Spend is what the rows are ordered by, and the cap the column cannot
           fit rides in the cell's `title`. */
        title:
          row.cap > 0
            ? t("topProjects.ofCap", { cap: `$${row.cap.toFixed(0)}` })
            : t("topProjects.noCap"),
        "data-test": "top-projects-spend",
      },
    ],
  }))

  return (
    <RankedTable
      label={t("table.project")}
      columns={[
        { label: t("column.share") },
        { label: t("column.spend"), strong: true, help: true },
      ]}
      rows={ranked}
      empty={t("topProjects.empty")}
      data-test="top-projects"
      rowTest="top-projects-row"
      emptyTest="top-projects-empty"
      className={className}
    />
  )
}
