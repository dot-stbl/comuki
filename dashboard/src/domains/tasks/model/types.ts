export type TaskSource = "jira" | "manual"
export type TaskPriority = "low" | "normal" | "high"
export type TaskStatus = "new" | "queued" | "planning"
export type TaskStatusFilter = TaskStatus | "all"
export type TaskPriorityFilter = TaskPriority | "all"

export interface Task {
  id: string
  source: TaskSource
  title: string
  app: string
  priority: TaskPriority
  status: TaskStatus
  age: string
}

export interface CreateTaskInput {
  title: string
  app: string
  priority: TaskPriority
  brief?: string
}
