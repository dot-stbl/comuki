import { useCallback, useMemo, useState } from "react"
import { getRouteApi } from "@tanstack/react-router"
import { Cpu, DollarSign, GitBranch, Hash, RotateCw, Timer } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { getWorkItemInspector, useRunQuery } from "@/domains/runs/api/queries"
import {
  briefSegments,
  formatCost,
  formatDuration,
  formatTokens,
} from "@/domains/runs/model/format"
import {
  currentItem,
  orderedItems,
  planGraph,
} from "@/domains/runs/model/work-items"
import { RunGraph } from "@/domains/runs/ui/run-graph"
import { WorkItemInspectorPanel } from "@/domains/runs/ui/work-item-inspector"
import { projectOf, useSession } from "@/shared/session"
import {
  Button,
  SplitPane,
  SplitPanel,
  SplitSeparator,
  StatusBadge,
  Tooltip,
} from "@/shared/ui"

import styles from "./run-detail-page.module.css"

const runDetailRoute = getRouteApi("/runs/$runId")

/** The tab stays open as long as the run does, so the divider survives a reload. */
const DETAIL_LAYOUT_KEY = "comuki.run.detail"

export function RunDetailPage() {
  const { runId } = runDetailRoute.useParams()
  const { data, isLoading, isError, error, refetch } = useRunQuery(runId)
  const session = useSession()
  const [picked, setPicked] = useState<string | null>(null)

  // The plan is a graph, so "the items in order" is the dependency order the
  // model derives — not the order they happen to sit in the payload.
  const items = useMemo(
    () => (data ? orderedItems(data.workItems) : []),
    [data]
  )

  // Derived rather than stored: navigating to another run leaves `picked`
  // pointing at an item this run has never heard of, and a selection that has
  // to be reset by an effect is a selection that is briefly wrong.
  const selectedItem =
    items.find((entry) => entry.id === picked) ??
    (data ? currentItem(data) : undefined) ??
    items[0]
  const selectedId = selectedItem?.id ?? ""

  const inspector = useMemo(() => {
    if (!data || !selectedId) {
      return null
    }
    return getWorkItemInspector(data.id, selectedId)
  }, [data, selectedId])

  // The same reading the graph draws with, so the node's "depends on 2 earlier"
  // and the inspector's list can never disagree about what is far away.
  const waitsOn = useMemo(() => {
    if (!selectedId) {
      return []
    }
    return planGraph(items).dependencies.get(selectedId) ?? []
  }, [items, selectedId])

  const brief = useMemo(() => briefSegments(data?.brief ?? ""), [data])

  const onSelect = useCallback((itemId: string) => {
    setPicked(itemId)
  }, [])

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[
            { label: "live runs", to: "/runs" },
            { label: `run_${runId}` },
          ]}
          title={data?.title ?? `Run ${runId}`}
          // Which project, then which app. Arrived at from a list that mixes
          // projects, so the page says whose run this is before it says
          // anything else about it.
          summary={
            data
              ? `${projectOf(session, data.projectId)?.key ?? "—"} · ${data.app}`
              : "…"
          }
          actions={data ? <StatusBadge status={data.status} /> : null}
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="run-loading">
            <span className={styles.skeletonBar} />
            <span className={styles.skeletonBoard} />
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>Couldn&apos;t load this run</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <Tooltip content="Retry">
              <Button
                size="icon-sm"
                data-test="run-retry"
                aria-label="Retry"
                onClick={() => {
                  void refetch()
                }}
              >
                <RotateCw aria-hidden="true" />
              </Button>
            </Tooltip>
          </div>
        ) : null}

        {data ? (
          <div className={styles.context}>
            <div className={styles.facts}>
              <span className={styles.fact}>
                <Hash className={styles.factIcon} aria-hidden="true" />
                <span className={styles.runId}>{data.id}</span>
              </span>
              <span className={styles.fact}>
                <Cpu className={styles.factIcon} aria-hidden="true" />
                <span className={styles.factValue}>{data.model}</span>
              </span>
              <span className={styles.fact}>
                <Timer className={styles.factIcon} aria-hidden="true" />
                <span className={styles.factValue}>
                  {formatDuration(data.durationSec)}
                </span>
              </span>
              <span className={styles.fact}>
                <DollarSign className={styles.factIcon} aria-hidden="true" />
                <span className={styles.factValue}>
                  {formatCost(data.cost)} · {formatTokens(data.tokens)} tok
                </span>
              </span>
              <span className={styles.fact}>
                <GitBranch className={styles.factIcon} aria-hidden="true" />
                <span className={styles.factValue}>
                  {data.revision.rules} · {data.revision.sdk}
                </span>
              </span>
            </div>

            {/* The ticket, in the words it arrived in. Rendered as elements
                rather than injected as markup: the values it quotes are spans,
                not a string handed to innerHTML. */}
            <p className={styles.brief}>
              {brief.map((segment, index) =>
                segment.code ? (
                  <code key={index} className={styles.briefCode}>
                    {segment.text}
                  </code>
                ) : (
                  <span key={index}>{segment.text}</span>
                )
              )}
            </p>
          </div>
        ) : null}

        {data && items.length === 0 ? (
          // The duty board says "No plans yet" for a whole list of tickets the
          // brain has not decomposed. Here it is one run, and a person came to
          // this URL to look at its plan — so the answer names what is missing,
          // says whose move it is, and says what will appear in its place.
          <div className={styles.state} data-test="run-unplanned">
            <p className={styles.stateTitle}>Not planned yet</p>
            <p className={styles.stateBody}>
              This run is accepted and the brain has not decomposed it into work
              items. Nothing is wrong and nothing is waiting on you — the graph
              and the item inspector appear here as soon as the plan has its
              first item.
            </p>
            <p className={styles.stateHint}>
              {data.status} · no work items · nothing to inspect
            </p>
          </div>
        ) : null}

        {data && items.length > 0 && selectedItem ? (
          <SplitPane
            orientation="vertical"
            storageKey={DETAIL_LAYOUT_KEY}
            className={styles.split}
          >
            <SplitPanel
              id="graph"
              className={styles.graphPanel}
              /* 55% is measured, not picked: on a 900px laptop it leaves the
                 graph ~356px, which is what a three-wide branch needs before
                 the column starts scrolling itself, and the common branch in
                 a plan is two or three. The minimum still clears one full
                 node so the panel can never be dragged into a sliver. */
              defaultSize="55%"
              minSize="25%"
            >
              <RunGraph
                items={items}
                current={data.current}
                selected={selectedId}
                onSelect={onSelect}
              />
            </SplitPanel>

            <SplitSeparator
              orientation="vertical"
              aria-label="Resize the run graph"
            />

            <SplitPanel
              id="inspector"
              className={styles.inspectorPanel}
              minSize="25%"
            >
              {/* The graph never waits on the inspector payload. An item whose
                  detail is unavailable leaves the graph fully usable and says
                  so here, rather than blanking the half of the screen the
                  plan lives on. */}
              {inspector ? (
                <WorkItemInspectorPanel
                  item={selectedItem}
                  index={items.indexOf(selectedItem) + 1}
                  total={items.length}
                  info={inspector}
                  waitsOn={waitsOn}
                  onSelect={onSelect}
                />
              ) : (
                <div className={styles.state}>
                  <p className={styles.stateTitle}>No detail for this item</p>
                  <p className={styles.stateBody}>
                    The plan above is complete; this item has no inspector
                    record yet. Pick another item, or come back once it starts.
                  </p>
                </div>
              )}
            </SplitPanel>
          </SplitPane>
        ) : null}
      </div>
    </AppShell>
  )
}
