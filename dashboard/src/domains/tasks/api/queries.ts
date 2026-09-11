import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { intakeTicketViewsToTasks, toTask } from "@/domains/tasks/api/mappers"
import type { CreateTaskInput, Task } from "@/domains/tasks/model/types"
import { postApiV1InboxClaim } from "@/shared/api/_generated/clients/postApiV1InboxClaim"
import { postApiV1Tickets } from "@/shared/api/_generated/clients/postApiV1Tickets"
import { getApiV1Inbox } from "@/shared/api/_generated/clients/getApiV1Inbox"
import { TASKS_SEED } from "@/shared/api/mock/tasks.seed"
import { env } from "@/shared/config/env"

export const tasksQueryKey = ["tasks"] as const

let mockQueue: Task[] | null = null

function ensureQueue(): Task[] {
  if (!mockQueue) {
    mockQueue = TASKS_SEED.map(toTask)
  }
  return mockQueue
}

/**
 * Read the pending backlog.
 *
 * `VITE_USE_MOCK=true` reads the mutable seed store (the only way a freshly
 * created task sticks across refetches); `VITE_USE_MOCK=false` calls
 * `GET /api/v1/inbox` on the host. The endpoint takes an optional `projectId`
 * filter, which the page does not yet promote into a UI control — we send
 * none today and let the host return every pending ticket the session can see.
 *
 * The wire returns the host's flat `IntakeTicketView[]`; the mapper in
 * `mappers.ts` translates to the dashboard's richer `Task` shape (the wire
 * provider key passes through as `Task.source`, `app` is defaulted from the
 * source, `age` is formatted from `createdAt`).
 */
async function listTasks(): Promise<Task[]> {
  if (env.useMock) {
    return [...ensureQueue()]
  }
  const views = await getApiV1Inbox()
  return intakeTicketViewsToTasks(views)
}

/**
 * File a manual ticket into a project's intake.
 *
 * Mock-first: the seed store appends and returns the full queue. Real mode
 * posts to `POST /api/v1/tickets` (the host's native intake) and re-reads
 * the inbox so the screen picks up the freshly-queued ticket alongside
 * everything else. The form's `source` field is dashboard-only — the wire
 * stamps `native` server-side and the dashboard renders that as `native`
 * in the column.
 *
 * `brief` is the only field not in the wire contract; the host takes an
 * optional `body`, which is the same text the operator typed into the
 * brief field on the form.
 */
async function createTask(input: CreateTaskInput): Promise<Task[]> {
  if (env.useMock) {
    const id = `m-${Math.floor(3042 + Math.random() * 900)}`
    const next: Task = {
      id,
      projectId: input.projectId,
      // The provenance the form asked about, rather than a hard-coded "native":
      // the stamp is the backlog's identity column, so it is the form's to give.
      source: input.source,
      title: input.title,
      app: input.app,
      priority: input.priority,
      status: "queued",
      age: "just now",
    }
    mockQueue = [next, ...ensureQueue()]
    return [...mockQueue]
  }
  await postApiV1Tickets({
    projectId: input.projectId,
    title: input.title,
    // Wire's `body` is `string | undefined` (kubb's loose typing of the
    // C# nullable); an empty form brief maps to `undefined` so the field
    // is omitted from the body rather than sent as the literal `null`.
    body: input.brief ?? undefined,
  })
  return listTasks()
}

async function dispatchTask(id: string): Promise<Task[]> {
  if (env.useMock) {
    mockQueue = ensureQueue().map((task) =>
      task.id === id ? { ...task, status: "planning" } : task
    )
    return [...mockQueue]
  }
  // `POST /api/v1/inbox/claim` launches the ticket's run (the host returns
  // the ticket in `Claimed` status — the run id rides on the same view).
  // The dashboard's `Task.status` then moves from `"new"` to `"queued"` on
  // the next refetch via the mapper's `Claimed → queued` mapping.
  await postApiV1InboxClaim({ ticketId: id })
  return listTasks()
}

export function useTasksQuery() {
  return useQuery({
    queryKey: tasksQueryKey,
    queryFn: listTasks,
  })
}

export function useCreateTaskMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: createTask,
    onSuccess: (next) => {
      queryClient.setQueryData(tasksQueryKey, next)
    },
  })
}

export function useDispatchTaskMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => dispatchTask(id),
    onSuccess: (next) => {
      queryClient.setQueryData(tasksQueryKey, next)
    },
  })
}
