import {
  BookOpen,
  CheckCircle2,
  Coins,
  FolderGit2,
  ListOrdered,
  ListTodo,
  PlayCircle,
  Plug,
  Route as RouteIcon,
  Server,
  Settings,
  Users,
} from "lucide-react"

import type { SidebarNavGroup } from "@/app/layout/app-shell-sidebar"
import { i18n } from "@/shared/i18n"
import { can, type Session } from "@/shared/session"

/**
 * An entry's word in the active language, for the non-React consumers of the
 * rail (the search resolver reads the same vocabulary). React consumers take
 * `useTranslation("shell")` so a switch re-renders them; this is the same
 * question answered through the shared instance (`dashboard-i18n` D7). An
 * entry without a `labelKey` — a hand-built rail in a test — keeps its label.
 */
export function navWord(entry: { label: string; labelKey?: string }): string {
  return entry.labelKey ? i18n.t(`shell:${entry.labelKey}`) : entry.label
}

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
/* `labelKey` addresses the word in the `shell` namespace (`dashboard-i18n`
   D2/D7); the English `label` beside it stays as the entry's stable identity
   and as the fallback a hand-built rail (tests) relies on. */
export const productNav: SidebarNavGroup[] = [
  {
    label: "Intake",
    labelKey: "nav.intake",
    items: [
      // The console is not a section: its one door in the chrome is the
      // floating trigger over the board (see `domains/chat`, the dock), so a
      // conversation is something you have, not somewhere you go. The `/chat`
      // route remains for a URL somebody pastes, and nothing here links it.
      {
        label: "Inbox",
        labelKey: "nav.inbox",
        href: "/tasks",
        icon: ListTodo,
        permission: "inbox.view",
      },
    ],
  },
  {
    label: "Observe",
    labelKey: "nav.observe",
    items: [
      // Attention has no rail item: the Comuki mark is the home link and has
      // been since the topbar was built, so a second door to the same screen
      // was costing a row in a rail that had grown to sixteen.
      {
        label: "Live runs",
        labelKey: "nav.liveRuns",
        href: "/runs",
        icon: PlayCircle,
        exact: false,
        badge: "running",
        permission: "runs.view",
      },
      {
        label: "Queue",
        labelKey: "nav.queue",
        href: "/queue",
        icon: ListOrdered,
        permission: "queue.view",
      },
      {
        label: "Approvals",
        labelKey: "nav.approvals",
        href: "/approvals",
        icon: CheckCircle2,
        badge: "needsHuman",
        permission: "plans.approve",
      },
      {
        label: "Cost",
        labelKey: "nav.cost",
        href: "/cost",
        icon: Coins,
        permission: "cost.view",
      },
    ],
  },
  {
    label: "Configure",
    labelKey: "nav.configure",
    items: [
      {
        label: "Sources",
        labelKey: "nav.sources",
        href: "/sources",
        icon: Plug,
        permission: "sources.view",
      },
      // Verify has no row of its own: it is the same chassis as Knowledge —
      // read-only registries sourced from the client's git — and lives as the
      // gate tab there.
      {
        label: "Knowledge",
        labelKey: "nav.knowledge",
        href: "/knowledge",
        icon: BookOpen,
        exact: false,
        permission: "knowledge.view",
      },
      {
        label: "Settings",
        labelKey: "nav.settings",
        href: "/settings",
        icon: Settings,
        permission: "settings.live",
      },
    ],
  },
  {
    label: "Platform",
    labelKey: "nav.platform",
    tier: "platform",
    items: [
      {
        label: "Projects",
        labelKey: "nav.projects",
        href: "/projects",
        icon: FolderGit2,
        permission: "projects.view",
      },
      {
        label: "Identity",
        labelKey: "nav.identity",
        href: "/identity",
        icon: Users,
        permission: "identity.manage",
      },
      // Observability has no row of its own: the boards list and the connect
      // guide are a section of Compute — same tier, same permission class.
      {
        label: "Compute",
        labelKey: "nav.compute",
        href: "/compute",
        icon: Server,
        exact: false,
        permission: "compute.view",
      },
      {
        label: "Models",
        labelKey: "nav.models",
        href: "/models",
        icon: RouteIcon,
        permission: "models.view",
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
