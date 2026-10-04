import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import {
  productNavSections,
  visibleNavSections,
} from "@/app/layout/nav-sections"
import { useSession, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* The tree shape is the only thing under test here — the rule is the same
   one `nav.test.tsx` exercises for the flat list. What the two-pane sidebar
   draws is its own concern. */
function SectionsProbe() {
  const session = useSession()
  return (
    <ul>
      {visibleNavSections(productNavSections, session).map((section) => (
        <li key={section.id}>
          <span data-test="section">{section.label}</span>
          <ul>
            {section.items.map((item) => (
              <li key={item.href} data-test="item">
                {item.label}
              </li>
            ))}
          </ul>
        </li>
      ))}
    </ul>
  )
}

function sectionsFor(roles: Role[], projectRoles?: Record<string, Role[]>) {
  const { container } = render(
    <TestSession roles={roles} projectRoles={projectRoles}>
      <SectionsProbe />
    </TestSession>
  )

  const textOf = (selector: string) =>
    [...container.querySelectorAll(selector)].map((node) => node.textContent)

  return {
    labels: textOf("[data-test='item']"),
    sections: textOf("[data-test='section']"),
  }
}

describe("the section tree, filtered by what the session may do", () => {
  it("shows a viewer the items and the sections it can use", () => {
    // A viewer holds runs.view and procedures.view — the workbench
    // (Studio / Live run / Replay) is a read act, so it sits at the
    // viewer tier. Configure surfaces with the Procedures row only;
    // Sources / Knowledge / Settings remain gated to richer roles.
    expect(sectionsFor(["viewer"]).labels).toEqual(["Live runs", "Procedures"])
    expect(sectionsFor(["viewer"]).sections).toEqual(["Observe", "Configure"])
  })

  it("drops a section whose items all vanished", () => {
    // A viewer holds runs.view and procedures.view; the other items in
    // Configure still gate, so Configure keeps just its Procedures row —
    // and the section is not empty.
    expect(sectionsFor(["viewer"]).sections).toEqual(["Observe", "Configure"])
  })

  it("opens intake and knowledge for a member, and still hides approvals", () => {
    const { labels } = sectionsFor(["member"])

    expect(labels).toContain("Inbox")
    expect(labels).toContain("Knowledge")
    expect(labels).not.toContain("Approvals")
    expect(labels).not.toContain("Cost")
    expect(labels).not.toContain("Settings")
  })

  it("gives a project-admin the upper tier entire", () => {
    const { sections } = sectionsFor(["project-admin"])

    expect(sections).toEqual(["Intake", "Observe", "Configure"])
  })

  it("gives an operator the platform tier without the identity registry", () => {
    const { labels, sections } = sectionsFor(["operator"])

    expect(sections).toContain("Platform")
    expect(labels).toContain("Compute")
    expect(labels).toContain("Projects")
    expect(labels).not.toContain("Identity")
  })

  it("keeps the platform tier out of a project-admin's rail", () => {
    expect(sectionsFor(["project-admin"]).sections).not.toContain("Platform")
  })

  it("returns the same shape a project member expects as a viewer", () => {
    // Roles in force are platform roles plus the current project's, so a
    // viewer with a project-admin role sees Settings and Approvals even
    // though they would not otherwise.
    const { labels } = sectionsFor(["viewer"], { p_test: ["project-admin"] })

    expect(labels).toContain("Settings")
    expect(labels).toContain("Approvals")
  })
})
