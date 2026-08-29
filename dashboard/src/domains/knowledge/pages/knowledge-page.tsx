import { useMemo, useState } from "react"
import { BookOpen, Search } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { useKnowledgeQuery } from "@/domains/knowledge/api/queries"
import { filterKnowledgeEntries } from "@/domains/knowledge/model/filter-knowledge"
import { EvalHarnessTable } from "@/domains/knowledge/ui/eval-harness-table"
import { KnowledgeDetailSheet } from "@/domains/knowledge/ui/knowledge-detail-sheet"
import { KnowledgeEntryRow } from "@/domains/knowledge/ui/knowledge-entry-row"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/shared/ui/card"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/shared/ui/empty"
import { Input } from "@/shared/ui/input"
import { Skeleton } from "@/shared/ui/skeleton"

export function KnowledgePage() {
  const { data, isLoading, isError, error } = useKnowledgeQuery()
  const [query, setQuery] = useState("")
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const entries = useMemo(() => data?.entries ?? [], [data?.entries])
  const shown = useMemo(
    () => filterKnowledgeEntries(entries, query),
    [entries, query]
  )
  const selected = entries.find((entry) => entry.id === selectedId) ?? null

  return (
    <AppShell>
      <div className="flex flex-col gap-4">
        <header className="flex flex-col gap-1">
          <div className="font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
            configure / knowledge
          </div>
          <h1 className="text-lg font-semibold tracking-tight">Knowledge</h1>
          <p className="font-mono text-xs text-muted-foreground">
            rule set, revisions, eval harness
          </p>
        </header>

        {isLoading ? (
          <div className="grid gap-3 sm:grid-cols-3">
            {Array.from({ length: 3 }).map((_, index) => (
              <Skeleton key={index} className="h-24 rounded-lg" />
            ))}
          </div>
        ) : null}

        {isError ? (
          <Empty className="border border-dashed">
            <EmptyHeader>
              <EmptyTitle>Failed to load knowledge</EmptyTitle>
              <EmptyDescription>
                {error instanceof Error ? error.message : "Unknown error"}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : null}

        {data ? (
          <>
            <div className="grid gap-3 sm:grid-cols-3">
              <Card size="sm">
                <CardHeader className="border-b">
                  <CardDescription>Current revision</CardDescription>
                  <CardTitle className="font-mono text-base">
                    {data.revision.rules}
                  </CardTitle>
                </CardHeader>
                <CardContent className="font-mono text-xs text-muted-foreground">
                  {data.revision.sdk} · updated {data.revision.updated}
                </CardContent>
              </Card>
              <Card size="sm">
                <CardHeader className="border-b">
                  <CardDescription>Active rules</CardDescription>
                  <CardTitle className="font-mono text-2xl">
                    {data.rulesActive}
                  </CardTitle>
                </CardHeader>
                <CardContent className="font-mono text-xs text-muted-foreground">
                  {data.rulesHard} hard · {data.rulesSoft} soft
                </CardContent>
              </Card>
              <Card size="sm">
                <CardHeader className="border-b">
                  <CardDescription>Reproducibility</CardDescription>
                  <CardTitle className="font-mono text-2xl">
                    100
                    <span className="text-sm text-muted-foreground">%</span>
                  </CardTitle>
                </CardHeader>
                <CardContent className="font-mono text-xs text-muted-foreground">
                  every run pins the rule set + SDK
                </CardContent>
              </Card>
            </div>

            <label className="relative max-w-xl">
              <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Search rules, docs, skills…"
                className="pl-7"
              />
            </label>

            {shown.length === 0 ? (
              <Empty className="border border-dashed">
                <EmptyHeader>
                  <EmptyMedia variant="icon">
                    <BookOpen />
                  </EmptyMedia>
                  <EmptyTitle>No matches</EmptyTitle>
                  <EmptyDescription>
                    Try another query over pinned rules and docs.
                  </EmptyDescription>
                </EmptyHeader>
              </Empty>
            ) : (
              <div className="flex flex-col gap-2">
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

            <Card>
              <CardHeader className="border-b">
                <CardTitle>EvalHarness · golden tasks</CardTitle>
                <CardDescription>before → after on rule edits</CardDescription>
              </CardHeader>
              <CardContent className="px-0">
                <EvalHarnessTable cases={data.eval} />
              </CardContent>
            </Card>
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
