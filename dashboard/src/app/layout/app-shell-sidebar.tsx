import type { LucideIcon } from "lucide-react"
import { Link } from "@tanstack/react-router"

import { SwarmMeter } from "@/app/layout/swarm-meter"

export interface SidebarNavItem {
  label: string
  href: string
  icon?: LucideIcon
  /** When false, child routes (e.g. /runs/$runId) keep the parent link active. */
  exact?: boolean
}

export interface SidebarNavGroup {
  label: string
  items: SidebarNavItem[]
}

export interface AppShellSidebarProps {
  groups: SidebarNavGroup[]
}

export function AppShellSidebar({ groups }: AppShellSidebarProps) {
  return (
    <aside className="flex w-44 shrink-0 flex-col gap-3 overflow-y-auto border-r border-border bg-sidebar p-3">
      {groups.map((group) => (
        <div key={group.label} className="flex flex-col gap-1">
          <div className="px-2 py-1 font-mono text-xs uppercase tracking-wider text-muted-foreground">
            {group.label}
          </div>
          {group.items.map((item) => (
            <Link
              key={item.href}
              to={item.href}
              activeOptions={{ exact: item.exact ?? false }}
              className="flex items-center gap-2 border-l-2 border-transparent rounded-sm px-2 py-1.5 font-mono text-sm text-sidebar-foreground/70 transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
              activeProps={{
                className:
                  "bg-sidebar-accent font-medium text-sidebar-accent-foreground border-l-primary",
              }}
            >
              {item.icon ? <item.icon className="size-3.5 shrink-0" /> : null}
              {item.label}
            </Link>
          ))}
        </div>
      ))}
      <SwarmMeter />
    </aside>
  )
}
