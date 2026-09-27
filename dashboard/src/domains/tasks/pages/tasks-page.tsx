import { useCallback, useMemo, useState } from "react"
import { Link } from "@tanstack/react-router"
import { Plus, RotateCw } from "lucide-react"
import { toast } from "sonner"
import { Trans, useTranslation } from "react-i18next"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useDispatchTaskMutation,
  useTasksQuery,
} from "@/domains/tasks/api/queries"
import {
  countNew,
  filterTasks,
  uniqueTaskApps,
  uniqueTaskProjects,
} from "@/domains/tasks/model/filter-tasks"
import type {
  Task,
  TaskPriorityFilter,
  TaskStatusFilter,
} from "@/domains/tasks/model/types"
import { createTaskColumns, getTaskId } from "@/domains/tasks/ui/tasks-columns"
import { TaskArtifactViewerHost } from "@/domains/tasks/ui/task-artifact-cell"
import tableStyles from "@/domains/tasks/ui/tasks-table.module.css"
import { TASK_APPS } from "@/shared/api/mock/tasks.seed"
import { can, useCan, useSession } from "@/shared/session"
import {
  Button,
  DataTable,
  DataTableToolbar,
  ScreenState,
  Skeleton,
  Tooltip,
  buttonClass,
  hasActiveFilters,
  type DataTableColumnSizing,
  type DataTableColumnVisibility,
  type DataTableFilterValues,
  type DataTableSorting,
} from "@/shared/ui"

import styles from "./tasks-page.module.css"

const SKELETON_WIDTHS = ["58%", "42%", "71%", "50%"]

export interface TasksPageProps {
  /**
   * A query to narrow the backlog to on arrival — what a global search, or a
   * pasted link, hands over. It seeds the toolbar's own text filter, so the
   * narrowing is visible in the field it lives in and clears in one click.
   */
  focus?: string
}

/**
 * The page. Wraps `TasksBody` in `TaskArtifactViewerHost` so the visual-
 * artifact modal lives outside the shell — one modal, owned by the page,
 * opened by clicking a row thumbnail and closed by escape or the pane's
 * close control.
 */
export function TasksPage({ focus }: TasksPageProps) {
  return (
    <TaskArtifactViewerHost>
      {({ openArtifact }) => (
        <TasksBody focus={focus} onArtifactOpen={openArtifact} />
      )}
    </TaskArtifactViewerHost>
  )
}

interface TasksBodyProps extends TasksPageProps {
  onArtifactOpen: (ticketId: string, projectId: string) => void
}

