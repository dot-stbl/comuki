import { useMemo, useState } from "react"
import { RotateCw } from "lucide-react"
import { Tab, TabList, TabPanel, Tabs } from "react-aria-components"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useKnowledgeQuery } from "@/domains/knowledge/api/queries"
import { filterKnowledgeEntries } from "@/domains/knowledge/model/filter-knowledge"
import { isKnowledgeTab, type KnowledgeTab } from "@/domains/knowledge/model/tabs"
import { EvalHarnessTable } from "@/domains/knowledge/ui/eval-harness-table"
import { GateSummary } from "@/domains/knowledge/ui/gate-summary"
import { GateTab } from "@/domains/knowledge/ui/gate-tab"
import { KnowledgeDetailSheet } from "@/domains/knowledge/ui/knowledge-detail-sheet"
import { KnowledgeEntryRow } from "@/domains/knowledge/ui/knowledge-entry-row"
import { KnowledgeSearch } from "@/domains/knowledge/ui/knowledge-search"
import { can, useSession } from "@/shared/session"
import { Button, Section, Tooltip } from "@/shared/ui"

import styles from "./knowledge-page.module.css"

const SKELETON_WIDTHS = ["58%", "82%", "44%", "70%", "52%"]

export interface KnowledgePageProps {
  /** Which section is showing. In the URL, so it can be linked and returned to. */
  tab: KnowledgeTab
  /**
   * A query to narrow the rule set to on arrival — see `focus` below. It is
   * the library's own narrowing, so it is handed to the library alone.
   */
  focus?: string
  onTabChange: (tab: KnowledgeTab) => void
}

/**
 * What the swarm has been told, and what telling it that did.
 *
 * The rule set is the product's real lever: a worker is not instructed by a
 * prompt somebody typed, it is instructed by this list plus a pinned SDK. So
 * the library answers three questions in the order they get asked — which
 * revision is in force, what is in it, and what the last edit did to the
 * golden tasks — and it is read top to bottom rather than filled like a board,
 * which is why nothing here claims a share of the viewport.
 *
 * The second tab is the verification gate, folded in from the screen that used
 * to stand at `/verify`. Same chassis — read-only registries sourced from the
 * client's git, both project-scoped — so it lives behind one door rather than
 * two; a different question, so it gets its own tab rather than a share of the
 * page. `/verify` redirects here, and the gate keeps its own assembly intact
 * under `domains/verify`.
 *
 * Everything on the library is read-only, and that is a fact about the product
 * rather than a gap: rules live in the client's git and change by commit.
 * There is no act to gate; the route's `knowledge.view` is the whole
 * permission story of the door. The gate below it is the one exception, and it
 * carries its own.
 *
 * `focus` is the query the address bar arrived with. It seeds the search field
 * once and is then the operator's, so a link into a narrowed rule set says why
 * the list is short and clears in one gesture — the same contract the projects
 * registry and the three identity lists keep.
 */
export function KnowledgePage({ tab, focus, onTabChange }: KnowledgePageProps) {
  const { data, isLoading, isError, error, refetch } = useKnowledgeQuery()
  const session = useSession()

  // A folded section carries its own permission and hides below the screen's:
  // the route's `knowledge.view` gates the door, and the gate tab — folded in
  // from the screen that used to stand at `/verify` — asks `verify.view` of
  // its own and is hidden, never disabled, when the answer is no. The same
  // rule the boards section on Compute follows.
  const gateVisible = can(session, "verify.view")

  // The address bar may name a section this session cannot have — a member
  // following an old `/verify` link arrives at `?tab=gate`. Hidden beats
  // disabled, and there is nothing to disable on a tab that is not rendered,
  // so the showing section falls back to the library rather than leaving the
  // strip pointing at a panel that does not exist.
  const shown = tab === "gate" && !gateVisible ? "library" : tab

  // Seeded once, then owned by the field: the filter is the operator's from the
  // moment they land, and clearing it is the ordinary control it always is.
  const [query, setQuery] = useState(() => focus ?? "")
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const entries = useMemo(() => data?.entries ?? [], [data?.entries])
  const shownEntries = useMemo(
    () => filterKnowledgeEntries(entries, query),
    [entries, query]
  )
  const selected = entries.find((entry) => entry.id === selectedId) ?? null

  const ready = !isLoading && !isError && data !== undefined

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[
            { label: "configure", to: "/settings" },
            { label: "knowledge" },
          ]}
          title="Knowledge"
          summary={
            shown === "gate" ? (
              <GateSummary />
            ) : (
              "rule set, revisions, eval harness"
            )
          }
          /* The search rides in the header's own band rather than in the
             scroll port, so the control that narrows the list cannot scroll
             away from the list it narrows — the same contract a table screen's
             toolbar keeps. It is the library's control: the gate's tables each
             carry their own toolbar, and one field over two different lists
             would narrow neither visibly. */
          filters={
            ready && shown === "library" ? (
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
          <Tabs
            className={styles.tabs}
            selectedKey={shown}
            onSelectionChange={(key) => {
              if (isKnowledgeTab(key)) {
                onTabChange(key)
              }
            }}
          >
            <TabList aria-label="Knowledge sections" className={styles.tabList}>
              <Tab id="library" className={styles.tab} data-test="tab-library">
                library
              </Tab>
              {gateVisible ? (
                <Tab id="gate" className={styles.tab} data-test="tab-gate">
                  gate
                </Tab>
              ) : null}
            </TabList>

            <TabPanel id="library" className={styles.tabPanel}>
              {/* Three readings about the revision in force. Not cards:
                  hairline data surfaces carrying the corner their size
                  deserves, with the figure as the reading and the label
                  naming it. */}
              <div className={styles.library}>
                <Section
                  variant="region"
                  id="knowledge-revision"
                  title="revision in force"
                  data-test="knowledge-revision"
                >
                  <div className={styles.readings}>
                    <div className={styles.reading}>
                      <span className={styles.readingLabel}>
                        current revision
                      </span>
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
                      <span className={styles.readingLabel}>
                        reproducibility
                      </span>
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
                  note={`${shownEntries.length} of ${entries.length}`}
                  data-test="knowledge-entries"
                >
                  {shownEntries.length === 0 ? (
                    <div className={styles.empty} data-test="knowledge-empty">
                      <p className={styles.emptyTitle}>No matches</p>
                      <p className={styles.emptyBody}>
                        Try another query over pinned rules and docs.
                      </p>
                    </div>
                  ) : (
                    <div className={styles.entries}>
                      {shownEntries.map((entry) => (
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
              </div>
            </TabPanel>

            {gateVisible ? (
              <TabPanel id="gate" className={styles.tabPanel}>
                <GateTab />
              </TabPanel>
            ) : null}
          </Tabs>
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
