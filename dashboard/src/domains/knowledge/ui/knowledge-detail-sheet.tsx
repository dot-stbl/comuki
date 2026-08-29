import { Pin } from "lucide-react"

import type { KnowledgeEntry } from "@/domains/knowledge/model/types"
import { Badge } from "@/shared/ui/badge"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/shared/ui/sheet"

export interface KnowledgeDetailSheetProps {
  entry: KnowledgeEntry | null
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function KnowledgeDetailSheet({
  entry,
  open,
  onOpenChange,
}: KnowledgeDetailSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        {entry ? (
          <>
            <SheetHeader>
              <SheetTitle className="font-mono">{entry.title}</SheetTitle>
              <SheetDescription>{entry.summary}</SheetDescription>
            </SheetHeader>
            <div className="flex flex-col gap-4 px-4 pb-4">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="outline">{entry.kind}</Badge>
                {entry.ruleKind ? (
                  <Badge
                    variant={
                      entry.ruleKind === "hard" ? "secondary" : "outline"
                    }
                  >
                    {entry.ruleKind}
                  </Badge>
                ) : null}
                {entry.pinned ? (
                  <span className="inline-flex items-center gap-1 font-mono text-[10px] uppercase tracking-wider text-primary">
                    <Pin className="size-3" />
                    pinned @ {entry.revision}
                  </span>
                ) : (
                  <span className="font-mono text-[10px] text-muted-foreground">
                    revision @{entry.revision}
                  </span>
                )}
              </div>
              <dl className="grid gap-2 font-mono text-xs">
                <div className="flex justify-between gap-3">
                  <dt className="text-muted-foreground">scope</dt>
                  <dd>{entry.scope}</dd>
                </div>
                <div className="flex justify-between gap-3">
                  <dt className="text-muted-foreground">updated</dt>
                  <dd>{entry.updated}</dd>
                </div>
              </dl>
              <p className="text-sm leading-relaxed text-foreground">
                {entry.body}
              </p>
            </div>
          </>
        ) : null}
      </SheetContent>
    </Sheet>
  )
}
