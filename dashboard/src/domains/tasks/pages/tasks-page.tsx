import { useMemo, useState } from "react"
import { Inbox, Plus, Zap } from "lucide-react"
import { toast } from "sonner"

import { AppShell } from "@/app/layout/app-shell"
import {
  useCreateTaskMutation,
  useDispatchTaskMutation,
  useTasksQuery,
} from "@/domains/tasks/api/queries"
import {
  countNew,
  filterTasks,
  uniqueTaskApps,
} from "@/domains/tasks/model/filter-tasks"
import type {
  CreateTaskInput,
  TaskPriorityFilter,
  TaskStatusFilter,
} from "@/domains/tasks/model/types"
import { CreateTaskDialog } from "@/domains/tasks/ui/create-task-dialog"
import { PriorityPill } from "@/domains/tasks/ui/priority-pill"
import { SourceBadge } from "@/domains/tasks/ui/source-badge"
import { TaskStatusPill } from "@/domains/tasks/ui/task-status-pill"
import { TasksFilterBar } from "@/domains/tasks/ui/tasks-filter-bar"
import { TASK_APPS } from "@/shared/api/mock/tasks.seed"
import { Button } from "@/shared/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/card"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/shared/ui/empty"
import { Skeleton } from "@/shared/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/shared/ui/table"

export function TasksPage() {
  const { data = [], isLoading, isError, error } = useTasksQuery()
  const createTask = useCreateTaskMutation()
  const dispatchTask = useDispatchTaskMutation()

  const [modalOpen, setModalOpen] = useState(false)
  const [query, setQuery] = useState("")
  const [app, setApp] = useState("all")
  const [status, setStatus] = useState<TaskStatusFilter>("all")
  const [priority, setPriority] = useState<TaskPriorityFilter>("all")

  const apps = useMemo(() => {
    const fromData = uniqueTaskApps(data)
    return fromData.length > 0 ? fromData : [...TASK_APPS]
  }, [data])

  const shown = useMemo(
    () => filterTasks(data, { query, app, status, priority }),
    [data, query, app, status, priority]
  )
  const newCount = countNew(data)

  const onCreate = (input: CreateTaskInput) => {
    createTask.mutate(input, {
      onSuccess: () => {
        setModalOpen(false)
        toast.success("Task created", { description: input.title })
      },
    })
  }

  const onDispatch = (id: string, title: string) => {
    dispatchTask.mutate(id, {
      onSuccess: () => {
        toast.message("Dispatched to orchestrator", { description: title })
      },
    })
  }

  return (
    <AppShell>
      <div className="flex flex-col gap-4">
        <header className="flex flex-wrap items-end justify-between gap-3">
          <div className="flex flex-col gap-1">
            <div className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
              intake / tasks
            </div>
            <h1 className="text-lg font-semibold tracking-tight">Tasks</h1>
            <p className="font-mono text-xs text-muted-foreground">
              {data.length} in backlog · {newCount} new
            </p>
          </div>
          <Button
            type="button"
            size="sm"
            onClick={() => setModalOpen(true)}
          >
            <Plus />
            New task
          </Button>
        </header>

        <TasksFilterBar
          query={query}
          app={app}
          status={status}
          priority={priority}
          apps={apps}
          total={shown.length}
          onQueryChange={setQuery}
          onAppChange={setApp}
          onStatusChange={setStatus}
          onPriorityChange={setPriority}
        />

        {isLoading ? (
          <Skeleton className="h-64 rounded-lg" />
        ) : null}

        {isError ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyTitle>Failed to load tasks</EmptyTitle>
              <EmptyDescription>
                {error instanceof Error ? error.message : "Unknown error"}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {!isLoading && !isError && shown.length === 0 ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <Inbox />
              </EmptyMedia>
              <EmptyTitle>Backlog empty</EmptyTitle>
              <EmptyDescription>
                No tasks match the current filters.
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {!isLoading && !isError && shown.length > 0 ? (
          <Card size="sm" className="gap-0 py-0">
            <CardHeader className="flex flex-row items-center justify-between border-b px-4 py-3">
              <CardTitle className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                Backlog · {shown.length}
              </CardTitle>
              <span className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
                Jira + manual
              </span>
            </CardHeader>
            <CardContent className="px-0 pb-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>source</TableHead>
                    <TableHead>task</TableHead>
                    <TableHead>app</TableHead>
                    <TableHead>priority</TableHead>
                    <TableHead>status</TableHead>
                    <TableHead className="text-right">action</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {shown.map((task) => (
                    <TableRow key={task.id}>
                      <TableCell>
                        <SourceBadge source={task.source} id={task.id} />
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-col gap-0.5">
                          <span className="max-w-[28rem] truncate text-sm text-foreground">
                            {task.title}
                          </span>
                          <span className="font-mono text-[10px] text-muted-foreground">
                            {task.age}
                          </span>
                        </div>
                      </TableCell>
                      <TableCell>
                        <span className="inline-flex items-center gap-1.5 font-mono text-xs text-muted-foreground">
                          <span className="size-1.5 rounded-full bg-primary" />
                          {task.app}
                        </span>
                      </TableCell>
                      <TableCell>
                        <PriorityPill priority={task.priority} />
                      </TableCell>
                      <TableCell>
                        <TaskStatusPill status={task.status} />
                      </TableCell>
                      <TableCell className="text-right">
                        {task.status === "planning" ? (
                          <TaskStatusPill status="planning" />
                        ) : (
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            disabled={dispatchTask.isPending}
                            onClick={() => onDispatch(task.id, task.title)}
                          >
                            <Zap />
                            Dispatch
                          </Button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        ) : null}
      </div>

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
