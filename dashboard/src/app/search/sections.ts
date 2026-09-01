import type { SidebarNavGroup } from "@/app/layout/app-shell-sidebar"
import { productNav, visibleNav } from "@/app/layout/nav"
import { can, type Permission, type Session } from "@/shared/session"

/**
 * Layer (b): the product's own places, and the handful of acts that open a
 * form rather than a list.
 *
 * It touches no data at all. A section is a screen the rail already names, and
 * an act is a route that exists — so this layer answers instantly, is the same
 * length on every shift, and cannot go stale against a seed.
 *
 * **The access rule is not re-litigated here.** `visibleNav` is the rail's own
 * answer to "may this session go there", and the palette reads it rather than
 * writing a second opinion: a screen the rail hides is a screen the palette
 * does not offer, and the day a permission moves, both change together because
 * there is only one place that knows. Acts are gated the same way, by the same
 * `can`, against the permission their route's `RequirePermission` names.
 */

/** A place, as the rail already words it. */
export interface SearchSection {
  /** The rail's own label — the word an operator has already been taught. */
  label: string
  href: string
  /** The rail group it sits under. Matched as well as shown. */
  group: string
}

/** A thing to start, rather than a place to go. */
export interface SearchAct {
  label: string
  href: string
  permission: Permission
  /** What it produces, in the product's words. */
  hint: string
}

/**
 * Every screen this session can reach, flattened out of the rail.
 *
 * `groups` is a parameter so a test can hand in a rail of its own; the default
 * is the one the shell renders, which is the whole point — two lists of the
 * product's screens would be one list too many.
 */
export function navSections(
  session: Session,
  groups: SidebarNavGroup[] = productNav
): SearchSection[] {
  return visibleNav(groups, session).flatMap((group) =>
    group.items.map((item) => ({
      label: item.label,
      href: item.href,
      group: group.label,
    }))
  )
}

/**
 * The acts worth reaching without going to the screen that hosts them first.
 *
 * Deliberately short. Every one of these is a *form* — a route that creates
 * something — because those are the destinations an operator otherwise reaches
 * in two hops through a list they did not want to look at. Acts that operate on
 * a row (approve, cancel, drain) are not here and should not be: they need the
 * row, and a palette that offered them would be offering a verb with no object.
 */
export const SEARCH_ACTS: SearchAct[] = [
  {
    label: "New task",
    href: "/tasks/new",
    permission: "inbox.take",
    hint: "put a ticket in the backlog",
  },
  {
    label: "New project",
    href: "/projects/new",
    permission: "projects.create",
    hint: "register a project",
  },
  {
    label: "New user",
    href: "/identity/users/new",
    permission: "identity.manage",
    hint: "invite a person",
  },
  {
    label: "New api key",
    href: "/identity/keys/new",
    permission: "identity.manage",
    hint: "issue a key",
  },
  {
    label: "Grant a role",
    href: "/identity/grants/new",
    permission: "identity.manage",
    hint: "give somebody access",
  },
]

/** The acts this session may actually perform. Hidden, never disabled. */
export function visibleActs(
  session: Session,
  acts: SearchAct[] = SEARCH_ACTS
): SearchAct[] {
  return acts.filter((act) => can(session, act.permission))
}
