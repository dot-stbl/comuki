import { useMemo } from "react"
import type { LucideIcon } from "lucide-react"
import { Link } from "@tanstack/react-router"

import { visibleNav } from "@/app/layout/nav"
import { RailAccount } from "@/app/layout/rail-account"
import { useApprovalsQuery } from "@/domains/approvals/api/queries"
import { useRunsQuery } from "@/domains/runs/api/queries"
import { cn } from "@/shared/lib/utils"
import { useSession, type Permission } from "@/shared/session"
import { Tooltip } from "@/shared/ui"

import styles from "./app-shell-sidebar.module.css"

export interface SidebarNavItem {
  label: string
  href: string
  icon?: LucideIcon
  /** When false, child routes (e.g. /runs/$runId) keep the parent link active. */
  exact?: boolean
  /** Which live count to show, if any. */
  badge?: "running" | "needsHuman"
  /**
   * The act this section is for. An item the session cannot perform is hidden
   * rather than disabled; an item with no permission is always shown.
   */
  permission?: Permission
}

export interface SidebarNavGroup {
  label: string
  items: SidebarNavItem[]
  /**
   * `platform` groups sit below the divider — the machinery under the product,
   * visited on a different clock from the work above it. Absent means work.
   */
  tier?: "work" | "platform"
}

export interface AppShellSidebarProps {
  groups: SidebarNavGroup[]
  /** Icon-only rail: the panel is collapsed, or the viewport is too narrow. */
  collapsed?: boolean
}

export function AppShellSidebar({
  groups,
  collapsed = false,
}: AppShellSidebarProps) {
  const { data = [] } = useRunsQuery()
  const session = useSession()

  // Filtered here rather than by the shell that hands the groups over: the rail
  // is the thing that knows what a rail item is, and a caller that had to
  // pre-filter would be one more place the rule could be forgotten.
  const visible = useMemo(() => visibleNav(groups, session), [groups, session])

  // The divider is drawn on the first platform group rather than at a fixed
  // index, so a session with no platform access simply never sees one — an
  // empty tier must not leave a rule floating above the account block.
  const firstPlatform = visible.findIndex(
    (group) => group.tier === "platform"
  )

  // Two live counts, each about the row it rides on. `running` is the runs
  // the board is showing in motion; `needsHuman` is the queue of decisions
  // waiting on the Approvals screen — the approvals queue IS the undecided
  // set (deciding removes the card), so its length is the count. It counted
  // waiting runs once, which was the runs screen's reading worn on the wrong
  // row: a badge that answers "how is my screen" must count that screen's
  // own things.
  const { data: approvals = [] } = useApprovalsQuery()
  const counts = useMemo(
    () => ({
      running: data.filter((run) => run.status === "running").length,
      needsHuman: approvals.length,
    }),
    [data, approvals]
  )

  return (
    <aside className={styles.rail} data-collapsed={collapsed || undefined}>
      <div className={styles.scroll}>
        {visible.map((group, index) => (
          <nav
            key={group.label}
            className={cn(
              styles.group,
              index === firstPlatform && styles.tierBreak
            )}
            aria-label={group.label}
          >
            <span className={styles.groupLabel}>{group.label}</span>
            {group.items.map((item) => {
              const count = item.badge ? counts[item.badge] : null
              return (
                /* The item's own label never leaves the accessibility tree —
                   collapsed it is clipped to zero width, not hidden — so the
                   tooltip is a second channel for a pointer and a keyboard,
                   not the name itself. It replaces the native `title`, which
                   arrived a second late, could not be styled and was invisible
                   to touch. The wrapper is always in the tree and switches off
                   with `disabled`: rendering it conditionally would remount the
                   link, and a remounted element has no previous state to
                   transition the collapse from. */
                <Tooltip
                  key={item.href}
                  content={item.label}
                  placement="end"
                  disabled={!collapsed}
                >
                  <Link
                    to={item.href}
                    activeOptions={{ exact: item.exact ?? item.href === "/" }}
                    className={styles.item}
                    activeProps={{ className: styles.active }}
                  >
                    {item.icon ? (
                      <item.icon className={styles.icon} aria-hidden="true" />
                    ) : null}
                    <span className={styles.itemLabel}>{item.label}</span>
                    {count ? (
                      /* The count span never carries the alert alone: the row
                         it rides on is what it counts, and the count reads as
                         a figure with or without the colour. */
                      <span
                        className={cn(
                          styles.count,
                          item.badge === "needsHuman" && styles.alert
                        )}
                        data-test="rail-badge"
                      >
                        {count}
                      </span>
                    ) : null}
                  </Link>
                </Tooltip>
              )
            })}
          </nav>
        ))}
      </div>

      {/* Anchored to the floor of the rail, not to the flow above it: identity
          is the one thing whose position must not move as sections appear and
          disappear with a role. */}
      <RailAccount collapsed={collapsed} />
    </aside>
  )
}
