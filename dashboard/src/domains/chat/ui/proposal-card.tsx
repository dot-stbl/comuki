import {
  PROPOSAL_WORDS,
  proposalCheck,
} from "@/domains/chat/model/proposals"
import type { Proposal, ProposalDecision } from "@/domains/chat/model/types"
import { projectOf, useSession } from "@/shared/session"
import { Button } from "@/shared/ui"

import styles from "./chat-message.module.css"

export interface ProposalCardProps {
  proposal: Proposal
  onDecide: (proposalId: string, decision: ProposalDecision) => void
  busy?: boolean
}

/** The act, in the product's own words rather than in its key. */
const ACT_LABEL: Record<Proposal["act"], string> = {
  "run.start": "start a run",
  "run.stop": "stop a run",
  "plan.approve": "approve a plan",
  "settings.debug": "change a live setting",
}

/**
 * A state change the assistant is offering, and a human has to press.
 *
 * ## Why this is the whole design
 *
 * The console never performs a state change because it decided to. Everything
 * that would change the world — starting a run, stopping one, approving a
 * plan, turning a live setting — arrives here first, as a question with two
 * halves. The assistant's job ends at the question.
 *
 * Which means this component has exactly one job that matters: it must be
 * impossible for it to act on its own. There is no effect on this component,
 * no auto-confirm after a delay, no "confirmed by default" and no path from
 * mounting it to `onDecide`. The only caller of `onDecide` is a click on one of
 * the two controls below.
 *
 * ## The refusal
 *
 * A chat tool checks the same permission REST does, resolved per project with
 * `can(session, permission, projectId)`. When it fails, both halves stay
 * exactly where they were, take `aria-disabled`, carry the sentence on
 * `data-denied`, and swallow the click — never `disabled`, which fires no
 * pointer events and would put the explanation out of reach of the pointer
 * that went looking for it.
 *
 * The sentence is also rendered as text, not only as a tooltip. A refusal
 * discovered by hovering is a refusal most people never discover.
 *
 * ## Words, not glyphs
 *
 * Action buttons in this product are icon buttons. These two are the exception
 * and the rule says why: they are the two halves of one question, and a
 * question answered with two pictures is a question nobody is sure they
 * answered.
 */
export function ProposalCard({
  proposal,
  onDecide,
  busy = false,
}: ProposalCardProps) {
  const session = useSession()
  const check = proposalCheck(session, proposal)
  const words = PROPOSAL_WORDS[proposal.act]
  const project = projectOf(session, proposal.projectId)
  const decided = proposal.decision !== undefined

  return (
    <article
      className={styles.proposal}
      data-test="chat-proposal"
      data-proposal={proposal.id}
      data-act={proposal.act}
      data-decision={proposal.decision}
    >
      <header className={styles.proposalHead}>
        <span className={styles.proposalAct}>{ACT_LABEL[proposal.act]}</span>
        <span className={styles.proposalScope} data-test="chat-proposal-scope">
          in {project ? project.key : "no project"}
        </span>
        {proposal.subject ? (
          <span className={styles.proposalSubject}>{proposal.subject}</span>
        ) : null}
      </header>

      <p className={styles.proposalSummary}>{proposal.summary}</p>

      {proposal.steps && proposal.steps.length > 0 ? (
        <ol className={styles.steps} data-test="chat-proposal-steps">
          {proposal.steps.map((step, index) => (
            <li key={`${step.profile}-${index}`} className={styles.step}>
              <span className={styles.stepProfile}>{step.profile}</span>
              <span className={styles.stepLabel}>{step.label}</span>
            </li>
          ))}
        </ol>
      ) : null}

      {decided ? (
        <p className={styles.decided} data-test="chat-proposal-decided">
          {proposal.decision === "confirmed"
            ? "confirmed by a human, and recorded where every other decision is"
            : "declined by a human, and recorded where every other decision is"}
        </p>
      ) : (
        <div className={styles.proposalActions}>
          {check.denial ? (
            <p className={styles.denial} data-test="chat-proposal-denial">
              {check.denial}
            </p>
          ) : null}
          <div className={styles.proposalPair}>
            {/* The kit's `Button` already *is* the refusal contract —
                `aria-disabled`, the sentence on `data-denied`, and a swallowed
                click — so spelling it a second time here would be a second
                place for it to drift. `denied` on both halves, because both
                write a decision to the journal: the approvals queue makes the
                same call for the same reason. */}
            <Button
              variant="outline"
              denied={check.denial}
              disabled={busy}
              data-test="chat-proposal-reject"
              aria-label={`${words.refuse} — ${proposal.summary}`}
              onClick={() => onDecide(proposal.id, "rejected")}
            >
              {words.refuse}
            </Button>
            <Button
              variant={proposal.act === "run.stop" ? "destructive" : "default"}
              denied={check.denial}
              disabled={busy}
              data-test="chat-proposal-confirm"
              aria-label={`${words.confirm} — ${proposal.summary}`}
              onClick={() => onDecide(proposal.id, "confirmed")}
            >
              {words.confirm}
            </Button>
          </div>
        </div>
      )}
    </article>
  )
}
