import type { ReactNode } from "react"

import { StatusBadge } from "./status-badge"

export interface DomainStubProps {
  title: string
  description?: string
  children?: ReactNode
}

/** Shared placeholder for domain pages until W1–W3 land. */
export function DomainStub({
  title,
  description = "mock-first · W1–W3",
  children,
}: DomainStubProps) {
  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-4 py-8">
      <div className="flex items-center gap-3">
        <h1 className="font-mono text-2xl font-semibold tracking-tight text-foreground">
          {title}
        </h1>
        <StatusBadge status="queued">stub</StatusBadge>
      </div>
      <p className="font-mono text-sm text-muted-foreground">{description}</p>
      {children}
    </div>
  )
}
