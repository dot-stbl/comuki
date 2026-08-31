/** A project as the platform list shows it: the record plus what it is doing. */
export interface ProjectRow {
  id: string
  /** The handle every other list in the product shows. A value, not a name. */
  slug: string
  name: string
  gitProfileRepo: string | null
  createdAt: string
  /** Runs the swarm is standing on for this project right now. */
  activeRuns: number
  /** Every run this shift has seen for it, finished ones included. */
  totalRuns: number
  /**
   * Today's spend, or `null` when nothing has been attributed to the project.
   *
   * `null` rather than `0` because they are different facts: zero is a project
   * that ran and cost nothing, `null` is a project the cost report has never
   * heard of. A row that renders both as `$0.00` is telling the operator that a
   * new project is already accounted for.
   */
  spendToday: number | null
}

export interface CreateProjectInput {
  name: string
  slug: string
  /** `null` when the project runs on the platform's default profiles. */
  gitProfileRepo: string | null
}
