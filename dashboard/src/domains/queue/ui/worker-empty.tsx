import { FilterX } from "lucide-react"
import { Trans, useTranslation } from "react-i18next"

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

/** The one line each of the four cases leads with, as `queue`-namespace keys. */
const TITLE_KEYS: Record<WorkerEmptyKind, string> = {
  filtered: "workerEmpty.filteredTitle",
  backlog: "workerEmpty.backlogTitle",
  "at-rest": "workerEmpty.atRestTitle",
  "under-target": "workerEmpty.underTargetTitle",
}

export function WorkerEmpty({
  kind,
  backlog,
  minIdle,
  poolSize,
  projectKey,
  onClearFilters,
}: WorkerEmptyProps) {
  const { t } = useTranslation("queue")
  const where = projectKey
    ? t("workerEmpty.onProject", { project: projectKey })
    : ""

  /* One sentence per case, and they have to stay four different sentences:
     every one of these pools is empty and three of the four are correct. The
     figures are values in their own voice and the words are prose in theirs,
     so the emphasis rides slot elements and the plural form follows the case's
     own count through `Trans`. The counts stringify: a Trans slot holding a
     falsy child (a bare `0`) renders empty, and zero is a reading, not a
     blank. */
  const figure = (value: number) => (
    <span className={styles.figure}>{String(value)}</span>
  )
  const description =
    kind === "filtered" ? (
      <Trans
        ns="queue"
        i18nKey="workerEmpty.filteredDescription"
        count={poolSize}
        values={{ where }}
        components={{ pool: figure(poolSize) }}
      />
    ) : kind === "backlog" ? (
      <Trans
        ns="queue"
        i18nKey="workerEmpty.backlogDescription"
        count={backlog}
        values={{ where }}
        components={{ zero: figure(0), backlog: figure(backlog) }}
      />
    ) : kind === "at-rest" ? (
      <Trans
        ns="queue"
        i18nKey="workerEmpty.atRestDescription"
        values={{ where }}
        components={{ zero: figure(0) }}
      />
    ) : (
      <Trans
        ns="queue"
        i18nKey="workerEmpty.underTargetDescription"
        count={backlog}
        values={{ where }}
        components={{ minIdle: figure(minIdle), backlog: figure(backlog) }}
      />
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
      title={<span data-kind={kind}>{t(TITLE_KEYS[kind])}</span>}
      description={description}
      inset="gutter"
      data-test="worker-empty"
      action={
        kind === "filtered" && onClearFilters ? (
          <Tooltip content={t("workerEmpty.clearFilters")}>
            <Button
              size="icon-sm"
              variant="outline"
              data-test="worker-empty-clear"
              aria-label={t("workerEmpty.clearFilters")}
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
