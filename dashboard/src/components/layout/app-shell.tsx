import type { ReactNode } from "react"

import { AppShellSidebar, type SidebarNavItem } from "@/components/layout/app-shell-sidebar"
import { AppShellTopbar } from "@/components/layout/app-shell-topbar"

export interface AppShellProps {
  navItems: SidebarNavItem[]
  children: ReactNode
}

export function AppShell({ navItems, children }: AppShellProps) {
  return (
    <div className="flex h-screen flex-col bg-background text-foreground">
      <AppShellTopbar />
      <div className="flex flex-1 overflow-hidden">
        <AppShellSidebar items={navItems} />
        <main className="flex-1 overflow-auto bg-background p-6">
          {children}
        </main>
      </div>
    </div>
  )
}