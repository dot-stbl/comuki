import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { ChevronsDownUp, ChevronsUpDown, RotateCw } from "lucide-react"
import type { PanelImperativeHandle, PanelSize } from "react-resizable-panels"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useSession } from "@/shared/session"
import {
  Button,
  SplitPane,
  SplitPanel,
  SplitSeparator,
  Tooltip,
} from "@/shared/ui"

import { useQueueQuery } from "@/domains/queue/api/queries"
import {
  AGE_STALLED_SEC,
  backlogOf,
  lostLeases,
  unclaimedOver,
  workerCounts,
} from "@/domains/queue/model/queue"
import { PoolStrip } from "@/domains/queue/ui/pool-strip"
import { QueuePanel } from "@/domains/queue/ui/queue-panel"
import { WorkersPanel } from "@/domains/queue/ui/workers-panel"

import styles from "./queue-page.module.css"

const SKELETON_WIDTHS = ["38%", "72%", "55%", "84%", "46%", "63%"]

/**
 * The collapsed pool's height, in pixels because that is what the panel
 * measures in. Roughly one row: enough for the pool's shape, not enough for a
 * number. Mirrors `--h-strip`, which is what any stylesheet needing the same
 * depth reads.
 */
const STRIP_HEIGHT = 34

/** The tab stays open all day, so the divider and the collapse survive reloads. */
const POOL_LAYOUT_KEY = "comuki.queue.pool"

export interface QueuePageProps {
  /**
   * The queue half's promoted search filter, held in the URL as `?q=`.
   *
   * Controlled-or-not, the way the kit's own table takes `columnVisibility`.
   * The route hands both halves over; a test that hands neither gets a screen
   * that keeps the values itself and behaves exactly as it did before.
   */
  search?: string
  onSearchChange?: (next: string) => void
  /** The pool half's own, held as `?w=`. Two halves, two parameters. */
  workerSearch?: string
  onWorkerSearchChange?: (next: string) => void
}

/**
 * Queue and workers — one mechanism seen from both ends.
 *
 * The orchestrator puts work items out for claim; a free worker claims one by
 * profile, takes a lease and heartbeats. The failure that produces is a pair:
 * an item nobody claims, and a worker holding a lease it stopped defending.
 * Neither half can be read without the other — "queued eleven minutes" is only
 * a fault once you can see there was an idle worker to take it — so they share
 * a screen and a draggable rule, and the pool collapses to a strip on the days
 * it is not the question.
 */
export function QueuePage({
  search,
  onSearchChange,
  workerSearch,
  onWorkerSearchChange,
}: QueuePageProps = {}) {
  const { data, isLoading, isError, error, refetch } = useQueueQuery()
  const session = useSession()

  const pool = useRef<PanelImperativeHandle | null>(null)
  const [poolCollapsed, setPoolCollapsed] = useState(false)

  const items = useMemo(() => data?.items ?? [], [data])
  const workers = useMemo(() => data?.workers ?? [], [data])
  const pools = useMemo(() => data?.pools ?? [], [data])

  const counts = useMemo(() => workerCounts(workers), [workers])
  const queued = useMemo(() => backlogOf(items), [items])
  const stalled = useMemo(
    () => unclaimedOver(items, AGE_STALLED_SEC),
    [items]
  )
  const lost = useMemo(() => lostLeases(workers), [workers])

  const onPoolResize = useCallback((size: PanelSize) => {
    setPoolCollapsed(size.inPixels <= STRIP_HEIGHT + 1)
  }, [])

  /* Arriving with the pool narrowed opens the pool. The collapse is
     remembered per pane group, so an operator who left the strip closed
     yesterday would otherwise follow a link to `wk_e34d` and land on a screen
     where the answer is real, applied, and behind a shut panel. Only the
     arrival opens it — the toggle still wins for the rest of the visit. */
  useEffect(() => {
    if (workerSearch) {
      pool.current?.expand()
    }
  }, [workerSearch])

  const expandPool = useCallback(() => {
    pool.current?.expand()
  }, [])

  const togglePool = useCallback(() => {
    const panel = pool.current
    if (!panel) {
      return
    }
    if (panel.isCollapsed()) {
      panel.expand()
    } else {
      panel.collapse()
    }
  }, [])

  const ready = !isLoading && !isError

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[{ label: "observe", to: "/runs" }, { label: "queue" }]}
          title="Queue & workers"
          summary={
            <>
              <span className={styles.strong}>{queued}</span> queued ·{" "}
              <span className={stalled > 0 ? styles.warn : styles.strong}>
                {stalled}
              </span>{" "}
              unclaimed over five minutes ·{" "}
              <span className={styles.strong}>{counts.total}</span> workers,{" "}
              <span className={styles.strong}>{counts.idle}</span> idle ·{" "}
              <span className={lost > 0 ? styles.warn : styles.strong}>
                {lost}
              </span>{" "}
              without a heartbeat
            </>
          }
          actions={
            ready ? (
              <Tooltip
                content={poolCollapsed ? "Expand pool" : "Collapse pool"}
              >
                <Button
                  variant="ghost"
                  size="icon-sm"
                  data-test="pool-toggle"
                  aria-controls="pool"
                  aria-expanded={!poolCollapsed}
                  aria-label={poolCollapsed ? "Expand pool" : "Collapse pool"}
                  onClick={togglePool}
                >
                  {poolCollapsed ? (
                    <ChevronsUpDown aria-hidden="true" />
                  ) : (
                    <ChevronsDownUp aria-hidden="true" />
                  )}
                </Button>
              </Tooltip>
            ) : null
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="queue-loading">
            {SKELETON_WIDTHS.map((width, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ width }}
              />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>Couldn&apos;t load the queue</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="queue-retry"
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
          <SplitPane
            orientation="vertical"
            storageKey={POOL_LAYOUT_KEY}
            className={styles.split}
          >
            <SplitPanel
              id="queue"
              className={styles.queuePanel}
              defaultSize="58%"
              minSize="20%"
            >
              <QueuePanel
                items={items}
                projects={session.projects}
                search={search}
                onSearchChange={onSearchChange}
              />
            </SplitPanel>

            <SplitSeparator
              orientation="vertical"
              aria-label="Resize the worker pool"
            />

            <SplitPanel
              id="pool"
              className={styles.poolPanel}
              panelRef={pool}
              minSize="18%"
              collapsible
              collapsedSize={STRIP_HEIGHT}
              onResize={onPoolResize}
            >
              {poolCollapsed ? (
                <PoolStrip counts={counts} onExpand={expandPool} />
              ) : (
                <WorkersPanel
                  workers={workers}
                  items={items}
                  pools={pools}
                  projects={session.projects}
                  search={workerSearch}
                  onSearchChange={onWorkerSearchChange}
                />
              )}
            </SplitPanel>
          </SplitPane>
        ) : null}
      </div>
    </AppShell>
  )
}
