import { useCallback, useMemo, useState } from "react"
import { Plus, RotateCw } from "lucide-react"
import { toast } from "sonner"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import {
  useCreateTaskMutation,
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
  CreateTaskInput,
  Task,
  TaskPriorityFilter,
  TaskStatusFilter,
} from "@/domains/tasks/model/types"
import { CreateTaskDialog } from "@/domains/tasks/ui/create-task-dialog"
import { createTaskColumns, getTaskId } from "@/domains/tasks/ui/tasks-columns"
import tableStyles from "@/domains/tasks/ui/tasks-table.module.css"
import { TASK_APPS } from "@/shared/api/mock/tasks.seed"
import { can, useCan, useSession } from "@/shared/session"
import {
  Button,
  DataTable,
  DataTableToolbar,
  Tooltip,
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

export function TasksPage({ focus }: TasksPageProps) {
  const { data = [], isLoading, isError, error, refetch } = useTasksQuery()
  const createTask = useCreateTaskMutation()
  const dispatchTask = useDispatchTaskMutation()

  const session = useSession()

  // Both acts on this screen put work into the swarm's intake, so both answer
  // to `inbox.take` — but *where* differs. The opener asks the rail's
  // question, with no project: may this person take a ticket at all, on any
  // project they hold? Hiding intake from someone who runs it daily on one
  // project out of three would be a lie of omission. The row's dispatch and
  // the dialog's submit ask the sharper question against a project, and they
  // ask it themselves.
  const mayTake = useCan("inbox.take")

  const [modalOpen, setModalOpen] = useState(false)
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
          toast.message("Dispatched to orchestrator", {
            description: task.title,
          })
        },
      })
    },
    [dispatchMutate, session]
  )

  const columns = useMemo(
    () =>
      createTaskColumns({
        apps,
        projects,
        dispatching: dispatchTask.isPending,
        onDispatch,
        session,
      }),
    [apps, projects, dispatchTask.isPending, onDispatch, session]
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

  const onCreate = (input: CreateTaskInput) => {
    // The dialog only offers projects this shift may take work in, and its
    // submit is gated on the one chosen — this asks again on the way out,
    // because the gate is the permission and not the control carrying it.
    if (!can(session, "inbox.take", input.projectId)) {
      return
    }
    createTask.mutate(input, {
      onSuccess: () => {
        setModalOpen(false)
        toast.success("Task created", { description: input.title })
      },
    })
  }

  const ready = !isLoading && !isError

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[{ label: "tasks" }]}
          title="Tasks"
          summary={
            ready ? (
              <>
                <span className={styles.strong}>{data.length}</span> in backlog
                {" · "}
                <span className={styles.strong}>{newCount}</span> new
              </>
            ) : undefined
          }
          actions={
            // The opener is gated rather than hidden: a viewer who lands here
            // should learn that intake exists and what it takes to use it.
            //
            // Two words, so the glyph carries the act and the tooltip carries
            // the words — the denial takes that place when there is one, which
            // is why `denied` and not `disabled`: an `aria-disabled` control
            // still fires the pointer events the tooltip rides on.
            <Tooltip content={mayTake.denial ?? "New task"}>
              <Button
                type="button"
                size="icon-sm"
                data-test="task-new"
                denied={mayTake.denial}
                aria-label="New task"
                onClick={() => setModalOpen(true)}
              >
                <Plus aria-hidden="true" />
              </Button>
            </Tooltip>
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="tasks-loading">
            {SKELETON_WIDTHS.map((width, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ width }}
              />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>The backlog did not load</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="tasks-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {ready ? (
          <>
            <div className={styles.toolbar}>
              <DataTableToolbar
                columns={columns}
                filters={filters}
                onFiltersChange={setFilters}
                columnVisibility={columnVisibility}
                onColumnVisibilityChange={setColumnVisibility}
                trailing={
                  <span className={tableStyles.count} data-test="tasks-count">
                    {shown.length} shown
                  </span>
                }
              />
            </div>
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
                    ? "no tasks match the current filters"
                    : "the backlog is empty"
                }
              />
            </div>
          </>
        ) : null}
      </div>

      {/* The dialog reads the shift itself: which projects it may open, and
          whether the one chosen is still one of them, are the same question
          asked of a value the dialog owns. */}
      <CreateTaskDialog
        open={modalOpen}
        apps={apps}
        busy={createTask.isPending}
        onOpenChange={setModalOpen}
        onCreate={onCreate}
      />
    </AppShell>
  )
}
