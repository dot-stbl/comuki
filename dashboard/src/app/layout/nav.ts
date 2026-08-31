import {
  Activity,
  BookOpen,
  CheckCircle2,
  Coins,
  FolderGit2,
  ListOrdered,
  ListTodo,
  MessageSquare,
  PlayCircle,
  Plug,
  Route as RouteIcon,
  Server,
  Settings,
  ShieldCheck,
  Users,
} from "lucide-react"

import type { SidebarNavGroup } from "@/app/layout/app-shell-sidebar"
import { can, type Session } from "@/shared/session"

/**
 * The rail, in two tiers.
 *
 * The upper tier is the duty engineer's day — intake, watching, configuring the
 * swarm's behaviour. The lower one is the platform underneath it: who exists,
 * what machines and models it may spend, where the boards are. They are
 * separated because they are visited on different clocks — the top every few
 * minutes, the bottom every few weeks — and a rail that mixes them makes the
 * frequent things harder to hit without making the rare ones easier to find.
 *
 * Every item names the act that opens it. Keying the rail on acts rather than
 * on roles is what lets one permission gate the item, the route and the buttons
 * inside it without any of the three agreeing on a role list first — see
 * `shared/session/permissions.ts`.
 */
export const productNav: SidebarNavGroup[] = [
  {
    label: "Intake",
    items: [
      // Above Inbox on purpose: Intake is where work enters the swarm, and
      // there are exactly two ways in — a ticket landing in the inbox, and a
      // person typing `/run`. `exact: false` so the wizard keeps it active.
      {
        label: "Chat",
        href: "/chat",
        icon: MessageSquare,
        exact: false,
        permission: "chat.use",
      },
      { label: "Inbox", href: "/tasks", icon: ListTodo, permission: "inbox.view" },
    ],
  },
  {
    label: "Observe",
    items: [
      // Attention has no rail item: the Comuki mark is the home link and has
      // been since the topbar was built, so a second door to the same screen
      // was costing a row in a rail that had grown to sixteen.
      {
        label: "Live runs",
        href: "/runs",
        icon: PlayCircle,
        exact: false,
        badge: "running",
        permission: "runs.view",
      },
      {
        label: "Queue",
        href: "/queue",
        icon: ListOrdered,
        permission: "queue.view",
      },
      {
        label: "Approvals",
        href: "/approvals",
        icon: CheckCircle2,
        badge: "needsHuman",
        permission: "plans.approve",
      },
      { label: "Cost", href: "/cost", icon: Coins, permission: "cost.view" },
    ],
  },
  {
    label: "Configure",
    items: [
      {
        label: "Sources",
        href: "/sources",
        icon: Plug,
        permission: "sources.view",
      },
      {
        label: "Knowledge",
        href: "/knowledge",
        icon: BookOpen,
        permission: "knowledge.view",
      },
      {
        label: "Verify",
        href: "/verify",
        icon: ShieldCheck,
        permission: "verify.view",
      },
      {
        label: "Settings",
        href: "/settings",
        icon: Settings,
        permission: "settings.live",
      },
    ],
  },
  {
    label: "Platform",
    tier: "platform",
    items: [
      {
        label: "Projects",
        href: "/projects",
        icon: FolderGit2,
        permission: "projects.view",
      },
      {
        label: "Identity",
        href: "/identity",
        icon: Users,
        permission: "identity.manage",
      },
      {
        label: "Compute",
        href: "/compute",
        icon: Server,
        permission: "compute.view",
      },
      {
        label: "Models",
        href: "/models",
        icon: RouteIcon,
        permission: "models.view",
      },
      {
        label: "Observability",
        href: "/observability",
        icon: Activity,
        permission: "observability.view",
      },
    ],
  },
]

/**
 * The rail, as this session may actually use it.
 *
 * The first access rule: navigation a role cannot use is *hidden*, not disabled
 * — a greyed rail teaches an operator that the product is broken, while a
 * shorter rail teaches them the shape of their own access. The second half
 * matters as much: a group whose items all vanished is dropped too, because a
 * heading standing over nothing is a more confusing artefact than the missing
 * item was.
 *
 * Project permissions are asked here *without* a project, which is the right
 * question for a rail: "may this person do it somewhere?" Hiding Approvals from
 * someone who approves on one project out of three would be a lie of omission.
 *
 * A plain function rather than a hook so the sidebar, a test and — the day
 * there is one — a route loader can all ask the same question.
 */
export function visibleNav(
  groups: SidebarNavGroup[],
  session: Session
): SidebarNavGroup[] {
  return groups
    .map((group) => ({
      ...group,
      items: group.items.filter(
        (item) => !item.permission || can(session, item.permission)
      ),
    }))
    .filter((group) => group.items.length > 0)
}
