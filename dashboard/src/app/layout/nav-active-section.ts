import { useMemo } from "react"
import { useLocation } from "@tanstack/react-router"

import type { NavSection } from "@/app/layout/nav-sections"

/**
 * Pick the section whose items include a given pathname.
 *
 * Pure function extracted from the hook so it can be unit-tested without a
 * Router context — the hook is a one-line wrapper that hands it the current
 * pathname.
 *
 * Rules, in order:
 * 1. If an item's `href` matches the pathname exactly, its section wins.
 * 2. If an item is `exact: false` and the pathname is either equal to it or
 *    extends it as a path segment (`/foo` → `/foo/bar`), its section wins.
 * 3. Otherwise no section claims this path — return `undefined`.
 *
 * `pathname.startsWith(item.href + "/")` rather than a plain `startsWith`
 * so `/tasks-archive` does not light up `/tasks` — the prefix must be a
 * path segment, not a substring.
 */
export function pickActiveSection(
  sections: NavSection[],
  pathname: string
): NavSection | undefined {
  if (sections.length === 0) {
    return undefined
  }
  for (const section of sections) {
    const exact = section.items.find((item) => item.href === pathname)
    if (exact) {
      return section
    }
  }
  for (const section of sections) {
    const parent = section.items.find(
      (item) =>
        item.exact === false &&
        (pathname === item.href || pathname.startsWith(item.href + "/"))
    )
    if (parent) {
      return section
    }
  }
  return undefined
}

/**
 * React hook wrapper around `pickActiveSection` that reads the pathname
 * from TanStack Router.
 *
 * The two-pane sidebar is *navigation* rather than *destination*: which
 * section is active is a function of the URL, not a piece of UI state that
 * has to survive a refresh. Returns `undefined` when the URL is somewhere
 * the rail does not name so the outer column stays blank rather than
 * highlighting the wrong one.
 */
export function useActiveNavSection(
  sections: NavSection[]
): NavSection | undefined {
  const location = useLocation()
  const pathname = location.pathname

  return useMemo(() => pickActiveSection(sections, pathname), [sections, pathname])
}