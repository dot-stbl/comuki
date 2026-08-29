import type { ReactNode } from "react"

import { AppShellSidebar } from "@/app/layout/app-shell-sidebar"
import { AppShellTopbar } from "@/app/layout/app-shell-topbar"
import { productNav } from "@/app/layout/nav"

export interface AppShellProps {
  children: ReactNode
}

export function AppShell({ children }: AppShellProps) {
  return (
    <div className="flex h-screen flex-col bg-background text-foreground">
      <AppShellTopbar />
      <div className="flex flex-1 overflow-hidden">
        <AppShellSidebar groups={productNav} />
        <main className="flex-1 overflow-auto bg-background p-4">{children}</main>
      </div>
    </div>
  )
}
