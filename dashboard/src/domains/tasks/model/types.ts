export type TaskSource = "jira" | "manual"
export type TaskPriority = "low" | "normal" | "high"
export type TaskStatus = "new" | "queued" | "planning"
export type TaskStatusFilter = TaskStatus | "all"
export type TaskPriorityFilter = TaskPriority | "all"

export interface Task {
  id: string
  /**
   * The project this ticket belongs to, by id. Dispatching it is a decision
   * made inside that project, so the row's action answers to this id rather
   * than to the shift — the same person may hand one row to the swarm and be
   * refused on the next.
   */
  projectId: string
  source: TaskSource
  title: string
  app: string
  priority: TaskPriority
  status: TaskStatus
  age: string
}

export interface CreateTaskInput {
  /** Which project the new ticket lands in — a choice, not a session mode. */
  projectId: string
  title: string
  app: string
  priority: TaskPriority
  brief?: string
}
