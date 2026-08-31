import { FilterX } from "lucide-react"

import { Button, Tooltip } from "@/shared/ui"

import type { WorkerEmptyKind } from "@/domains/queue/model/queue"

import styles from "./worker-empty.module.css"

/**
 * What an empty worker pool means — which is four different things.
 *
 * This is the state the screen most has to get right. An empty pool is
 * *usually correct*: `min idle = 0` is create-per-task, and it is how most
 * projects are configured, so the resting state of a healthy pool is nothing
 * at all. A blank band would teach the duty engineer that the screen is broken
 * and, worse, would look identical on the one day the pool really is failing
 * to come up. So each case says its own sentence and names the number it read.
 */

export interface WorkerEmptyProps {
  kind: WorkerEmptyKind
  /** Queued, unclaimed items in the same slice the filters describe. */
  backlog: number
  /** Idle workers this slice is configured to keep. */
  minIdle: number
  /** Workers the project filter alone leaves — what "filtered" is hiding. */
  poolSize: number
  /** The project handle, when the list is narrowed to one. */
  projectKey?: string
  onClearFilters?: () => void
}

export function WorkerEmpty({
  kind,
  backlog,
  minIdle,
  poolSize,
  projectKey,
  onClearFilters,
}: WorkerEmptyProps) {
  const where = projectKey ? ` on ${projectKey}` : ""

  return (
    <div className={styles.state} data-test="worker-empty" data-kind={kind}>
      {kind === "filtered" ? (
        <>
          <p className={styles.title}>No workers match the filters</p>
          <p className={styles.body}>
            The pool is up —{" "}
            <span className={styles.figure}>{poolSize}</span> worker
            {poolSize === 1 ? "" : "s"}
            {where} are running, they are just not these.
          </p>
          {onClearFilters ? (
            <span>
              <Tooltip content="Clear filters">
                <Button
                  size="icon-sm"
                  variant="outline"
                  data-test="worker-empty-clear"
                  aria-label="Clear filters"
                  onClick={onClearFilters}
                >
                  <FilterX aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          ) : null}
        </>
      ) : null}

      {kind === "backlog" ? (
        <>
          <p className={styles.title}>No workers yet</p>
          <p className={styles.body}>
            min idle = <span className={styles.figure}>0</span>
            {where}, so the pool sits empty until there is work to do. There is
            now: <span className={styles.figure}>{backlog}</span> item
            {backlog === 1 ? "" : "s"} queued and unclaimed. Scale raises a
            worker to take them.
          </p>
        </>
      ) : null}

      {kind === "at-rest" ? (
        <>
          <p className={styles.title}>No workers, and none wanted</p>
          <p className={styles.body}>
            min idle = <span className={styles.figure}>0</span>
            {where} and nothing is queued. An empty pool is the configured
            resting state here, not an outage — a container is created per task
            and torn down after it.
          </p>
        </>
      ) : null}

      {kind === "under-target" ? (
        <>
          <p className={styles.title}>The pool is under its target</p>
          <p className={styles.body}>
            min idle = <span className={styles.figure}>{minIdle}</span>
            {where} and no workers are up. This one is not a resting state:
            compute is not raising them, and{" "}
            <span className={styles.figure}>{backlog}</span> item
            {backlog === 1 ? "" : "s"} will wait until it does.
          </p>
        </>
      ) : null}
    </div>
  )
}
