import { useMemo, useState } from "react"
import { Link } from "@tanstack/react-router"
import { Plus, RotateCw } from "lucide-react"
import { Trans, useTranslation } from "react-i18next"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useProjectsQuery } from "@/domains/projects/api/queries"
import {
  createProjectColumns,
  getProjectId,
} from "@/domains/projects/ui/projects-columns"
import tableStyles from "@/domains/projects/ui/projects-table.module.css"
import { TagFilter } from "@/domains/projects/ui/tag-filter"
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
  const { t } = useTranslation("projects")
  const { t: tShell } = useTranslation("shell")
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

  const columns = useMemo(() => createProjectColumns(t), [t])

  // The registry's whole tag vocabulary, read off the loaded rows. There is
  // no dictionary endpoint and none is wanted (design D6): the list is fully
  // in memory, the vocabulary is what these rows carry, and the flip to a
  // server-side filter is a recorded threshold rather than a default.
  const distinctTags = useMemo(() => {
    const tags = new Set<string>()
    for (const project of data) {
      for (const tag of project.tags) {
        tags.add(tag)
      }
    }
    return [...tags].sort()
  }, [data])

  const [selectedTags, setSelectedTags] = useState<string[]>([])

  const toggleTag = (tag: string) => {
    setSelectedTags((current) =>
      current.includes(tag)
        ? current.filter((entry) => entry !== tag)
        : [...current, tag]
    )
  }

  const rows = useMemo(() => {
    const textFiltered = applyDataFilters(data, filters, columns)
    // AND semantics: a row shows when it carries *every* selected tag, so
    // two tags intersect rather than union — the operator narrowing a list
    // is subtracting, not adding. Composes with the text filter: both must
    // match, and clearing either returns to what the other alone narrows.
    if (selectedTags.length === 0) {
      return textFiltered
    }
    return textFiltered.filter((project) =>
      selectedTags.every((tag) => project.tags.includes(tag))
    )
  }, [data, filters, columns, selectedTags])

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
          breadcrumbs={[
            { label: tShell("crumb.platform") },
            { label: tShell("crumb.projects") },
          ]}
          title={t("registry.title")}
          summary={
            ready ? (
              /* The figures are values in their own voice and the words are
                 prose in theirs, so the emphasis rides slot elements and the
                 sentence — word order included — belongs to the locale. The
                 counts stringify: a Trans slot holding a falsy child (a bare
                 `0`) renders empty, and zero is a reading, not a blank. */
              <Trans
                ns="projects"
                i18nKey="registry.summary"
                components={{
                  count: (
                    <span className={styles.strong}>{String(data.length)}</span>
                  ),
                  runs: (
                    <span className={styles.strong}>{String(inFlight)}</span>
                  ),
                  spend: (
                    <span className={styles.strong}>{formatCost(spend)}</span>
                  ),
                }}
              />
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
                <Tooltip content={t("registry.newProject")}>
                  <Link
                    to="/projects/new"
                    data-test="project-new"
                    aria-label={t("registry.newProject")}
                    className={buttonClass({ size: "icon-sm" })}
                  >
                    <Plus aria-hidden="true" />
                  </Link>
                </Tooltip>
              ) : (
                <Tooltip content={mayCreate.denial ?? t("registry.newProject")}>
                  <Button
                    size="icon-sm"
                    data-test="project-new"
                    denied={mayCreate.denial}
                    aria-label={t("registry.newProject")}
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
                leading={
                  <TagFilter
                    availableTags={distinctTags}
                    selectedTags={selectedTags}
                    onToggleTag={toggleTag}
                  />
                }
                trailing={
                  <span
                    className={tableStyles.count}
                    data-test="projects-count"
                  >
                    {t("registry.count", { count: rows.length })}
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
            title={t("registry.errorTitle")}
            description={requestFailureMessage(error, t("errors.unknown"))}
            inset="gutter"
            action={
              <Tooltip content={t("actions.retry")}>
                <Button
                  size="icon-sm"
                  data-test="projects-retry"
                  aria-label={t("actions.retry")}
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
                hasActiveFilters(filters) || selectedTags.length > 0
                  ? t("registry.empty.filtered")
                  : t("registry.empty.none")
              }
            />
          </div>
        ) : null}
      </div>
    </AppShell>
  )
}
