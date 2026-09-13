import { Link } from "@tanstack/react-router"

import type { CostTopProject } from "@/domains/cost/model/cost"
import { projectAxis, projectShare } from "@/domains/cost/model/cost"
import { cn } from "@/shared/lib/utils"

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
 * A small list rather than a chart, because the reading is "which project
 * is expensive and by how much against the rest" — and a table that says
 * the project key, the spend, the share of total, and the bar against the
 * largest spend is the only shape that lets the operator compare four facts
 * about the same row at once. The mini-bar is on top of the spend it sits
 * beside, never on its own.
 *
 * Clicking the project key hands the operator off to `/projects/$id`
 * — the project's own surface — because a list that names a project and
 * then asks the operator to go looking for it has a row whose right end
 * is missing.
 */
export function TopProjects({ rows, limit = 7, className }: TopProjectsProps) {
  const visible = rows.slice(0, limit)
  const axis = projectAxis(visible)

  if (visible.length === 0) {
    return (
      <p className={cn(styles.empty, className)} data-test="top-projects-empty">
        no project spend this period
      </p>
    )
  }

  const total = rows.reduce((sum, row) => sum + row.spend, 0)

  return (
    <table
      className={cn(styles.table, className)}
      data-test="top-projects"
    >
      <thead>
        <tr>
          <th scope="col" className={styles.headProject}>
            project
          </th>
          <th scope="col" className={styles.headShare}>
            share
          </th>
          <th scope="col" className={styles.headSpend}>
            spend
          </th>
        </tr>
      </thead>
      <tbody>
        {visible.map((row) => (
          <tr
            key={row.projectId}
            className={styles.row}
            data-test="top-projects-row"
            data-project={row.projectId}
          >
            <td className={styles.projectCell}>
              <Link
                to="/projects/$projectId"
                params={{ projectId: row.projectId }}
                className={styles.projectKey}
              >
                {row.projectKey}
              </Link>
              <span className={styles.projectBar} aria-hidden="true">
                <span
                  className={styles.projectBarFill}
                  style={{
                    inlineSize: `${Math.round(projectShare(row, axis) * 100)}%`,
                  }}
                />
              </span>
            </td>
            <td className={styles.share} data-test="top-projects-share">
              {total > 0
                ? `${Math.round((row.spend / total) * 100)}%`
                : "0%"}
            </td>
            <td
              className={styles.spend}
              data-test="top-projects-spend"
              title={row.cap > 0 ? `of $${row.cap.toFixed(0)} cap` : "no cap"}
            >
              ${row.spend.toFixed(0)}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
