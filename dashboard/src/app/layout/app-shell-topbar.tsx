import { Box } from "lucide-react"

import { ModeToggle } from "@/shared/ui/mode-toggle"

export function AppShellTopbar() {
  return (
    <header className="flex h-10 shrink-0 items-center gap-4 border-b border-border bg-sidebar px-4">
      <div className="flex items-center gap-2">
        <Box className="size-4 text-primary" strokeWidth={2} aria-hidden="true" />
        <span className="font-mono text-sm font-semibold tracking-tight text-foreground">
          Comuki
        </span>
      </div>
      <div className="flex-1" />
      <ModeToggle />
    </header>
  )
}