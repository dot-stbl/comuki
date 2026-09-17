import type { Approval } from "@/domains/approvals/model/types"
import type { SeedApproval } from "@/shared/api/mock"
import { PROJECT_BY_APP } from "@/shared/api/mock/runs.seed"

/* The mock queue's shapes, and — since the runs read API could answer it —
   the wire's. The host has no approvals endpoint: a run needing a human *is*
   an escalated run, and the queue is the runs list narrowed to them. */

export function toApproval(seed: SeedApproval): Approval {
  return {
    id: seed.id,
    type: seed.type,
    app: seed.app,
    // An app is built inside one project and does not move, so the run, the
    // ticket and the approval can never disagree about where they live.
    projectId: PROJECT_BY_APP[seed.app] ?? "",
    runId: seed.run,
    age: seed.age,
    risk: seed.risk,
    summary: seed.summary,
    assumptions: seed.assumptions,
  }
}

/* ------------------------------------------------------------------ *
 * The wire — `GET /api/v1/runs?filter=status==Escalated`.
 *
 * The kubb client types the runs page, but the approvals mapping wants its
 * own claim about the row (it reads only five fields), so the shape is
 * restated here beside the mapper that reads it.
 * ------------------------------------------------------------------ */

/** One escalated run, as the runs list answers it. */
export interface EscalatedRunWire {
  readonly id: string
  readonly projectId: string
  readonly status: string
  readonly createdAt: string
  readonly updatedAt: string
}

/** The runs page envelope. */
export interface EscalatedRunsPageWire {
  readonly items: readonly EscalatedRunWire[]
  readonly page: number
  readonly pageSize: number
  readonly total: number
}

/** How long the run has waited, in the seed's own words ("12 min", "3 h"). */
function waitingAge(updatedAt: string, nowMs: number): string {
  const at = Date.parse(updatedAt)
  if (Number.isNaN(at)) {
    return "—"
  }
  const minutes = Math.max(0, Math.round((nowMs - at) / 60_000))
  if (minutes < 60) {
    return `${minutes} min`
  }
  const hours = Math.floor(minutes / 60)
  return `${hours} h`
}

/**
 * An escalated run onto the queue's decision card.
 *
 * The wire says *that* a run escalated, never *why* — so the type is `gate`,
 * the risk is unread rather than guessed, and the summary is the one honest
 * sentence about the mechanism. What it does carry is the identity the
 * decision needs: the run, its project, and how long it has waited.
 */
export function escalatedRunToApproval(
  run: EscalatedRunWire,
  nowMs: number = Date.now()
): Approval {
  return {
    id: run.id,
    type: "gate",
    app: run.id,
    projectId: run.projectId,
    runId: run.id,
    age: waitingAge(run.updatedAt, nowMs),
    risk: null,
    summary:
      "The orchestrator escalated this run back to a human gate. Approve releases it to the swarm; reject cancels it.",
    assumptions: [],
  }
}

/** A page of escalated runs onto the queue. */
export function escalatedRunsToApprovals(
  page: EscalatedRunsPageWire,
  nowMs: number = Date.now()
): Approval[] {
  return page.items.map((run) => escalatedRunToApproval(run, nowMs))
}

/* ------------------------------------------------------------------ *
 * The learning wire — `GET /api/v1/learning/candidates?status=pending`.
 *
 * A learning candidate is a rule a worker proposed (learning.suggest)
 * waiting for adoption. The kubb client types the row, but the approvals
 * mapping wants its own claim about it, so the shape is restated here
 * beside the mapper that reads it.
 * ------------------------------------------------------------------ */

/** One pending learning candidate, as the learning API answers it. */
export interface LearningCandidateWire {
  readonly id: string
  readonly projectId: string
  readonly topic: string
  readonly observation: string
  readonly proposedRule: string
  readonly sourceRef: string
  readonly repeatCount: number | string
  readonly status: string
  readonly createdAt: string
}

/**
 * A learning candidate onto the queue's decision card.
 *
 * What is being decided is the rule, so that is the summary; the observation
 * is the evidence behind it and waits behind the disclosure under its own
 * name — never as a "planner assumption", which it is not. The risk is
 * unread (a worker's suggestion carries no risk judgement) and the app line
 * is the topic the rule filed itself under. A repeat count above one is how
 * the platform says other workers hit the same thing, which is exactly the
 * weight an approver wants before adopting a rule.
 */
export function learningCandidateToApproval(
  candidate: LearningCandidateWire,
  nowMs: number = Date.now()
): Approval {
  const repeats = Number(candidate.repeatCount)
  return {
    id: candidate.id,
    type: "learning",
    app: candidate.topic,
    projectId: candidate.projectId,
    runId: "",
    age: waitingAge(candidate.createdAt, nowMs),
    risk: null,
    summary:
      repeats > 1
        ? `${candidate.proposedRule} — ${repeats} workers suggested this`
        : candidate.proposedRule,
    assumptions: [candidate.observation],
    assumptionsHeading: "Observation",
  }
}

/** A page of learning candidates onto the queue. */
export function learningCandidatesToApprovals(
  candidates: readonly LearningCandidateWire[],
  nowMs: number = Date.now()
): Approval[] {
  return candidates.map((candidate) =>
    learningCandidateToApproval(candidate, nowMs)
  )
}
