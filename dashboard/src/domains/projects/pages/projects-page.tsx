import { useMemo, useState } from "react"
import { Link } from "@tanstack/react-router"
import { Plus, RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useProjectsQuery } from "@/domains/projects/api/queries"
import {
  createProjectColumns,
  getProjectId,
} from "@/domains/projects/ui/projects-columns"
import tableStyles from "@/domains/projects/ui/projects-table.module.css"
import { formatCost } from "@/domains/runs/model/format"
import { requestFailureMessage } from "@/shared/api/problem"
import { useCan } from "@/shared/session"
import {
  Button,
  DataTable,
  DataTableToolbar,
  ScreenState,
  Skeleton,
  Tooltip,
  applyDataFilters,
  buttonClass,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import styles from "./projects-page.module.css"

const SKELETON_WIDTHS = ["44%", "68%", "52%", "80%"]

export interface ProjectsPageProps {
  /** A slug to narrow the list to on arrival — see `focus` below. */
  focus?: string
}

/**
 * The platform registry: every project, what it is running, what it costs.
 *
 * The lower tier of the rail, visited on a different clock from the duty
 * screens and usually to create something or to answer what a project is
 * spending. Density still matters and urgency does not, which is why this is a
 * plain list with one act on it rather than a board.
 *
 * The three derived columns are joined from the run list and the cost report
 * rather than stored: there is no third place for the numbers to be wrong in,
 * and a project that has neither yet degrades to dashes instead of pretending
 * to have been measured.
 *
 * Creating one is a screen of its own at `/projects/new` rather than a modal
 * over this list — it is an edit, and edits get pages here. What comes back is
 * `focus`: the slug the form just wrote, seeded into the toolbar's own filter
 * so the operator lands on the row they made and can see, in the toolbar, why
 * the list is one row long.
 */
export function ProjectsPage({ focus }: ProjectsPageProps) {
  const { data = [], isLoading, isError, error, refetch } = useProjectsQuery()

  // A platform act, asked without a project: platform roles alone answer for
  // Projects, and being project-admin of three of them must never open this.
  const mayCreate = useCan("projects.create")

  // Seeded once, then owned by the toolbar: the filter is the operator's from
  // the moment they land, and clearing it is the ordinary control it always is.
  const [filters, setFilters] = useState<DataTableFilterValues>(() => {
    const seeded: DataTableFilterValues = {}
    if (focus) {
      seeded.slug = focus
    }
    return seeded
  })
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  const [sorting, setSorting] = useState<DataTableSorting>([])
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const columns = useMemo(() => createProjectColumns(), [])
  const rows = useMemo(
    () => applyDataFilters(data, filters, columns),
    [data, filters, columns]
  )

  const inFlight = useMemo(
    () => data.reduce((sum, project) => sum + project.activeRuns, 0),
    [data]
  )
  const spend = useMemo(
    () => data.reduce((sum, project) => sum + (project.spendToday ?? 0), 0),
    [data]
  )

  const ready = !isLoading && !isError

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[{ label: "platform" }, { label: "projects" }]}
          title="Projects"
          summary={
            ready ? (
              <>
                <span className={styles.strong}>{data.length}</span> projects
                {" · "}
                <span className={styles.strong}>{inFlight}</span> runs in flight
                {" · "}
                <span className={styles.strong}>{formatCost(spend)}</span> today
              </>
            ) : undefined
          }
          actions={
            ready ? (
              // Two elements for one act, and the split is the access rule.
              // Allowed, it is navigation and it is spelled as navigation — a
              // real anchor wearing the button's recipe, so it can be opened
              // in a tab, copied, and read as a destination by anything that
              // traverses links. Denied, it is a control that refuses and says
              // what it needs: a disabled anchor is not a thing, and an anchor
              // has no `denied`. Gated rather than hidden, because somebody who
              // may read the registry and not add to it should learn that
              // creating exists rather than meeting a screen with a hole in it.
              //
              // Two words, so the glyph carries the act and the tooltip
              // carries the words. `aria-label` keeps the name either way —
              // a tooltip describes and never becomes the name.
              mayCreate.allowed ? (
                <Tooltip content="New project">
                  <Link
                    to="/projects/new"
                    data-test="project-new"
                    aria-label="New project"
                    className={buttonClass({ size: "icon-sm" })}
                  >
                    <Plus aria-hidden="true" />
                  </Link>
                </Tooltip>
              ) : (
                <Tooltip content={mayCreate.denial ?? "New project"}>
                  <Button
                    size="icon-sm"
                    data-test="project-new"
                    denied={mayCreate.denial}
                    aria-label="New project"
                  >
                    <Plus aria-hidden="true" />
                  </Button>
                </Tooltip>
              )
            ) : null
          }
          /* The bar that narrows the list, in the band that never scrolls —
             the contract `PageHeader` states and `runs-page` is named the
             reference for. It used to ride inside `.screen`, held still by a
             hand-traced height chain of this screen's own; the slot does that
             for free and puts the search field on the same start edge as the
             first column below it. */
          filters={
            ready ? (
              <DataTableToolbar
                columns={columns}
                filters={filters}
                onFiltersChange={setFilters}
                columnVisibility={columnVisibility}
                onColumnVisibilityChange={setColumnVisibility}
                trailing={
                  <span
                    className={tableStyles.count}
                    data-test="projects-count"
                  >
                    {rows.length} shown
                  </span>
                }
              />
            ) : null
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <Skeleton
            lines={SKELETON_WIDTHS}
            inset="gutter"
            fill
            data-test="projects-loading"
          />
        ) : null}

        {isError ? (
          <ScreenState
            kind="error"
            title="The registry did not load"
            description={requestFailureMessage(error, "Unknown error")}
            inset="gutter"
            action={
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="projects-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            }
          />
        ) : null}

        {ready ? (
          <div className={styles.tableArea}>
            <DataTable
              columns={columns}
              data={rows}
              getRowId={getProjectId}
              density="compact"
              columnVisibility={columnVisibility}
              onColumnVisibilityChange={setColumnVisibility}
              sorting={sorting}
              onSortingChange={setSorting}
              columnSizing={columnSizing}
              onColumnSizingChange={setColumnSizing}
              emptyLabel={
                hasActiveFilters(filters)
                  ? "no projects match the current filters"
                  : "no projects yet"
              }
            />
          </div>
        ) : null}
      </div>
    </AppShell>
  )
}
