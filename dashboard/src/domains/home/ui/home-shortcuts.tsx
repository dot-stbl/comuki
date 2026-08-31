import { CheckCircle2, ListTodo, PlayCircle } from "lucide-react"
import { Link } from "@tanstack/react-router"

import { can, useSession, type Permission } from "@/shared/session"
import { Tooltip, buttonClass } from "@/shared/ui"

import styles from "./home-shortcuts.module.css"

interface Shortcut {
  label: string
  /** What it is for, in one line — the shortcut is a control, not a riddle. */
  note: string
  to: string
  permission: Permission
  icon: typeof ListTodo
}

/**
 * The three places a duty engineer goes from here.
 *
 * `New run` lands on the inbox because that is where a run actually begins:
 * work enters the swarm by taking a ticket, and there is no other door. The
 * note says so rather than leaving the label to imply a form that does not
 * exist. The icons are the rail's own, so a shortcut and the rail item it
 * duplicates are recognisably the same destination.
 */
const SHORTCUTS: Shortcut[] = [
  {
    label: "New run",
    note: "take a ticket from the inbox",
    to: "/tasks",
    permission: "inbox.take",
    icon: ListTodo,
  },
  {
    label: "Live runs",
    note: "the whole swarm, with the flow board and filters",
    to: "/runs",
    permission: "runs.view",
    icon: PlayCircle,
  },
  {
    label: "Approvals",
    note: "plans queued for a decision",
    to: "/approvals",
    permission: "plans.approve",
    icon: CheckCircle2,
  },
]

/**
 * Shortcuts — and the one place on this screen where something is hidden.
 *
 * The access rule has two halves and they point opposite ways: an *action* a
 * role cannot perform stays visible and explains itself, but *navigation* it
 * cannot use is removed. These are links to screens, so a shortcut whose screen
 * is closed goes — it would otherwise walk the operator into a wall that the
 * rail has already, correctly, hidden from them.
 */
export function HomeShortcuts() {
  const session = useSession()
  const visible = SHORTCUTS.filter((shortcut) =>
    can(session, shortcut.permission)
  )

  if (visible.length === 0) {
    return null
  }

  return (
    <ul className={styles.list} data-test="home-shortcuts">
      {visible.map((shortcut) => {
        const Icon = shortcut.icon
        return (
          <li className={styles.row} key={shortcut.to}>
            {/* All three take the glyph, including the one-word `Approvals`.
                The three sit in one column and a row of two glyphs and one
                worded button would read as three different kinds of control
                rather than three of the same. The note beside each stays
                written, so the column is never a strip of bare marks. */}
            <Tooltip content={shortcut.label}>
              <Link
                to={shortcut.to}
                aria-label={shortcut.label}
                className={buttonClass({
                  variant: "outline",
                  size: "icon",
                  className: styles.link,
                })}
                data-test="home-shortcut"
              >
                <Icon aria-hidden="true" />
              </Link>
            </Tooltip>
            <span className={styles.note}>{shortcut.note}</span>
          </li>
        )
      })}
    </ul>
  )
}
