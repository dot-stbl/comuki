import { Link } from "@tanstack/react-router"

import { formatCost } from "@/domains/runs/model/format"
import type { ProjectRow } from "@/domains/projects/model/types"
import { ProjectMark } from "@/domains/projects/ui/project-mark"
import { projectAccentStyle } from "@/domains/projects/ui/project-accent"
import { i18n } from "@/shared/i18n"
import { badgeShell, numericSort, type DataColumn } from "@/shared/ui"
import { cn } from "@/shared/lib/utils"

import styles from "./projects-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export function getProjectId(project: ProjectRow): string {
  return project.id
}

/**
 * The registry's column declarations.
 *
 * A project that exists is running work and spending money, so the row says so:
 * the handle and the name identify it, and everything after that is what it is
 * *doing*. A list that only proved the row existed would not be worth the trip
 * to a screen visited once a month.
 *
 * The row also carries the project's chosen identity — the mark beside the
 * handle (stored override, or a brand derived from the profiles host) and its
 * tags. Both degrade honestly: no icon falls through to the derived mark, no
 * colour leaves the dot on the surface's muted ink, no tags render a dash in
 * the vocabulary column the way an unmeasured counter renders one.
 *
 * Three of the columns can be genuinely absent — a project with no profile
 * repository runs on the platform's defaults, and a project created this
 * morning has no runs and no line in the cost report. Each degrades to a dash
 * rather than to a blank, because a blank cell reads as a rendering fault and a
 * dash reads as a fact.
 *
 * Copy arrives as a parameter because `cell` is called as a plain function
 * while the table builds a row, so a `useTranslation` inside one throws. The
 * page passes its hook-bound `t` (a language change re-renders the page and
 * rebuilds the columns); tests and stories fall back to the shared instance,
 * which answers in the active locale — `en` — without either having to know
 * the machinery exists.
 */
export type ProjectsTranslator = (
  key: string,
  options?: Record<string, unknown>
) => string

function sharedProjectsT(
  key: string,
  options?: Record<string, unknown>
): string {
  return i18n.t(key, { ...options, ns: "projects" })
}

export function createProjectColumns(
  t: ProjectsTranslator = sharedProjectsT
): DataColumn<ProjectRow>[] {
  return [
    {
      accessorKey: "slug",
      header: t("column.slug"),
      // The identifier cell is the way in, exactly as the run id is on the
      // duty list. The *cell*, not the row: a row-wide click target swallows
      // whatever an actions column puts on the row, and this list is one act
      // away from having one — the moment it does, a row-wide link would be
      // eating the button beside it and nobody would know why.
      //
      // The mark and its accent dot lead the handle: the dot is the row's
      // colour, set as the one custom property and consumed by the module —
      // decoration beside the words that identify, never instead of them.
      cell: ({ row }) => (
        <span
          className={styles.identity}
          style={projectAccentStyle(row.original.color)}
        >
          <ProjectMark project={row.original} size="sm" />
          <span className={styles.accentDot} aria-hidden="true" />
          <Link
            to="/projects/$projectId"
            params={{ projectId: row.original.id }}
            className={styles.slug}
            data-test="project-link"
          >
            {row.original.slug}
          </Link>
        </span>
      ),
      meta: {
        width: 168,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: t("column.slugFilterPlaceholder"),
          match: (project, needle) =>
            `${project.slug} ${project.name} ${project.gitProfileRepo ?? ""}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "name",
      header: t("column.name"),
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.name}>
          {row.original.name}
        </span>
      ),
    },
    {
      accessorKey: "tags",
      header: t("column.tags"),
      // The project's own vocabulary, chip by chip. The tint reads the same
      // custom property the dot does, so a row's identity is one colour in
      // every place it appears — and a row without a colour tints its chips
      // with the surface's muted ink rather than with nothing.
      cell: ({ row }) => {
        const tags = row.original.tags
        if (tags.length === 0) {
          return <span className={styles.absent}>—</span>
        }
        return (
          <span
            className={styles.tags}
            style={projectAccentStyle(row.original.color)}
            title={tags.join(", ")}
          >
            {tags.map((tag) => (
              <span
                key={tag}
                className={styles.tagChip}
                data-test="project-tag"
              >
                {tag}
              </span>
            ))}
          </span>
        )
      },
      meta: { width: 168, label: t("column.tags") },
    },
    {
      accessorKey: "envClass",
      header: t("column.envClass"),
      // The class the project's repository binds to — drawn as a chip when
      // present, said out loud when not. The chip uses the kit's `badgeShell`
      // (the only shape every badge in this product agrees on) and the local
      // `envClassChip` rule for the one colour the kit deliberately leaves
      // for the caller (`badge-shell.module.css` — colour, background and
      // font-weight). A bound class is a catalog id, not a project attribute,
      // so the chip wears the surface's muted ink rather than the project's
      // accent (the row's colour is its identity, not its runtime).
      cell: ({ row }) => {
        const envClass = row.original.envClass
        if (envClass === null) {
          // Not missing — a project without a class is a project whose
          // implement items are not claimable (add-worker-environments 2.2).
          // The text is the muted "no class" indicator, the same voice every
          // other absent fact on this row uses.
          return (
            <span
              className={styles.envClassNone}
              data-test="project-env-class"
              data-env-class="none"
            >
              {t("list.envClass.none")}
            </span>
          )
        }
        return (
          <span
            className={cn(badgeShell(), styles.envClassChip)}
            data-test="project-env-class"
            data-env-class={envClass}
            title={envClass}
          >
            {envClass}
          </span>
        )
      },
      // An absent class sorts last — like every other not-measured column on
      // this list. The DataTable's string sort would put a bound class before
      // the dash text, but it would also pin the dash last, so this matches
      // what an operator expects.
      meta: { width: 144, label: t("column.envClass") },
    },
    {
      accessorKey: "activeRuns",
      header: t("column.inFlight"),
      cell: ({ row }) => {
        const count = row.original.activeRuns
        return count > 0 ? (
          <span className={styles.active}>{count}</span>
        ) : (
          <span className={styles.absent}>—</span>
        )
      },
      meta: { width: 88, numeric: true, label: t("column.inFlight") },
    },
    {
      accessorKey: "totalRuns",
      header: t("column.runs"),
      cell: ({ row }) => {
        const count = row.original.totalRuns
        return count > 0 ? count : <span className={styles.absent}>—</span>
      },
      meta: { width: 80, numeric: true },
    },
    {
      accessorKey: "spendToday",
      header: t("column.costToday"),
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
      meta: { width: 104, numeric: true, label: t("column.costToday") },
    },
    {
      accessorKey: "gitProfileRepo",
      header: t("column.profiles"),
      cell: ({ row }) => {
        const repo = row.original.gitProfileRepo
        return repo ? (
          <span className={styles.repo} title={repo}>
            {repo}
          </span>
        ) : (
          // Not missing — running on the platform's own profiles, which is a
          // legitimate way for a project to be configured.
          <span className={styles.absent}>
            {t("identity.platformDefaults")}
          </span>
        )
      },
      meta: { label: t("column.profiles") },
    },
    {
      accessorKey: "createdAt",
      header: t("column.created"),
      cell: ({ row }) => (
        <span className={styles.created}>{row.original.createdAt}</span>
      ),
      meta: { width: 104 },
    },
  ]
}
