import {
  can,
  needsLabel,
  projectOf,
  type Permission,
  type PermissionCheck,
  type Session,
} from "@/shared/session"

import type { ChatTranslator } from "./commands"
import { sharedChatT } from "./commands"
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

/**
 * The two halves of the question, in the words they keep. The registry holds
 * the EN words; the reader below resolves them through the `chat` catalogue
 * with these as `defaultValue` (D11 map-edge rule).
 */
export const PROPOSAL_WORDS: Record<
  ProposalAct,
  { confirm: string; refuse: string }
> = {
  "run.start": { confirm: "Start", refuse: "Discard" },
  "run.stop": { confirm: "Stop", refuse: "Leave running" },
  "plan.approve": { confirm: "Approve", refuse: "Reject" },
  "settings.debug": { confirm: "Turn on", refuse: "Leave off" },
}

/**
 * The two controls' words for an act, in the active locale. Per-act rather
 * than shared, because the halves are a pair that names its act — "Leave
 * running" is only the refusal of stopping.
 */
export function proposalWords(
  act: ProposalAct,
  t: ChatTranslator = sharedChatT
): { confirm: string; refuse: string } {
  const words = PROPOSAL_WORDS[act]
  return {
    confirm: t(`proposal.words.${act}.confirm`, {
      defaultValue: words.confirm,
    }),
    refuse: t(`proposal.words.${act}.refuse`, { defaultValue: words.refuse }),
  }
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
  proposal: Proposal,
  t: ChatTranslator = sharedChatT
): PermissionCheck {
  const permission = PROPOSAL_PERMISSION[proposal.act]

  if (!proposal.projectId) {
    return { allowed: false, denial: t("proposal.needsProjectDenial") }
  }

  if (can(session, permission, proposal.projectId)) {
    return ALLOWED
  }

  return {
    allowed: false,
    denial: needsLabel(permission, projectOf(session, proposal.projectId)?.key),
  }
}

/** Still a question, rather than a record of one that was answered. */
export function isPending(proposal: Proposal): boolean {
  return proposal.decision === undefined
}
