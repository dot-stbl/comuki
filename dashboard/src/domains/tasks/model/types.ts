/**
 * Where a ticket came from, in the platform's own provider vocabulary — the
 * same tracker kinds a source connection can speak, plus `manual` for the
 * product's own intake.
 *
 * It stayed `"jira" | "manual"` while the only tracker the seed carried was
 * jira. The intake form now asks the question out loud, and a question the
 * model cannot hold an answer to would have had to hard-code one — so the
 * union widened to the four connectable providers rather than the form
 * offering a choice that pretends to exist.
 */
export type TaskSource =
  | "github"
  | "gitlab"
  | "yandex-tracker"
  | "jira"
  | "manual"
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
  /**
   * The provenance stamp the ticket carries into the backlog — see
   * `task-sources.ts` for what each value says and where it is read.
   */
  source: TaskSource
  title: string
  app: string
  priority: TaskPriority
  brief?: string
}
