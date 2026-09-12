import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { inboxQueryKey } from "@/domains/inbox/api/queries"
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
 * Mock-first: the seed store appends and returns the full queue, which the
 * mutation's `onSuccess` writes straight into the cache (the only way a
 * freshly created task sticks across refetches in mock mode). Real mode
 * posts to `POST /api/v1/tickets` (the host's native intake) and returns
 * nothing — `onSuccess` invalidates the tasks key and the inbox domain's
 * list key, and the refetch picks up the freshly-queued ticket alongside
 * everything else. The form's `source` field is dashboard-only — the wire
 * stamps `native` server-side and the dashboard renders that as `native`
 * in the column.
 *
 * `brief` is the only field not in the wire contract; the host takes an
 * optional `body`, which is the same text the operator typed into the
 * brief field on the form.
 */
async function createTask(input: CreateTaskInput): Promise<Task[] | undefined> {
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
  return undefined
}

async function dispatchTask(id: string): Promise<Task[] | undefined> {
  if (env.useMock) {
    mockQueue = ensureQueue().map((task) =>
      task.id === id ? { ...task, status: "planning" } : task
    )
    return [...mockQueue]
  }
  // `POST /api/v1/inbox/claim` launches the ticket's run (the host returns
  // the ticket in `Claimed` status — the run id rides on the same view).
  // The mutation returns nothing; the invalidation in `onSuccess` refetches
  // the inbox, and the claimed ticket leaves the pending list (the host's
  // `ListPendingAsync` filters it out) — the honest reading of a claim.
  await postApiV1InboxClaim({ ticketId: id })
  return undefined
}

export function useTasksQuery() {
  return useQuery({
    queryKey: tasksQueryKey,
    queryFn: listTasks,
  })
}

/**
 * How both write hooks settle the cache: mock mode hands the screen the
 * seed queue the mutation just returned (setQueryData); real mode
 * invalidates — the tasks key this screen reads, and the inbox domain's
 * list key so any reader of the shared inbox layer agrees with the host
 * after the write. The same discipline the sources domain applies.
 */
function settleTasksCache(queryClient: ReturnType<typeof useQueryClient>) {
  return async (next: Task[] | undefined) => {
    if (next) {
      queryClient.setQueryData(tasksQueryKey, next)
      return
    }
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: tasksQueryKey }),
      queryClient.invalidateQueries({ queryKey: inboxQueryKey }),
    ])
  }
}

export function useCreateTaskMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: createTask,
    onSuccess: settleTasksCache(queryClient),
  })
}

export function useDispatchTaskMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => dispatchTask(id),
    onSuccess: settleTasksCache(queryClient),
  })
}
