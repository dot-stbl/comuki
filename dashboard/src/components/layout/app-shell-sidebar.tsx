import type { LucideIcon } from "lucide-react"
import { Link } from "@tanstack/react-router"

export interface SidebarNavItem {
  label: string
  href: string
  icon?: LucideIcon
}

export interface AppShellSidebarProps {
  items: SidebarNavItem[]
}

export function AppShellSidebar({ items }: AppShellSidebarProps) {
  return (
    <aside className="flex w-56 shrink-0 flex-col gap-1 overflow-y-auto border-r border-border bg-sidebar p-3">
      <div className="px-2 py-1 font-mono text-xs uppercase tracking-wider text-muted-foreground">
        Sections
      </div>
      {items.map((item) => (
        <Link
          key={item.href}
          to={item.href}
          className="flex items-center gap-2 rounded-sm px-2 py-1.5 font-mono text-sm text-sidebar-foreground/70 transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
          activeProps={{ className: "bg-sidebar-accent font-medium text-sidebar-accent-foreground" }}
        >
          {item.icon ? <item.icon className="size-3.5 shrink-0" /> : null}
          {item.label}
        </Link>
      ))}
    </aside>
  )
}