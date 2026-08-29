import type {
  Task,
  TaskPriorityFilter,
  TaskStatusFilter,
} from "@/domains/tasks/model/types"

export interface TaskFilters {
  query: string
  app: string
  status: TaskStatusFilter
  priority: TaskPriorityFilter
}

export function uniqueTaskApps(tasks: Task[]): string[] {
  return [...new Set(tasks.map((task) => task.app))].sort()
}

export function filterTasks(tasks: Task[], filters: TaskFilters): Task[] {
  const needle = filters.query.trim().toLowerCase()
  return tasks.filter((task) => {
    if (filters.app !== "all" && task.app !== filters.app) {
      return false
    }
    if (filters.status !== "all" && task.status !== filters.status) {
      return false
    }
    if (filters.priority !== "all" && task.priority !== filters.priority) {
      return false
    }
    if (!needle) {
      return true
    }
    return (
      task.title.toLowerCase().includes(needle) ||
      task.id.toLowerCase().includes(needle) ||
      task.app.toLowerCase().includes(needle)
    )
  })
}

export function countNew(tasks: Task[]): number {
  return tasks.filter((task) => task.status === "new").length
}
