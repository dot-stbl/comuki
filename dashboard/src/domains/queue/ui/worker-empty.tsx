import { FilterX } from "lucide-react"

import { Button, ScreenState, Tooltip } from "@/shared/ui"

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

/** The one line each of the four cases leads with. */
const TITLES: Record<WorkerEmptyKind, string> = {
  filtered: "No workers match the filters",
  backlog: "No workers yet",
  "at-rest": "No workers, and none wanted",
  "under-target": "The pool is under its target",
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

  /* One sentence per case, and they have to stay four different sentences:
     every one of these pools is empty and three of the four are correct. */
  const description =
    kind === "filtered" ? (
      <>
        The pool is up — <span className={styles.figure}>{poolSize}</span>{" "}
        worker
        {poolSize === 1 ? "" : "s"}
        {where} are running, they are just not these.
      </>
    ) : kind === "backlog" ? (
      <>
        min idle = <span className={styles.figure}>0</span>
        {where}, so the pool sits empty until there is work to do. There is now:{" "}
        <span className={styles.figure}>{backlog}</span> item
        {backlog === 1 ? "" : "s"} queued and unclaimed. Scale raises a worker
        to take them.
      </>
    ) : kind === "at-rest" ? (
      <>
        min idle = <span className={styles.figure}>0</span>
        {where} and nothing is queued. An empty pool is the configured resting
        state here, not an outage — a container is created per task and torn
        down after it.
      </>
    ) : (
      <>
        min idle = <span className={styles.figure}>{minIdle}</span>
        {where} and no workers are up. This one is not a resting state: compute
        is not raising them, and{" "}
        <span className={styles.figure}>{backlog}</span> item
        {backlog === 1 ? "" : "s"} will wait until it does.
      </>
    )

  return (
    <ScreenState
      kind="empty"
      /* `data-kind` is the finer reading — which of the four empties the model
         resolved — and it rides on the title rather than on the state's own
         box. `ScreenState` stamps `data-state="empty"` for all four and
         forwards no other data attribute, so the distinction needs an element
         of its own; the title is the one that is always there and the one that
         actually differs between the four. */
      title={<span data-kind={kind}>{TITLES[kind]}</span>}
      description={description}
      inset="gutter"
      data-test="worker-empty"
      action={
        kind === "filtered" && onClearFilters ? (
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
        ) : null
      }
    />
  )
}
