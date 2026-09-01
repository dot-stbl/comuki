import { describe, expect, it } from "vitest"

import {
  productNavSections,
  type NavSection,
} from "@/app/layout/nav-sections"
import { pickActiveSection } from "@/app/layout/nav-active-section"

describe("pickActiveSection — which section owns this pathname", () => {
  it("matches the exact route to its item", () => {
    expect(pickActiveSection(productNavSections, "/settings")?.id).toBe(
      "configure"
    )
    expect(pickActiveSection(productNavSections, "/approvals")?.id).toBe(
      "observe"
    )
    expect(pickActiveSection(productNavSections, "/tasks")?.id).toBe("intake")
  })

  it("matches a child route to its non-exact parent", () => {
    // Live runs is `exact: false`, so `/runs/$runId` belongs to observe.
    expect(pickActiveSection(productNavSections, "/runs/4711")?.id).toBe(
      "observe"
    )
    // Knowledge hosts Verify as a gate tab, exact: false.
    expect(pickActiveSection(productNavSections, "/knowledge/gate")?.id).toBe(
      "configure"
    )
  })

  it("does not match a prefix that is not a path segment", () => {
    // `/tasks-archive` is not under `/tasks` — the prefix must be either the
    // whole path or followed by `/`, so a sibling section does not light up
    // by accident.
    expect(pickActiveSection(productNavSections, "/tasks-archive")).toBeUndefined()
    expect(pickActiveSection(productNavSections, "/settings-extra")).toBeUndefined()
  })

  it("returns undefined when no section claims the path", () => {
    // A screen the rail does not name — login, an error page, anything
    // outside the rail's vocabulary — leaves the outer column blank.
    expect(pickActiveSection(productNavSections, "/login")).toBeUndefined()
    expect(pickActiveSection(productNavSections, "/")).toBeUndefined()
  })

  it("returns undefined for an empty section list", () => {
    expect(pickActiveSection([], "/runs")).toBeUndefined()
  })

  it("prefers exact match over parent match when both could apply", () => {
    // Imagine a section with both `/runs` (exact) and `/runs-archive` (parent).
    // The exact `/runs` wins over the parent match for `/runs/something`.
    const sections: NavSection[] = [
      {
        id: "intake",
        label: "Exact",
        icon: { displayName: "X" } as never,
        items: [{ label: "Runs", href: "/runs", exact: false }],
      },
      {
        id: "observe",
        label: "Sibling",
        icon: { displayName: "S" } as never,
        items: [{ label: "Archive", href: "/runs-archive", exact: false }],
      },
    ]
    expect(pickActiveSection(sections, "/runs/4711")?.id).toBe("intake")
    expect(pickActiveSection(sections, "/runs-archive")?.id).toBe("observe")
  })
})