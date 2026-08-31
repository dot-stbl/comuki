import { useState } from "react"
import { Check, ChevronDown, Eye, Image, X } from "lucide-react"

import type {
  Approval,
  ApprovalDecision,
} from "@/domains/approvals/model/types"
import { useRunsQuery } from "@/domains/runs/api/queries"
import { RunGraph } from "@/domains/runs/ui/run-graph"
import { cn } from "@/shared/lib/utils"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, Tooltip } from "@/shared/ui"

import {
  ApprovalRiskBadge,
  ApprovalTypeBadge,
} from "./approval-badges"
import { APPROVAL_TYPE_META } from "./approval-type-meta"
import styles from "./approval-card.module.css"

/** The two panes a baseline decision compares. Fictional until the store lands. */
const BASELINE_PANES = ["baseline", "new"] as const

export interface ApprovalCardProps {
  approval: Approval
  onAction: (id: string, decision: ApprovalDecision) => void
  busy?: boolean
}

/**
 * One decision waiting on a person.
 *
 * Not a card in the design system's sense — no fill that lifts it off the
 * floor, no shadow that floats it. It is a data surface: bounded by hairlines,
 * the lane material it is made of, and the surface step of the corner scale,
 * because a block this size reads as a square slab without one and every
 * control standing on it is rounded. The corner was never what made a card a
 * card.
 *
 * The reading is arranged around the one question the queue is opened with —
 * *what is this and how much does it cost to get wrong* — so the kind, the app
 * and the risk share the first line and the summary is the second. Everything
 * that takes reading to answer is behind the disclosure, because a queue of
 * fully-expanded cards is a queue nobody scrolls to the end of.
 */
export function ApprovalCard({
  approval,
  onAction,
  busy = false,
}: ApprovalCardProps) {
  const [open, setOpen] = useState(false)
  const session = useSession()

  // Asked per card, not per screen. The queue mixes projects, and the same
  // person approves on one and only watches another — one answer for the whole
  // list would refuse decisions they are entitled to make, or offer ones they
  // are not. All three decisions ride this check: approve, reject and review
  // are one act seen from three sides, and every one of them writes.
  const allowed = can(session, "plans.approve", approval.projectId)
  const decide = allowed
    ? { allowed: true, denial: null }
    : {
        allowed: false,
        denial: needsLabel(
          "plans.approve",
          projectOf(session, approval.projectId)?.key
        ),
      }
  const { data: runs = [] } = useRunsQuery()
  const run = runs.find((item) => item.id === approval.runId)
  const { noun } = APPROVAL_TYPE_META[approval.type]

  return (
    <article
      className={styles.card}
      data-test="approval-card"
      data-approval={approval.id}
    >
      <header className={styles.head}>
        <ApprovalTypeBadge type={approval.type} />
        <span className={styles.app}>{approval.app}</span>
        <ApprovalRiskBadge risk={approval.risk} />
        <span className={styles.age}>{approval.age}</span>
      </header>

      <p className={styles.summary}>{approval.summary}</p>

      <div className={styles.actions}>
        {/* Reading the plan is not a decision, so the disclosure keeps its word
            and stays open to everyone — the person who cannot approve is often
            exactly the one asked why. */}
        <Button
          type="button"
          size="sm"
          variant="ghost"
          data-test="approval-details"
          aria-expanded={open}
          onClick={() => setOpen((value) => !value)}
        >
          <ChevronDown
            aria-hidden="true"
            className={cn(styles.chevron, open && styles.chevronOpen)}
          />
          {open ? "Hide" : "Details"}
        </Button>

        <span className={styles.spacer} />

        {/* Three acts, so three glyphs: an eye that only looks, a cross that
            refuses and a tick that agrees. Each names the approval it decides in
            its own accessible name, because a queue of cards would otherwise
            offer a column of controls all called "approve". A refused one keeps
            its place and says what is missing instead of disappearing — and it
            is `denied`, never `disabled`, because a disabled control fires no
            pointer events and its explanation would never arrive. */}
        <Tooltip content={decide.denial ?? "Review"}>
          <Button
            type="button"
            size="icon-sm"
            variant="ghost"
            disabled={busy}
            denied={decide.denial}
            data-test="approval-review"
            aria-label={`Review the ${noun} for ${approval.app}`}
            onClick={() => onAction(approval.id, "review")}
          >
            <Eye aria-hidden="true" />
          </Button>
        </Tooltip>
        <Tooltip content={decide.denial ?? "Reject"}>
          <Button
            type="button"
            size="icon-sm"
            variant="destructive"
            disabled={busy}
            denied={decide.denial}
            data-test="approval-reject"
            aria-label={`Reject the ${noun} for ${approval.app}`}
            onClick={() => onAction(approval.id, "reject")}
          >
            <X aria-hidden="true" />
          </Button>
        </Tooltip>
        <Tooltip content={decide.denial ?? "Approve"}>
          <Button
            type="button"
            size="icon-sm"
            disabled={busy}
            denied={decide.denial}
            data-test="approval-approve"
            aria-label={`Approve the ${noun} for ${approval.app}`}
            onClick={() => onAction(approval.id, "approve")}
          >
            <Check aria-hidden="true" />
          </Button>
        </Tooltip>
      </div>

      {open ? (
        <div className={styles.detail} data-test="approval-detail">
          {approval.type === "plan" && run ? (
            <section className={styles.region}>
              <h3 className={styles.regionHead}>Plan — work item graph</h3>
              {/* The plan preview has nothing to select, so the graph is drawn
                  as static content and sizes itself from the columns rather
                  than from a height this surface does not have. */}
              <RunGraph
                items={run.workItems}
                current={run.current}
                fit="content"
                label="Plan — work item graph"
              />
            </section>
          ) : null}

          {approval.type === "baseline" ? (
            <div className={styles.panes}>
              {BASELINE_PANES.map((label) => (
                <figure key={label} className={styles.pane}>
                  <figcaption className={styles.paneHead}>{label}</figcaption>
                  <span className={styles.paneBody}>
                    <Image className={styles.paneIcon} aria-hidden="true" />
                  </span>
                </figure>
              ))}
            </div>
          ) : null}

          <section className={styles.region}>
            <h3 className={styles.regionHead}>Planner assumptions</h3>
            <ul className={styles.assumptions}>
              {approval.assumptions.map((item) => (
                <li key={item} className={styles.assumption}>
                  <span className={styles.arrow} aria-hidden="true">
                    →
                  </span>
                  {item}
                </li>
              ))}
            </ul>
          </section>
        </div>
      ) : null}
    </article>
  )
}
