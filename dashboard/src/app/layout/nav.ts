import {
  CheckCircle2,
  Coins,
  LayoutGrid,
  ListTodo,
  PlayCircle,
  BookOpen,
  Settings,
} from "lucide-react"

import type { SidebarNavGroup } from "@/app/layout/app-shell-sidebar"

/** Product IA — Intake / Observe / Configure / Dev */
export const productNav: SidebarNavGroup[] = [
  {
    label: "Intake",
    items: [{ label: "Tasks", href: "/tasks", icon: ListTodo }],
  },
  {
    label: "Observe",
    items: [
      { label: "Live runs", href: "/runs", icon: PlayCircle, exact: false },
      { label: "Approvals", href: "/approvals", icon: CheckCircle2 },
      { label: "Cost", href: "/cost", icon: Coins },
    ],
  },
  {
    label: "Configure",
    items: [
      { label: "Knowledge", href: "/knowledge", icon: BookOpen },
      { label: "Settings", href: "/settings", icon: Settings },
    ],
  },
  {
    label: "Dev",
    items: [{ label: "Components", href: "/components", icon: LayoutGrid }],
  },
]
