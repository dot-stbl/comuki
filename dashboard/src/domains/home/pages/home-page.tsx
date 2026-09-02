import { useCallback, useMemo, useState } from "react"
import { RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useOutcomesQuery } from "@/domains/home/api/queries"
import {
  groupAttention,
  readAttention,
} from "@/domains/home/model/attention"
import { AttentionList } from "@/domains/home/ui/attention-list"
import { AttentionVerdict } from "@/domains/home/ui/attention-verdict"
import { HomeShortcuts } from "@/domains/home/ui/home-shortcuts"
import { OutcomesBand } from "@/domains/home/ui/outcomes-band"
import { RunningNow } from "@/domains/home/ui/running-now"
import { useApproveRun, useCancelRun } from "@/domains/runs/api/mutations"
import { useRunsQuery } from "@/domains/runs/api/queries"
import type { RunSummary } from "@/domains/runs/model/types"
import { Button, ConfirmDialog, Section, Tooltip } from "@/shared/ui"

import styles from "./home-page.module.css"

/**
 * How many rows the attention list draws before it stops and says how many are
 * left. This screen is a verdict, not a work queue: past a dozen the answer is
 * no longer "here is each one" but "there is a backlog, go to the duty list".
 * The figure in the verdict is always the true total, capped by nothing.
 */
const NEEDS_SHOWN = 12

/** A glance at what is in flight, not an inventory of it. */
const RUNNING_SHOWN = 8

/**
 * Home / Attention — "am I needed right now, and where".
 *
 * The screen is three blocks in one column and the order is the argument:
 *
 * 1. the **verdict**, which is the whole reason the screen exists — a figure
 *    when a decision is owed, a statement when none is;
 * 2. **needs you**, the runs behind that figure, each with its decision on the
 *    same line, because an observation that implies an action and does not
 *    carry it makes the operator go and find it;
 * 3. **running now** and **shortcuts**, which are the second question.
 *
 * It scrolls as one column rather than filling the viewport with panels. That
 * is a deliberate difference from the duty screen: this one is short by design
 * — on a good shift it is a sentence and two lists — and a screen built to fill
 * a viewport has to invent something to fill it with.
 */
export function HomePage() {
  const { data = [], isLoading, isError, error, refetch } = useRunsQuery()
  const outcomes = useOutcomesQuery()

  const reading = useMemo(() => readAttention(data), [data])

  const groups = useMemo(
    () => groupAttention(reading.items.slice(0, NEEDS_SHOWN)),
    [reading.items]
  )
  const running = useMemo(
    () => reading.running.slice(0, RUNNING_SHOWN),
    [reading.running]
  )
  const hidden = Math.max(0, reading.items.length - NEEDS_SHOWN)

  const approve = useApproveRun()
  const cancel = useCancelRun()
  const [stopping, setStopping] = useState<RunSummary | null>(null)

  const approveMutate = approve.mutate
  const onApprove = useCallback(
    (run: RunSummary) => {
      approveMutate(run.id)
    },
    [approveMutate]
  )
  // Tearing a container down is the one irreversible act on this screen, so it
  // asks first — the same dialog, in the same words, as the duty list.
  const onStop = useCallback((run: RunSummary) => {
    setStopping(run)
  }, [])

  const approvingId = approve.isPending ? (approve.variables ?? null) : null
  const cancellingId = cancel.isPending ? (cancel.variables ?? null) : null
  const failure = approve.error ?? cancel.error

  // An empty swarm is a state this screen renders, not a reason to render
  // nothing: "nothing needs you" is true and worth saying when there are no
  // runs at all.
  const ready = !isLoading && !isError
  const owed = reading.items.length

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[{ label: "attention" }]}
          title="Attention"
          summary={
            <>
              <span className={owed > 0 ? styles.warn : styles.strong}>
                {owed}
              </span>{" "}
              {owed === 1 ? "needs" : "need"} you ·{" "}
              <span className={styles.strong}>{reading.running.length}</span>{" "}
              running · <span className={styles.strong}>{reading.total}</span>{" "}
              runs
            </>
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="home-loading">
            <span className={styles.skeletonBand} />
            <span className={styles.skeletonRow} />
            <span className={styles.skeletonRow} />
            <span className={styles.skeletonRow} />
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert" data-test="home-error">
            <p className={styles.stateTitle}>Couldn&apos;t load the shift</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"} — until
              this loads, nothing on this screen can be trusted to say whether a
              decision is owed.
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="home-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {ready ? (
          <>
            {failure ? (
              <p className={styles.failure} role="alert">
                {failure instanceof Error
                  ? failure.message
                  : "The decision failed."}{" "}
                Nothing changed — the run is back as it was.
              </p>
            ) : null}

            <Section id="needs-you" title="Needs you">
              <AttentionVerdict
                count={owed}
                mix={reading.mix}
                worst={reading.worst}
                running={reading.running.length}
                queued={reading.queued}
              />

              {groups.length > 0 ? (
                <AttentionList
                  groups={groups}
                  hidden={hidden}
                  approvingId={approvingId}
                  cancellingId={cancellingId}
                  onApprove={onApprove}
                  onStop={onStop}
                />
              ) : null}
            </Section>

            <Section
              id="running-now"
              title="Running now"
              note={
                <>
                  {reading.running.length} in flight · {reading.queued} queued
                </>
              }
            >
              {/* The week behind the shift, above the rows: this section is
                  already the screen's second question ("what is the swarm
                  doing"), and the outcomes band is that question's time half.
                  It stays *below* "needs you" on purpose — the verdict owns
                  the top of this screen, and history never outranks a
                  decision that is owed now. */}
              {outcomes.data ? (
                <OutcomesBand days={outcomes.data} className={styles.outcomes} />
              ) : null}

              <RunningNow runs={running} total={reading.running.length} />
            </Section>

            <Section id="shortcuts" title="Shortcuts">
              <HomeShortcuts />
            </Section>
          </>
        ) : null}
      </div>

      <ConfirmDialog
        open={stopping !== null}
        danger
        title="Stop this run?"
        body={
          stopping
            ? `${stopping.title} — the container is torn down and the lease released. Work already merged stays.`
            : ""
        }
        confirmLabel="Stop run"
        cancelLabel="Keep running"
        onConfirm={() => {
          if (stopping) {
            cancel.mutate(stopping.id)
          }
          setStopping(null)
        }}
        onCancel={() => setStopping(null)}
      />
    </AppShell>
  )
}
