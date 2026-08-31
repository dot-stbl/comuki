import { ArrowRight, Check, Loader2, X } from "lucide-react"
import { Link } from "@tanstack/react-router"

import type {
  AttentionGroup,
  AttentionItem,
} from "@/domains/home/model/attention"
import { formatDuration } from "@/domains/runs/model/format"
import type { RunSummary } from "@/domains/runs/model/types"
import { currentLabel } from "@/domains/runs/model/work-items"
import { projectOf, useCan, useSession } from "@/shared/session"
import { Button, StatusBadge, Tooltip, buttonClass } from "@/shared/ui"

import styles from "./attention-list.module.css"

export interface AttentionListProps {
  /** The rows, bucketed worst-first by `groupAttention`. */
  groups: AttentionGroup[]
  /** Runs owed a decision that did not fit the cap — named, never dropped silently. */
  hidden: number
  approvingId: string | null
  cancellingId: string | null
  onApprove: (run: RunSummary) => void
  onStop: (run: RunSummary) => void
}

interface AttentionRowProps {
  item: AttentionItem
  approving: boolean
  cancelling: boolean
  onApprove: (run: RunSummary) => void
  onStop: (run: RunSummary) => void
}

/**
 * One run owed a decision, with the decision on the same line.
 *
 * The permission is asked here, per row, against **the run's own project** —
 * not against the shift. Every list in this product mixes projects by design,
 * and the same person approves on one and only watches the next; a check made
 * once for the whole screen would answer for the wrong project on half of it.
 * A row is a component (unlike a table `cell`, which is a plain function), so
 * the hook can live where the fact does.
 *
 * A refused act stays exactly where it was, at the same size, and explains
 * itself through `denied` — never `disabled`, which fires no pointer events and
 * so puts the explanation somewhere nobody can reach. `disabled` here means
 * *busy*, and only that.
 */
function AttentionRow({
  item,
  approving,
  cancelling,
  onApprove,
  onStop,
}: AttentionRowProps) {
  const session = useSession()
  const { run } = item
  const approve = useCan("plans.approve", run.projectId)
  const stop = useCan("runs.stop", run.projectId)

  const project = projectOf(session, run.projectId)
  const step = currentLabel(run)
  const busy = approving || cancelling
  const decides = item.acts.includes("approve")

  return (
    <li className={styles.row} data-test="attention-row" data-run={run.id}>
      <Link
        to="/runs/$runId"
        params={{ runId: run.id }}
        className={styles.id}
        data-test="attention-run-link"
      >
        {run.id}
      </Link>

      {/* A run in a project this session cannot see is still a run: the dash
          says the swarm is working somewhere out of view, where a blank cell
          would read as a render that failed. */}
      <span className={project ? styles.project : styles.faint}>
        {project ? project.key : "—"}
      </span>

      <span className={styles.task} title={run.title}>
        {run.title}
      </span>

      <span className={step ? styles.step : styles.faint} title={step}>
        {step || "waiting on a plan"}
      </span>

      <span className={styles.clock} title="time in step">
        {formatDuration(run.durationSec)}
      </span>

      {/* Three acts, three glyphs, and the run they act on rides in each
          accessible name — forty rows of a control called "approve" name
          nothing. The tooltip repeats the word for the pointer and the
          keyboard; a denied control puts its sentence there instead, which is
          the only reason `denied` and not `disabled` keeps it reachable. */}
      <span className={styles.actions}>
        {decides ? (
          <>
            <Tooltip content={approve.denial ?? "Approve"}>
              <Button
                size="icon"
                data-test="attention-approve"
                disabled={busy}
                denied={approve.denial}
                aria-busy={approving || undefined}
                aria-label={`Approve ${run.title}`}
                onClick={() => onApprove(run)}
              >
                {approving ? (
                  <Loader2 className={styles.spin} aria-hidden="true" />
                ) : (
                  <Check aria-hidden="true" />
                )}
              </Button>
            </Tooltip>
            <Tooltip content={stop.denial ?? "Stop"}>
              <Button
                size="icon"
                variant="destructive"
                data-test="attention-stop"
                disabled={busy}
                denied={stop.denial}
                aria-label={`Stop ${run.title}`}
                onClick={() => onStop(run)}
              >
                <X aria-hidden="true" />
              </Button>
            </Tooltip>
          </>
        ) : null}

        {/* A link that has to look like a control takes the recipe rather than
            nesting an anchor inside a button. It is last on every row, decided
            or not, so the column ends the same way everywhere and the eye can
            run straight down it. An arrow rather than a second cross or tick:
            this is the one control in the row that goes somewhere. */}
        <Tooltip content="Open">
          <Link
            to="/runs/$runId"
            params={{ runId: run.id }}
            data-test="attention-open"
            aria-label={`Open ${run.title}`}
            className={buttonClass({ variant: "outline", size: "icon" })}
          >
            <ArrowRight aria-hidden="true" />
          </Link>
        </Tooltip>
      </span>
    </li>
  )
}

/**
 * Needs you — every run whose next move belongs to a person, worst-first.
 *
 * Grouped rather than flat, and that is what lets a row drop its status column:
 * the heading carries the state once, as the product's own sentence, instead of
 * a badge repeating it on every line. Three buckets and their sizes are also
 * the fastest possible read of *what kind* of attention is owed — which is the
 * second question after "how much".
 */
export function AttentionList({
  groups,
  hidden,
  approvingId,
  cancellingId,
  onApprove,
  onStop,
}: AttentionListProps) {
  return (
    <div className={styles.list} data-test="attention-list">
      {/* Column names, once for the whole list rather than once per bucket.
          Hidden from assistive tech: each row already reads as a sentence, and
          a repeated header would only add six words in front of every one. */}
      <div className={styles.columns} aria-hidden="true">
        <span>run</span>
        <span>project</span>
        <span>task</span>
        <span>step</span>
        <span className={styles.clockHead}>in step</span>
        <span />
      </div>

      {groups.map((group) => (
        <section
          key={group.status}
          className={styles.group}
          data-test="attention-group"
          data-status={group.status}
          aria-label={`${group.items.length} ${group.status} — ${group.reason}`}
        >
          <h3 className={styles.groupHead}>
            <StatusBadge status={group.status} size="sm" />
            <span className={styles.reason}>{group.reason}</span>
            <span className={styles.groupCount}>{group.items.length}</span>
          </h3>

          <ul className={styles.rows}>
            {group.items.map((item) => (
              <AttentionRow
                key={item.run.id}
                item={item}
                approving={approvingId === item.run.id}
                cancelling={cancellingId === item.run.id}
                onApprove={onApprove}
                onStop={onStop}
              />
            ))}
          </ul>
        </section>
      ))}

      {hidden > 0 ? (
        <p className={styles.more}>
          <Link
            to="/runs"
            className={styles.moreLink}
            data-test="attention-more"
          >
            and {hidden} more — open live runs
          </Link>
        </p>
      ) : null}
    </div>
  )
}
