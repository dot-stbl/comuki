import { Search } from "lucide-react"

import type { RunStatusFilter } from "@/domains/runs/model/types"
import { Input } from "@/shared/ui/input"
import {
  NativeSelect,
  NativeSelectOption,
} from "@/shared/ui/native-select"

const STATUS_OPTIONS: RunStatusFilter[] = [
  "all",
  "running",
  "waiting",
  "escalated",
  "queued",
  "failed",
  "success",
]

export interface RunsFilterBarProps {
  query: string
  app: string
  status: RunStatusFilter
  apps: string[]
  total: number
  onQueryChange: (value: string) => void
  onAppChange: (value: string) => void
  onStatusChange: (value: RunStatusFilter) => void
}

export function RunsFilterBar({
  query,
  app,
  status,
  apps,
  total,
  onQueryChange,
  onAppChange,
  onStatusChange,
}: RunsFilterBarProps) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <label className="relative min-w-56 flex-1">
        <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Search by run id, title or app…"
          className="pl-7"
        />
      </label>
      <NativeSelect
        value={app}
        onChange={(event) => onAppChange(event.target.value)}
        aria-label="Filter by app"
      >
        <NativeSelectOption value="all">all apps</NativeSelectOption>
        {apps.map((item) => (
          <NativeSelectOption key={item} value={item}>
            {item}
          </NativeSelectOption>
        ))}
      </NativeSelect>
      <NativeSelect
        value={status}
        onChange={(event) =>
          onStatusChange(event.target.value as RunStatusFilter)
        }
        aria-label="Filter by status"
      >
        {STATUS_OPTIONS.map((item) => (
          <NativeSelectOption key={item} value={item}>
            {item === "all" ? "all statuses" : item}
          </NativeSelectOption>
        ))}
      </NativeSelect>
      <span className="font-mono text-xs text-muted-foreground">
        {total} runs
      </span>
    </div>
  )
}
