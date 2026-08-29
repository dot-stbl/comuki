import { Search } from "lucide-react"

import type {
  TaskPriorityFilter,
  TaskStatusFilter,
} from "@/domains/tasks/model/types"
import { Input } from "@/shared/ui/input"
import {
  NativeSelect,
  NativeSelectOption,
} from "@/shared/ui/native-select"

export interface TasksFilterBarProps {
  query: string
  app: string
  status: TaskStatusFilter
  priority: TaskPriorityFilter
  apps: string[]
  total: number
  onQueryChange: (value: string) => void
  onAppChange: (value: string) => void
  onStatusChange: (value: TaskStatusFilter) => void
  onPriorityChange: (value: TaskPriorityFilter) => void
}

export function TasksFilterBar({
  query,
  app,
  status,
  priority,
  apps,
  total,
  onQueryChange,
  onAppChange,
  onStatusChange,
  onPriorityChange,
}: TasksFilterBarProps) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <div className="relative min-w-[12rem] flex-1">
        <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Filter title, id, app…"
          className="pl-7"
        />
      </div>
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
          onStatusChange(event.target.value as TaskStatusFilter)
        }
        aria-label="Filter by status"
      >
        <NativeSelectOption value="all">all status</NativeSelectOption>
        <NativeSelectOption value="new">new</NativeSelectOption>
        <NativeSelectOption value="queued">queued</NativeSelectOption>
        <NativeSelectOption value="planning">planning</NativeSelectOption>
      </NativeSelect>
      <NativeSelect
        value={priority}
        onChange={(event) =>
          onPriorityChange(event.target.value as TaskPriorityFilter)
        }
        aria-label="Filter by priority"
      >
        <NativeSelectOption value="all">all priority</NativeSelectOption>
        <NativeSelectOption value="high">high</NativeSelectOption>
        <NativeSelectOption value="normal">normal</NativeSelectOption>
        <NativeSelectOption value="low">low</NativeSelectOption>
      </NativeSelect>
      <span className="ml-auto font-mono text-[10px] uppercase tracking-wider text-muted-foreground">
        {total} shown
      </span>
    </div>
  )
}