function TasksBody({ focus, onArtifactOpen }: TasksBodyProps) {
  const { t } = useTranslation("tasks")
  const { t: tShell } = useTranslation("shell")
  const { data = [], isLoading, isError, error, refetch } = useTasksQuery()
  const dispatchTask = useDispatchTaskMutation()

  const session = useSession()

  // Both acts on this screen put work into the swarm's intake, so both answer
  // to `inbox.take` — but *where* differs. The opener asks the rail's
  // question, with no project: may this person take a ticket at all, on any
  // project they hold? Hiding intake from someone who runs it daily on one
  // project out of three would be a lie of omission. The row's dispatch asks
  // the sharper question against a project, and the create page asks it again
  // on submit.
  const mayTake = useCan("inbox.take")

  // Seeded once, then owned by the toolbar: the filter is the operator's from
  // the moment they land, and clearing it is the ordinary control it always is.
  const [filters, setFilters] = useState<DataTableFilterValues>(() => {
    const seeded: DataTableFilterValues = {}
    if (focus) {
      seeded.title = focus
    }
    return seeded
  })
  const [columnVisibility, setColumnVisibility] =
    useState<DataTableColumnVisibility>({})
  // The backlog has no opinion about its own order — it opens as the intake
  // gave it — so this starts empty and only the user writes to it. When the
  // orchestrator learns to sort server-side, this is what the query reads.
  const [sorting, setSorting] = useState<DataTableSorting>([])
  // Column widths, screen-owned like every other slice the user writes.
  const [columnSizing, setColumnSizing] = useState<DataTableColumnSizing>({})

  const apps = useMemo(() => {
    const fromData = uniqueTaskApps(data)
    return fromData.length > 0 ? fromData : [...TASK_APPS]
  }, [data])

  const projects = useMemo(
    () => uniqueTaskProjects(data, session.projects),
    [data, session.projects]
  )

  // `mutate` is stable across renders, so the callback — and with it the
  // column list — only rebuilds when the backlog's apps, the shift or the
  // pending flag do. The whole ticket rather than its id and title, because
  // the guard here has to ask the same question the button asked: may this
  // shift take *this project's* work.
  const dispatchMutate = dispatchTask.mutate
  const onDispatch = useCallback(
    (task: Task) => {
      if (!can(session, "inbox.take", task.projectId)) {
        return
      }
      dispatchMutate(task.id, {
        onSuccess: () => {
          toast.success(t("dispatch.toast"), {
            description: task.title,
          })
        },
      })
    },
    [dispatchMutate, session, t]
  )

  const columns = useMemo(
    () =>
      createTaskColumns({
        apps,
        projects,
        dispatching: dispatchTask.isPending,
        onDispatch,
        // Issue #51 slice 3 — `onArtifactOpen` is the cell ↔ modal bridge.
        // The column receives a ticket id + project id; the host owns
        // mounting the pane.
        onArtifactOpen,
        session,
        t,
      }),
    [
      apps,
      projects,
      dispatchTask.isPending,
      onDispatch,
      onArtifactOpen,
      session,
      t,
    ]
  )

  // The toolbar hands back values keyed by column id; the domain still owns
  // what filtering *means*, so they are mapped onto `TaskFilters` rather than
  // reimplemented here. Values can only be ids the columns above declared, and
  // `title` runs the same `matchesTaskQuery` the column's own `match` declares.
  const shown = useMemo(
    () =>
      filterTasks(data, {
        query: filters.title ?? "",
        project: filters.project || "all",
        app: filters.app || "all",
        status: (filters.status || "all") as TaskStatusFilter,
        priority: (filters.priority || "all") as TaskPriorityFilter,
      }),
    [data, filters]
  )
  const newCount = countNew(data)

  const ready = !isLoading && !isError

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[{ label: tShell("crumb.tasks") }]}
          title={t("registry.title")}
          summary={
            ready ? (
              /* The figures are values in their own voice and the words are
                 prose in theirs, so the emphasis rides slot elements and the
                 sentence — word order included — belongs to the locale. The
                 counts stringify: a Trans slot holding a falsy child (a bare
                 `0`) renders empty, and zero is a reading, not a blank. */
              <Trans
                ns="tasks"
                i18nKey="registry.summary"
                components={{
                  total: (
                    <span className={styles.strong}>{String(data.length)}</span>
                  ),
                  fresh: (
                    <span className={styles.strong}>{String(newCount)}</span>
                  ),
                }}
              />
            ) : undefined
          }
          actions={
            // Creating is a page at `/tasks/new`, not a modal over this list —
            // same split projects and identity use. Allowed, it is navigation
            // and spelled as an anchor; denied, it is a control that refuses
            // and says what it needs (a disabled anchor is not a thing).
            mayTake.allowed ? (
              <Tooltip content={t("registry.newTask")}>
                <Link
                  to="/tasks/new"
                  data-test="task-new"
                  aria-label={t("registry.newTask")}
                  className={buttonClass({ size: "icon-sm" })}
                >
                  <Plus aria-hidden="true" />
                </Link>
              </Tooltip>
            ) : (
              <Tooltip content={mayTake.denial ?? t("registry.newTask")}>
                <Button
                  size="icon-sm"
                  data-test="task-new"
                  denied={mayTake.denial}
                  aria-label={t("registry.newTask")}
                >
                  <Plus aria-hidden="true" />
                </Button>
              </Tooltip>
            )
          }
          /* The filter bar rides in the header rather than above the table,
             which is the contract on `PageHeader` and what `runs-page` is the
             worked example of: the header is the band that never scrolls, and
             these are the controls that decide which rows the screen shows. A
             filter that can scroll away from the list it narrows is a filter
             somebody has to go looking for. The screen still owns `filters`
             and `columnVisibility`, and the same values still reach the same
             table — only the bar moved, and the local height chain that used
             to pin it went with it. */
          filters={
            ready ? (
              <DataTableToolbar
                columns={columns}
                filters={filters}
                onFiltersChange={setFilters}
                columnVisibility={columnVisibility}
                onColumnVisibilityChange={setColumnVisibility}
                trailing={
                  <span className={tableStyles.count} data-test="tasks-count">
                    {t("registry.count", { count: shown.length })}
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
            label={t("registry.loading")}
            data-test="tasks-loading"
          />
        ) : null}

        {isError ? (
          <ScreenState
            kind="error"
            title={t("registry.errorTitle")}
            description={
              error instanceof Error
                ? error.message
                : t("errors.unknown", { ns: "common" })
            }
            inset="gutter"
            data-test="tasks-error"
            action={
              <Tooltip content={t("actions.retry", { ns: "common" })}>
                <Button
                  size="icon-sm"
                  data-test="tasks-retry"
                  aria-label={t("actions.retry", { ns: "common" })}
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

        {/* A dispatch that the orchestrator refused. Said on the screen rather
            than in a toast, and it stays said: the success of this act is
            already a toast, and a toast for the failure would be the one of
            the two readings that disappears on its own while the row it is
            about is still sitting in the backlog looking untouched. Inline,
            `role="alert"`, and it names what did not move. */}
        {dispatchTask.error ? (
          <p
            className={styles.failure}
            role="alert"
            data-test="tasks-dispatch-failed"
          >
            {dispatchTask.error instanceof Error
              ? dispatchTask.error.message
              : t("dispatch.fallback")}{" "}
            {t("dispatch.tail")}
          </p>
        ) : null}

        {ready ? (
          <div className={styles.tableArea}>
            <DataTable
              columns={columns}
              data={shown}
              getRowId={getTaskId}
              density="compact"
              columnVisibility={columnVisibility}
              onColumnVisibilityChange={setColumnVisibility}
              sorting={sorting}
              onSortingChange={setSorting}
              columnSizing={columnSizing}
              onColumnSizingChange={setColumnSizing}
              emptyLabel={
                hasActiveFilters(filters)
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
