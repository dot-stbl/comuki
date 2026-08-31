import { useMemo, useState } from "react"
import { RotateCw } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useKnowledgeQuery } from "@/domains/knowledge/api/queries"
import { filterKnowledgeEntries } from "@/domains/knowledge/model/filter-knowledge"
import { EvalHarnessTable } from "@/domains/knowledge/ui/eval-harness-table"
import { KnowledgeDetailSheet } from "@/domains/knowledge/ui/knowledge-detail-sheet"
import { KnowledgeEntryRow } from "@/domains/knowledge/ui/knowledge-entry-row"
import { KnowledgeSearch } from "@/domains/knowledge/ui/knowledge-search"
import { Button, Section, Tooltip } from "@/shared/ui"

import styles from "./knowledge-page.module.css"

const SKELETON_WIDTHS = ["58%", "82%", "44%", "70%", "52%"]

export interface KnowledgePageProps {
  /** A query to narrow the rule set to on arrival — see `focus` below. */
  focus?: string
}

/**
 * What the swarm has been told, and what telling it that did.
 *
 * The rule set is the product's real lever: a worker is not instructed by a
 * prompt somebody typed, it is instructed by this list plus a pinned SDK. So
 * the screen answers three questions in the order they get asked — which
 * revision is in force, what is in it, and what the last edit did to the golden
 * tasks — and it is read top to bottom rather than filled like a board, which
 * is why nothing here claims a share of the viewport.
 *
 * Everything on it is read-only, and that is a fact about the product rather
 * than a gap: rules live in the client's git and change by commit. There is no
 * act to gate; the route's `knowledge.view` is the whole permission story.
 *
 * `focus` is the query the address bar arrived with. It seeds the search field
 * once and is then the operator's, so a link into a narrowed rule set says why
 * the list is short and clears in one gesture — the same contract the projects
 * registry and the three identity lists keep.
 */
export function KnowledgePage({ focus }: KnowledgePageProps) {
  const { data, isLoading, isError, error, refetch } = useKnowledgeQuery()

  // Seeded once, then owned by the field: the filter is the operator's from the
  // moment they land, and clearing it is the ordinary control it always is.
  const [query, setQuery] = useState(() => focus ?? "")
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const entries = useMemo(() => data?.entries ?? [], [data?.entries])
  const shown = useMemo(
    () => filterKnowledgeEntries(entries, query),
    [entries, query]
  )
  const selected = entries.find((entry) => entry.id === selectedId) ?? null

  const ready = !isLoading && !isError && data !== undefined

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[
            { label: "configure", to: "/settings" },
            { label: "knowledge" },
          ]}
          title="Knowledge"
          summary="rule set, revisions, eval harness"
          /* The search rides in the header's own band rather than in the
             scroll port, so the control that narrows the list cannot scroll
             away from the list it narrows — the same contract a table screen's
             toolbar keeps. */
          filters={
            ready ? (
              <KnowledgeSearch value={query} onValueChange={setQuery} />
            ) : undefined
          }
        />
      }
    >
      <div className={styles.screen}>
        {isLoading ? (
          <div className={styles.skeleton} data-test="knowledge-loading">
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
            <p className={styles.stateTitle}>Knowledge did not load</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="knowledge-retry"
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
            {/* Three readings about the revision in force. Not cards: hairline
                data surfaces carrying the corner their size deserves, with the
                figure as the reading and the label naming it. */}
            <Section
              variant="region"
              id="knowledge-revision"
              title="revision in force"
              data-test="knowledge-revision"
            >
              <div className={styles.readings}>
                <div className={styles.reading}>
                  <span className={styles.readingLabel}>current revision</span>
                  <span className={styles.readingFigure}>
                    {data.revision.rules}
                  </span>
                  <span className={styles.readingNote}>
                    {data.revision.sdk} · updated {data.revision.updated}
                  </span>
                </div>

                <div className={styles.reading}>
                  <span className={styles.readingLabel}>active rules</span>
                  <span className={styles.readingFigure}>
                    {data.rulesActive}
                  </span>
                  <span className={styles.readingNote}>
                    {data.rulesHard} hard · {data.rulesSoft} soft
                  </span>
                </div>

                <div className={styles.reading}>
                  <span className={styles.readingLabel}>reproducibility</span>
                  <span className={styles.readingFigure}>
                    100
                    <span className={styles.readingUnit}>%</span>
                  </span>
                  <span className={styles.readingNote}>
                    every run pins the rule set + SDK
                  </span>
                </div>
              </div>
            </Section>

            <Section
              variant="region"
              id="knowledge-entries"
              title="rules, docs and skills"
              note={`${shown.length} of ${entries.length}`}
              data-test="knowledge-entries"
            >
              {shown.length === 0 ? (
                <div className={styles.empty} data-test="knowledge-empty">
                  <p className={styles.emptyTitle}>No matches</p>
                  <p className={styles.emptyBody}>
                    Try another query over pinned rules and docs.
                  </p>
                </div>
              ) : (
                <div className={styles.entries}>
                  {shown.map((entry) => (
                    <KnowledgeEntryRow
                      key={entry.id}
                      entry={entry}
                      selected={entry.id === selectedId}
                      onSelect={setSelectedId}
                    />
                  ))}
                </div>
              )}
            </Section>

            <Section
              variant="screen"
              data-test="knowledge-eval"
              title="golden tasks"
              note="before → after on rule edits"
            >
              <EvalHarnessTable cases={data.eval} />
            </Section>
          </>
        ) : null}
      </div>

      <KnowledgeDetailSheet
        entry={selected}
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) {
            setSelectedId(null)
          }
        }}
      />
    </AppShell>
  )
}
