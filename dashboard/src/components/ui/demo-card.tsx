import type { ReactNode } from "react"
import { cn } from "@/lib/utils"

export interface DemoCardProps {
  /** Small uppercase label shown in the card header (e.g. "VARIANT", "BUTTON", "DEFAULT") */
  label: string
  children: ReactNode
  className?: string
  /** Optional: align children to the start of the card (for tall content like forms, dialogs) */
  alignStart?: boolean
}

export function DemoCard({ label, children, className, alignStart }: DemoCardProps) {
  return (
    <div className={cn("overflow-visible rounded-md border border-border bg-card", className)}>
      <div className="flex items-center gap-2 border-b border-border px-2.5 py-1.5 font-mono text-xs uppercase tracking-widest text-muted-foreground">
        {label}
      </div>
      <div
        className={cn(
          "flex flex-wrap items-center gap-3 p-2.5",
          alignStart && "items-start",
        )}
      >
        {children}
      </div>
    </div>
  )
}