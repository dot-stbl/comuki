import { Pin } from "lucide-react"

import type { KnowledgeEntry } from "@/domains/knowledge/model/types"
import { cn } from "@/shared/lib/utils"
import { Badge } from "@/shared/ui/badge"

export interface KnowledgeEntryRowProps {
  entry: KnowledgeEntry
  selected: boolean
  onSelect: (id: string) => void
}

export function KnowledgeEntryRow({
  entry,
  selected,
  onSelect,
}: KnowledgeEntryRowProps) {
  return (
    <button
      type="button"
      onClick={() => onSelect(entry.id)}
      className={cn(
        "flex w-full flex-col gap-2 rounded-lg border border-border bg-card px-3 py-2.5 text-left transition-colors hover:bg-muted/40",
        selected && "border-primary/50 bg-muted/50 ring-1 ring-primary/30"
      )}
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-mono text-xs font-medium text-foreground">
          {entry.title}
        </span>
        <Badge variant="outline">{entry.kind}</Badge>
        {entry.ruleKind ? (
          <Badge variant={entry.ruleKind === "hard" ? "secondary" : "outline"}>
            {entry.ruleKind}
          </Badge>
        ) : null}
        {entry.pinned ? (
          <span className="inline-flex items-center gap-1 font-mono text-[10px] uppercase tracking-wider text-primary">
            <Pin className="size-3" />
            pinned
          </span>
        ) : null}
        <span className="ml-auto font-mono text-[10px] text-muted-foreground">
          @{entry.revision}
        </span>
      </div>
      <p className="text-xs text-muted-foreground">{entry.summary}</p>
      <div className="flex items-center gap-2 font-mono text-[10px] text-muted-foreground">
        <span>{entry.scope}</span>
        <span>·</span>
        <span>updated {entry.updated}</span>
      </div>
    </button>
  )
}
