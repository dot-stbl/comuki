import type { ProviderKey } from "@/domains/sources/model/types"

/**
 * The backlog, as the screen sees it.
 *
 * A ticket's provenance is a `ProviderKey` from `domains/sources` — the same
 * registry a source connection reads, and not a second union that happens to
 * hold the same words. There was one: `TaskSource` listed the four trackers
 * plus `manual`, where the sources half said `native` for the same idea, and
 * `task-sources.ts` said in its own header that it mirrored the other
 * catalogue *on purpose*. Two spellings of one vendor is two vocabularies for
 * the operator and two places for a sixth provider to be forgotten.
 *
 * `native` is the surviving word for the product's own intake, because it is
 * the word on the wire: `IntakeTicketView.source` carries it, the webhook
 * route segment is built from it, and `manual` appears nowhere the host can
 * see. `manual` also named the wrong thing — it described a gesture (somebody
 * typed this) rather than a provider, and this form deliberately lets a person
 * type a ticket and stamp it `github`.
 */
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
  source: ProviderKey
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
   * The provenance stamp the ticket carries into the backlog — a key from the
   * provider registry in `domains/sources/model/providers.ts`, which is where
   * its word, its mark and the line the intake card says all come from.
   */
  source: ProviderKey
  title: string
  app: string
  priority: TaskPriority
  brief?: string
}
