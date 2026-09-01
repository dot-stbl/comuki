import {
  Activity,
  BookOpen,
  CheckCircle2,
  Coins,
  FolderGit2,
  type LucideIcon,
  ListOrdered,
  ListTodo,
  PlayCircle,
  Plug,
  Route as RouteIcon,
  Server,
  Settings,
  SlidersHorizontal,
  Inbox as InboxIcon,
  Layers,
  Users,
} from "lucide-react"

import { can, type Permission, type Session } from "@/shared/session"

/**
 * The rail, as a tree.
 *
 * The two-pane sidebar reads navigation in two layers: a thin outer column of
 * section icons (the context switcher) and a wider inner column of the pages
 * inside the active section. Each item in `NavItem` is one row of the inner
 * rail, each entry in `productNavSections` is one icon of the outer.
 *
 * Sections are visited on different clocks — the duty engineer's day versus
 * the platform underneath it — and a section that mixes them makes the
 * frequent things harder to hit. `tier: "platform"` carries the seam between
 * the two, and a session with no platform access simply never renders one.
 *
 * Every item still names the act that opens it. Keying the rail on acts rather
 * than on roles is what lets one permission gate the item, the route and the
 * buttons inside it without any of the three agreeing on a role list first.
 */
export interface NavItem {
  label: string
  href: string
  icon?: LucideIcon
  /** When false, child routes (e.g. /runs/$runId) keep the parent link active. */
  exact?: boolean
  /** Which live count to show, if any. */
  badge?: "running" | "needsHuman"
  /**
   * The act this item is for. An item the session cannot perform is hidden
   * rather than disabled; an item with no permission is always shown.
   */
  permission?: Permission
}

export interface NavSection {
  /** Machine name; used by the variant hook to remember a "last section". */
  id: "intake" | "observe" | "configure" | "platform"
  /** Display label, also used as the inner-rail heading when this section is active. */
  label: string
  /** Outer-rail icon — never the same as an item icon, so the two never rhyme. */
  icon: LucideIcon
  /**
   * `platform` sits below the divider — the machinery under the product,
   * visited on a different clock from the work above it. Absent means work.
   */
  tier?: "work" | "platform"
  /**
   * If defined, the section itself is hidden when the session lacks this act,
   * even if items would otherwise survive. The section's permission is the
   * most restrictive of its items as a rule of thumb.
   */
  permission?: Permission
  items: NavItem[]
}

export const productNavSections: NavSection[] = [
  {
    id: "intake",
    label: "Intake",
    icon: InboxIcon,
    items: [
      // The console is not a section: its one door in the chrome is the
      // floating trigger over the board (see `domains/chat`, the dock), so a
      // conversation is something you have, not somewhere you go.
      {
        label: "Inbox",
        href: "/tasks",
        icon: ListTodo,
        permission: "inbox.view",
      },
    ],
  },
  {
    id: "observe",
    label: "Observe",
    icon: Activity,
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
      {
        label: "Cost",
        href: "/cost",
        icon: Coins,
        permission: "cost.view",
      },
    ],
  },
  {
    id: "configure",
    label: "Configure",
    icon: SlidersHorizontal,
    items: [
      {
        label: "Sources",
        href: "/sources",
        icon: Plug,
        permission: "sources.view",
      },
      // Verify has no row of its own: it is the same chassis as Knowledge —
      // read-only registries sourced from the client's git — and lives as the
      // gate tab there.
      {
        label: "Knowledge",
        href: "/knowledge",
        icon: BookOpen,
        exact: false,
        permission: "knowledge.view",
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
    id: "platform",
    label: "Platform",
    icon: Layers,
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
      // Observability has no row of its own: the boards list and the connect
      // guide are a section of Compute — same tier, same permission class.
      {
        label: "Compute",
        href: "/compute",
        icon: Server,
        exact: false,
        permission: "compute.view",
      },
      {
        label: "Models",
        href: "/models",
        icon: RouteIcon,
        permission: "models.view",
      },
    ],
  },
]

/**
 * The sections, as this session may actually use them.
 *
 * Same rule as `visibleNav` in `nav.ts`: navigation a role cannot use is hidden
 * rather than disabled, and a section whose items all vanished is dropped too
 * — a heading standing over nothing is a worse artefact than the missing item.
 * A section-level permission is checked first; if the section is hidden, its
 * items do not need to be.
 *
 * Plain function rather than a hook so the sidebar, a test and — the day
 * there is one — a route loader can all ask the same question.
 */
export function visibleNavSections(
  sections: NavSection[],
  session: Session
): NavSection[] {
  return sections
    .filter((section) => !section.permission || can(session, section.permission))
    .map((section) => ({
      ...section,
      items: section.items.filter(
        (item) => !item.permission || can(session, item.permission)
      ),
    }))
    .filter((section) => section.items.length > 0)
}