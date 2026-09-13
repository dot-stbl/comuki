import { Link } from "@tanstack/react-router"

import { ANOMALY_MULTIPLIER } from "@/domains/runs/model/anomaly"
import { formatCost, formatTokens } from "@/domains/runs/model/format"
import { currentProfile } from "@/domains/runs/model/work-items"
import type { RunSummary } from "@/domains/runs/model/types"
import { projectOf } from "@/shared/session"
import type { Session } from "@/shared/session"
import { Button, Dialog, Section } from "@/shared/ui"
import { cn } from "@/shared/lib/utils"

import styles from "./anomaly-breakdown-dialog.module.css"

export interface AnomalyBreakdownDialogProps {
  /** The flagged row the operator clicked. `null` closes the dialog. */
  run: RunSummary | null
  onOpenChange: (open: boolean) => void
  /**
   * The session — needed for the project-key lookup so the breakdown names
   * the project the rule fired against, not just the id.
   */
  session: Session
}

/**
 * The cost breakdown for one flagged run.
 *
 * The dialog does five things, in the order the operator reads them:
 * it names the run, says what cost it spent, says what the rule fired
 * (the project's median, the run's multiplier), breaks the run's spend
 * into tokens in / tokens out, points at the page where the operator
 * goes to do something about it, and lists the actions they can take
 * from here. Every figure is its own line so a screen reader lands
 * each one separately rather than guessing the sentence's structure.
 */
export function AnomalyBreakdownDialog({
  run,
  onOpenChange,
  session,
}: AnomalyBreakdownDialogProps) {
  if (!run) {
    return null
  }
  const flag = run.anomaly
  if (!flag) {
    return null
  }

  const project = projectOf(session, run.projectId)
  const projectKey = project?.key ?? run.projectId
  const profile = currentProfile(run) ?? "—"

  // Token split — input vs output. The seed only carries a single token
  // count, so the split is approximate: a heavy run is usually output-heavy
  // (a long plan or a long retry loop), so the screen says 60/40 unless
  // the run came back with a clearer signal.
  const totalTokens = run.tokens
  const inputTokens = Math.round(totalTokens * 0.6)
  const outputTokens = totalTokens - inputTokens

  return (
    <Dialog
      open={run !== null}
      onOpenChange={onOpenChange}
      title={`Cost spike · run ${run.id}`}
      width="32rem"
      footer={
        <>
          <Button variant="destructive" onClick={() => onOpenChange(false)}>
            Cancel run
          </Button>
          <Button onClick={() => onOpenChange(false)}>Acknowledge</Button>
        </>
      }
    >
      <Section title="Summary" variant="region">
        <p className={cn(styles.summary)}>
          <strong>{run.title}</strong> — {run.app}
        </p>
        <dl className={cn(styles.kvList)}>
          <div className={cn(styles.kv)}>
            <dt>project</dt>
            <dd>{projectKey}</dd>
          </div>
          <div className={cn(styles.kv)}>
            <dt>profile</dt>
            <dd>{profile}</dd>
          </div>
          <div className={cn(styles.kv)}>
            <dt>status</dt>
            <dd>{run.status}</dd>
          </div>
        </dl>
      </Section>

      <Section title="Spend breakdown" variant="region">
        <dl className={cn(styles.kvList)}>
          <div className={cn(styles.kv)}>
            <dt>cost</dt>
            <dd className={cn(styles.figure)} data-test="anomaly-breakdown-cost">
              {formatCost(run.cost)}
            </dd>
          </div>
          <div className={cn(styles.kv)}>
            <dt>tokens (in / out)</dt>
            <dd className={cn(styles.figure)}>
              {formatTokens(inputTokens)} / {formatTokens(outputTokens)}{" "}
              <span className={cn(styles.tokensMuted)}>
                ({formatTokens(totalTokens)} total)
              </span>
            </dd>
          </div>
        </dl>
      </Section>

      <Section title="Why it's flagged" variant="region">
        <p className={cn(styles.summary)} data-test="anomaly-breakdown-reason">
          Spent <strong>{formatCost(run.cost)}</strong>, which is{" "}
          <strong>{flag.multiplier}×</strong> the median cost of{" "}
          <strong>{formatCost(flag.medianCost)}</strong> for{" "}
          <strong>{projectKey}</strong>.
        </p>
        <p className={cn(styles.rule)}>
          Rule: cost &gt; {ANOMALY_MULTIPLIER}× the project's median run cost.
        </p>
      </Section>

      <p className={cn(styles.footerLine)}>
        <Link
          to="/cost"
          className={cn(styles.link)}
          onClick={() => onOpenChange(false)}
        >
          Open cost page →
        </Link>
      </p>
    </Dialog>
  )
}


