import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { toTask } from "@/domains/tasks/api/mappers"
import type { CreateTaskInput, Task } from "@/domains/tasks/model/types"
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

async function listTasks(): Promise<Task[]> {
  if (!env.useMock) {
    throw new Error("tasks API not implemented — set VITE_USE_MOCK=true")
  }
  return [...ensureQueue()]
}

async function createTask(input: CreateTaskInput): Promise<Task[]> {
  if (!env.useMock) {
    throw new Error("tasks API not implemented — set VITE_USE_MOCK=true")
  }
  const id = `m-${Math.floor(3042 + Math.random() * 900)}`
  const next: Task = {
    id,
    source: "manual",
    title: input.title,
    app: input.app,
    priority: input.priority,
    status: "queued",
    age: "just now",
  }
  mockQueue = [next, ...ensureQueue()]
  return [...mockQueue]
}

async function dispatchTask(id: string): Promise<Task[]> {
  if (!env.useMock) {
    throw new Error("tasks API not implemented — set VITE_USE_MOCK=true")
  }
  mockQueue = ensureQueue().map((task) =>
    task.id === id ? { ...task, status: "planning" } : task
  )
  return [...mockQueue]
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
