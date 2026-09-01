import { Link } from "@tanstack/react-router"

import { cn } from "@/shared/lib/utils"
import { useSession } from "@/shared/session"
import { Tooltip } from "@/shared/ui"

import {
  visibleNavSections,
  type NavItem,
  type NavSection,
} from "@/app/layout/nav-sections"

import styles from "./app-shell-two-pane-sidebar.module.css"

/* The wire format the inner and the outer share: the count badge a row may
   carry, plus the source numbers. Keeping it a typed object means the
   queries are the only thing that touches the upstream shape — the items
   below render the bag. */
export interface NavCounts {
  running: number
  needsHuman: number
}

export interface AppShellTwoPaneOuterProps {
  sections: NavSection[]
  /** The currently active section id; the column highlights its icon. */
  activeId?: NavSection["id"]
  /** Click handler — wired by the shell so the icon navigates to a page. */
  onSelect: (section: NavSection) => void
}

/**
 * The outer column of section icons — always 48px, never collapses away.
 *
 * It lives outside `react-resizable-panels`: when the inner column collapses
 * to zero the outer survives alone, and tooltips always name each icon
 * (there is never a word beside them). Clicking an icon navigates to the
 * section's first item rather than merely highlighting it.
 */
export function AppShellTwoPaneOuter({
  sections,
  activeId,
  onSelect,
}: AppShellTwoPaneOuterProps) {
  const session = useSession()
  const visible = visibleNavSections(sections, session)

  return (
    <nav className={styles.outer} data-outer-rail aria-label="Sections">
      {visible.map((section) => {
        const Icon = section.icon
        const isActive = activeId === section.id
        return (
          <Tooltip
            key={section.id}
            content={section.label}
            placement="end"
          >
            <button
              type="button"
              className={styles.sectionButton}
              data-active={isActive || undefined}
              aria-label={section.label}
              aria-current={isActive ? "true" : undefined}
              data-test="two-pane-section"
              onClick={() => onSelect(section)}
            >
              <Icon aria-hidden="true" className={styles.sectionIcon} />
            </button>
          </Tooltip>
        )
      })}
    </nav>
  )
}

export interface AppShellTwoPaneInnerProps {
  /** The section to render. `undefined` only when no sections are visible. */
  section: NavSection | undefined
  counts: NavCounts
  /** Icon-only rail: nothing in here renders at all. */
  collapsed: boolean
}

/**
 * The inner column — the pages of the active section, with the section's
 * icon and name as a heading. The two-pane sidebar's inner is what
 * `react-resizable-panels` collapses; the outer column lives above this and
 * is the parent's concern.
 */
export function AppShellTwoPaneInner({
  section,
  counts,
  collapsed,
}: AppShellTwoPaneInnerProps) {
  if (collapsed || !section) {
    return null
  }

  return (
    <div className={styles.inner}>
      {/* No section heading here — the outer icon + its tooltip already name
          the section. Repeating it as a label above the pages was noise. */}
      <div className={styles.scroll}>
        <ul className={styles.itemList} aria-label={section.label}>
          {section.items.map((item) => (
            <Item
              key={item.href}
              item={item}
              counts={counts}
              testId="two-pane-item"
            />
          ))}
        </ul>
      </div>
    </div>
  )
}

interface ItemProps {
  item: NavItem
  counts: NavCounts
  testId: string
}

/** A single row in the inner rail — its own component because the active
 *  styles are load-bearing and inlining them in the map grew the parent
 *  past the point where the row could be read on its own. */
function Item({ item, counts, testId }: ItemProps) {
  const count = item.badge ? counts[item.badge] : null
  const Icon = item.icon

  return (
    <li>
      <Link
        to={item.href}
        activeOptions={{ exact: item.exact ?? item.href === "/" }}
        className={styles.item}
        activeProps={{ className: cn(styles.item, styles.itemActive) }}
        data-test={testId}
      >
        {Icon ? (
          <Icon aria-hidden="true" className={styles.itemIcon} />
        ) : null}
        <span className={styles.itemLabel}>{item.label}</span>
        {count ? (
          <span
            className={cn(
              styles.count,
              item.badge === "needsHuman" && styles.countAlert
            )}
            data-test="rail-badge"
          >
            {count}
          </span>
        ) : null}
      </Link>
    </li>
  )
}