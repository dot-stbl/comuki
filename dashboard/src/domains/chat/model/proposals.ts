import {
  can,
  needsLabel,
  projectOf,
  type Permission,
  type PermissionCheck,
  type Session,
} from "@/shared/session"

import type { Proposal, ProposalAct } from "./types"

/**
 * What a proposal is asking for, and who may say yes.
 *
 * The one rule this module exists to hold: **chat tools check the same
 * permissions REST does.** There is no second matrix here, no chat-only key
 * and no shortcut for "the assistant already decided" — `PROPOSAL_PERMISSION`
 * maps an act onto the product's own `Permission`, and the answer comes from
 * the same `can()` the duty list's Approve button asks.
 *
 * Both halves of the question are gated, not only the confirming one. That
 * looks asymmetric at first — rejecting a stop changes nothing — but it is the
 * same call the approvals queue already makes, and for the same reason: a
 * decision is written to the journal either way, and "this run was not stopped
 * because a viewer declined" is a record a viewer had no standing to write.
 */
export const PROPOSAL_PERMISSION: Record<ProposalAct, Permission> = {
  "run.start": "inbox.take",
  "run.stop": "runs.stop",
  "plan.approve": "plans.approve",
  "settings.debug": "settings.live",
}

/** The two halves of the question, in the words they keep. */
export const PROPOSAL_WORDS: Record<
  ProposalAct,
  { confirm: string; refuse: string }
> = {
  "run.start": { confirm: "Start", refuse: "Discard" },
  "run.stop": { confirm: "Stop", refuse: "Leave running" },
  "plan.approve": { confirm: "Approve", refuse: "Reject" },
  "settings.debug": { confirm: "Turn on", refuse: "Leave off" },
}

const ALLOWED: PermissionCheck = { allowed: true, denial: null }

/**
 * May this session decide this proposal?
 *
 * A proposal with no project on it is refused rather than allowed. That is the
 * safe direction and it is also the true one: a state change with nowhere to
 * land is not a state change anybody can authorise, and defaulting the other
 * way would make the empty string a bypass.
 */
export function proposalCheck(
  session: Session,
  proposal: Proposal
): PermissionCheck {
  const permission = PROPOSAL_PERMISSION[proposal.act]

  if (!proposal.projectId) {
    return { allowed: false, denial: "needs a project to act in" }
  }

  if (can(session, permission, proposal.projectId)) {
    return ALLOWED
  }

  return {
    allowed: false,
    denial: needsLabel(
      permission,
      projectOf(session, proposal.projectId)?.key
    ),
  }
}

/** Still a question, rather than a record of one that was answered. */
export function isPending(proposal: Proposal): boolean {
  return proposal.decision === undefined
}
