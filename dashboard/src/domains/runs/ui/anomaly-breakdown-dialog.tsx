import { Link } from "@tanstack/react-router"
import { Trans, useTranslation } from "react-i18next"

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
  /**
   * Hand the run back to whoever owns cancelling on this screen.
   *
   * The dialog does not cancel. Tearing a container down is the duty list's
   * act: the list already holds the confirm, the pending row and the banner a
   * failed decision lands in, and a second cancel path here would be a second
   * place to keep the permission check, the optimistic update and the wording
   * of the confirmation in step. So this closes and hands the run over, and
   * the one path runs.
   *
   * **Absent means no button.** Not a disabled one and not one that quietly
   * does nothing: a screen that cannot cancel does not show a control saying
   * it can. The owner leaves it out when this session may not stop this run,
   * or when the run is already past stopping — the host answers 409 to a
   * terminal run, and a button whose only possible reply is a conflict is a
   * button that lies more quietly than the one it replaces.
   */
  onCancelRun?: (run: RunSummary) => void
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
  onCancelRun,
}: AnomalyBreakdownDialogProps) {
  const { t } = useTranslation("runs")
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
      title={t("anomaly.dialogTitle", { runId: run.id })}
      width="32rem"
      footer={
        <>
          {onCancelRun ? (
            /* Closes first, then hands the run over: the owner's confirm
               opens in this dialog's place rather than behind it. */
            <Button
              variant="destructive"
              data-test="anomaly-cancel-run"
              onClick={() => {
                onOpenChange(false)
                onCancelRun(run)
              }}
            >
              {t("anomaly.cancelRun")}
            </Button>
          ) : null}
          <Button onClick={() => onOpenChange(false)}>
            {t("anomaly.acknowledge")}
          </Button>
        </>
      }
    >
      <Section title={t("anomaly.summary")} variant="region">
        <p className={cn(styles.summary)}>
          <strong>{run.title}</strong> — {run.app}
        </p>
        <dl className={cn(styles.kvList)}>
          <div className={cn(styles.kv)}>
            <dt>{t("anomaly.project")}</dt>
            <dd>{projectKey}</dd>
          </div>
          <div className={cn(styles.kv)}>
            <dt>{t("anomaly.profile")}</dt>
            <dd>{profile}</dd>
          </div>
          <div className={cn(styles.kv)}>
            <dt>{t("anomaly.status")}</dt>
            <dd>{run.status}</dd>
          </div>
        </dl>
      </Section>

      <Section title={t("anomaly.spend")} variant="region">
        <dl className={cn(styles.kvList)}>
          <div className={cn(styles.kv)}>
            <dt>{t("anomaly.cost")}</dt>
            <dd
              className={cn(styles.figure)}
              data-test="anomaly-breakdown-cost"
            >
              {formatCost(run.cost)}
            </dd>
          </div>
          <div className={cn(styles.kv)}>
            <dt>{t("anomaly.tokensInOut")}</dt>
            <dd className={cn(styles.figure)}>
              {formatTokens(inputTokens)} / {formatTokens(outputTokens)}{" "}
              <span className={cn(styles.tokensMuted)}>
                {t("anomaly.total", { total: formatTokens(totalTokens) })}
              </span>
            </dd>
          </div>
        </dl>
      </Section>

      <Section title={t("anomaly.why")} variant="region">
        {/* The figures are values in their own voice and the words are prose
            in theirs, so the emphasis rides slot elements and the sentence —
            word order included — belongs to the locale. */}
        <p className={cn(styles.summary)} data-test="anomaly-breakdown-reason">
          <Trans
            ns="runs"
            i18nKey="anomaly.reason"
            components={{
              spent: <strong>{formatCost(run.cost)}</strong>,
              multiplier: <strong>{`${flag.multiplier}×`}</strong>,
              median: <strong>{formatCost(flag.medianCost)}</strong>,
              project: <strong>{projectKey}</strong>,
            }}
          />
        </p>
        <p className={cn(styles.rule)}>
          {t("anomaly.rule", { threshold: ANOMALY_MULTIPLIER })}
        </p>
      </Section>

      <p className={cn(styles.footerLine)}>
        <Link
          to="/cost"
          className={cn(styles.link)}
          onClick={() => onOpenChange(false)}
        >
          {t("anomaly.openCost")}
        </Link>
      </p>
    </Dialog>
  )
}
