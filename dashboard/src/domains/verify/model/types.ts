/**
 * The verification gate as the screen sees it.
 *
 * Two records, and the split is the product's rule made structural. A
 * **project** carries the one thing this screen can change — whether the gate
 * runs — plus the git coordinates the commands were read from. A **command** is
 * a line in the client's repository, and it has no setter anywhere in this
 * domain: changing one means a commit over there, which is what GitOps means
 * when it is not a slogan.
 *
 * That is why there is no `VerifyCommandDraft`, no `useUpdateCommand`, and no
 * editor. A greyed-out editor over a table nobody can edit is worse than
 * nothing — it describes a feature the product deliberately does not have, and
 * teaches the operator that the screen is broken rather than that the commands
 * live somewhere else.
 */

/** A verification result is pass or fail. It is not a run status. */
export type VerifyOutcome = "success" | "failed"

export interface VerifyResult {
  outcome: VerifyOutcome
  /** The run this result came out of — the row's deep link. */
  runId: string
  at: string
  durationSec: number
  /** The first useful line of output. Present on a failure. */
  detail?: string
}

export interface VerifyCommand {
  id: string
  projectId: string
  /** The file in the client's repo this was declared in, repo-relative. */
  path: string
  name: string
  /** The command line, verbatim, as it will be run in the container. */
  command: string
  /**
   * `null` when it has never run. Distinct from a failure, and it has to look
   * distinct: "never ran" is a fact about the gate's coverage, "failed" is a
   * fact about the code.
   */
  last: VerifyResult | null
}

/** Where a project's commands are declared, and how to get there. */
export interface VerifySource {
  repo: string
  ref: string
  path: string
  url: string
}

export interface VerifyProject {
  projectId: string
  enabled: boolean
  source: VerifySource
  /** When the declarations were last read out of git. */
  readAt: string
}

export interface VerifySnapshot {
  projects: VerifyProject[]
  commands: VerifyCommand[]
}
