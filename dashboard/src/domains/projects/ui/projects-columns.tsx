import { Link } from "@tanstack/react-router"

import { formatCost } from "@/domains/runs/model/format"
import type { ProjectRow } from "@/domains/projects/model/types"
import { numericSort, type DataColumn } from "@/shared/ui"

import styles from "./projects-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getProjectId = (project: ProjectRow) => project.id

/**
 * The registry's column declarations.
 *
 * A project that exists is running work and spending money, so the row says so:
 * the handle and the name identify it, and everything after that is what it is
 * *doing*. A list that only proved the row existed would not be worth the trip
 * to a screen visited once a month.
 *
 * Three of the columns can be genuinely absent — a project with no profile
 * repository runs on the platform's defaults, and a project created this
 * morning has no runs and no line in the cost report. Each degrades to a dash
 * rather than to a blank, because a blank cell reads as a rendering fault and a
 * dash reads as a fact.
 *
 * No factory argument and no session: §14 is a list and a create form, and
 * nothing on a row is an act. The moment one is, this takes the session the way
 * `createRunColumns` does — `cell` is called as a plain function while the
 * table builds a row, so a hook inside one throws.
 */
export function createProjectColumns(): DataColumn<ProjectRow>[] {
  return [
    {
      accessorKey: "slug",
      header: "slug",
      // The identifier cell is the way in, exactly as the run id is on the
      // duty list. The *cell*, not the row: a row-wide click target swallows
      // whatever an actions column puts on the row, and this list is one act
      // away from having one — the moment it does, a row-wide link would be
      // eating the button beside it and nobody would know why.
      cell: ({ row }) => (
        <Link
          to="/projects/$projectId"
          params={{ projectId: row.original.id }}
          className={styles.slug}
          data-test="project-link"
        >
          {row.original.slug}
        </Link>
      ),
      meta: {
        width: 132,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: "filter slug, name, repository…",
          match: (project, needle) =>
            `${project.slug} ${project.name} ${project.gitProfileRepo ?? ""}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "name",
      header: "name",
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.name}>
          {row.original.name}
        </span>
      ),
    },
    {
      accessorKey: "activeRuns",
      header: "in flight",
      cell: ({ row }) => {
        const count = row.original.activeRuns
        return count > 0 ? (
          <span className={styles.active}>{count}</span>
        ) : (
          <span className={styles.absent}>—</span>
        )
      },
      meta: { width: 88, numeric: true, label: "in flight" },
    },
    {
      accessorKey: "totalRuns",
      header: "runs",
      cell: ({ row }) => {
        const count = row.original.totalRuns
        return count > 0 ? count : <span className={styles.absent}>—</span>
      },
      meta: { width: 80, numeric: true },
    },
    {
      accessorKey: "spendToday",
      header: "cost today",
      // `null` is "not measured" and it has to sort somewhere; `numericSort`
      // already parks blanks last, which is where an unmeasured project
      // belongs whichever way the column is pointed.
      sortFn: numericSort,
      cell: ({ row }) => {
        const spend = row.original.spendToday
        return spend === null ? (
          <span className={styles.absent}>—</span>
        ) : (
          formatCost(spend)
        )
      },
      meta: { width: 104, numeric: true, label: "cost today" },
    },
    {
      accessorKey: "gitProfileRepo",
      header: "profiles",
      cell: ({ row }) => {
        const repo = row.original.gitProfileRepo
        return repo ? (
          <span className={styles.repo} title={repo}>
            {repo}
          </span>
        ) : (
          // Not missing — running on the platform's own profiles, which is a
          // legitimate way for a project to be configured.
          <span className={styles.absent}>platform defaults</span>
        )
      },
      meta: { label: "profiles" },
    },
    {
      accessorKey: "createdAt",
      header: "created",
      cell: ({ row }) => (
        <span className={styles.created}>{row.original.createdAt}</span>
      ),
      meta: { width: 104 },
    },
  ]
}
