import { Link } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import { formatDuration } from "@/shared/lib/duration"
import type { RunSummary } from "@/domains/runs/model/types"
import { currentLabel, currentProfile } from "@/domains/runs/model/work-items"
import { projectOf, useSession } from "@/shared/session"

import styles from "./running-now.module.css"

export interface RunningNowProps {
  /** The rows to draw — already capped by the screen. Longest in step first. */
  runs: RunSummary[]
  /** Every run in flight, so the footer can name what did not fit. */
  total: number
}

/**
 * Running now — what the swarm is chewing on, at a glance.
 *
 * Deliberately not a second duty table. It carries no status column (every row
 * here is running, and the heading says so once), no filters, no sorting and no
 * actions — nothing is owed on these rows, which is the whole reason they are
 * *below* the block that answers this screen's question.
 *
 * Its one opinion is the order: longest in step first. A run that has sat on
 * one step far longer than its neighbours is the likeliest next occupant of the
 * block above, so the rows that matter most are the ones already at the top.
 */
export function RunningNow({ runs, total }: RunningNowProps) {
  const { t } = useTranslation("home")
  const session = useSession()
  const hidden = Math.max(0, total - runs.length)

  if (total === 0) {
    return (
      <p className={styles.quiet} data-test="running-empty">
        {t("running.empty")}
      </p>
    )
  }

  return (
    <div className={styles.list} data-test="running-now">
      <ul className={styles.rows}>
        {runs.map((run) => {
          const project = projectOf(session, run.projectId)
          const step = currentLabel(run)
          const profile = currentProfile(run)

          return (
            <li className={styles.row} key={run.id} data-run={run.id}>
              <Link
                to="/runs/$runId"
                params={{ runId: run.id }}
                className={styles.id}
              >
                {run.id}
              </Link>

              <span className={project ? styles.meta : styles.faint}>
                {project ? project.key : "—"}
              </span>

              <span className={styles.task} title={run.title}>
                {run.title}
              </span>

              {/* Profile, then the brain's name for the step: the identity the
                  swarm can be aggregated on, then the prose it invented for
                  this ticket. Two different kinds of name, kept apart. */}
              <span className={profile ? styles.meta : styles.faint}>
                {profile || "—"}
              </span>

              <span className={styles.step} title={step}>
                {step || t("list.waitingOnPlan")}
              </span>

              <span className={styles.clock} title={t("list.timeInStep")}>
                {formatDuration(run.durationSec)}
              </span>
            </li>
          )
        })}
      </ul>

      {hidden > 0 ? (
        <p className={styles.more}>
          <Link to="/runs" className={styles.moreLink} data-test="running-more">
            {t("running.more", { count: hidden })}
          </Link>
        </p>
      ) : null}
    </div>
  )
}
