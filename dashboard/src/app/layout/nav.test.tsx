import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { productNav, visibleNav } from "@/app/layout/nav"
import { useSession, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* The rail itself renders links and live counts, so it needs a router and a
   query client to say anything at all. What is under test here is the rule, not
   the chrome: this probe puts the session through the same filter the rail
   calls and prints the result. */
function NavProbe() {
  const session = useSession()
  return (
    <ul>
      {visibleNav(productNav, session).map((group) => (
        <li key={group.label}>
          <span data-test="group">{group.label}</span>
          <ul>
            {group.items.map((item) => (
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

function railFor(roles: Role[], projectRoles?: Record<string, Role[]>) {
  const { container } = render(
    <TestSession roles={roles} projectRoles={projectRoles}>
      <NavProbe />
    </TestSession>
  )

  const textOf = (selector: string) =>
    [...container.querySelectorAll(selector)].map((node) => node.textContent)

  return {
    labels: textOf("[data-test='item']"),
    groups: textOf("[data-test='group']"),
  }
}

describe("the rail, filtered by what the session may do", () => {
  it("shows a viewer the one section it can use", () => {
    // One item, and it is the whole rail. Attention lost its row when the rail
    // reached sixteen — the mark has been the home link since the topbar was
    // built — and the dev showcase left the product's navigation entirely.
    expect(railFor(["viewer"]).labels).toEqual(["Live runs"])
  })

  it("drops a group whose items all vanished", () => {
    // A viewer holds runs.view and nothing else, so Intake and Configure lose
    // every item they had — and a heading standing over nothing is a worse
    // artefact than the missing item.
    expect(railFor(["viewer"]).groups).toEqual(["Observe"])
  })

  it("opens intake and knowledge for a member, and still hides approvals", () => {
    const { labels } = railFor(["member"])

    expect(labels).toContain("Inbox")
    expect(labels).toContain("Knowledge")
    expect(labels).not.toContain("Approvals")
    expect(labels).not.toContain("Cost")
    expect(labels).not.toContain("Settings")
  })

  it("opens approvals for an approver without opening settings", () => {
    const { labels } = railFor(["approver"])

    expect(labels).toContain("Approvals")
    expect(labels).not.toContain("Settings")
  })

  it("gives a project-admin the whole product rail", () => {
    const { labels, groups } = railFor(["project-admin"])

    expect(labels).toEqual([
      "Inbox",
      "Live runs",
      "Queue",
      "Approvals",
      "Cost",
      "Sources",
      "Knowledge",
      "Settings",
    ])
    // The upper tier entire, and not one item of the lower one: administering
    // a project is not the same as administering the platform it runs on.
    expect(groups).toEqual(["Intake", "Observe", "Configure"])
  })

  it("gives an operator the platform tier without the identity registry", () => {
    const { labels, groups } = railFor(["operator"])

    expect(groups).toContain("Platform")
    expect(labels).toContain("Compute")
    expect(labels).toContain("Models")
    expect(labels).toContain("Projects")
    expect(labels).not.toContain("Identity")

    // The one branch in a table that otherwise reads as a ladder: operator is
    // platform ops, and approving a plan stays a project judgement.
    expect(labels).toContain("Settings")
    expect(labels).toContain("Cost")
  })

  it("keeps the platform tier out of a project-admin's rail", () => {
    // Platform acts read platform roles alone: being project-admin of one
    // project must never open the registry every project shares.
    const { groups } = railFor(["project-admin"])

    expect(groups).not.toContain("Platform")
  })

  it("keeps the console out of the rail entirely", () => {
    // The console is not a section: it is the floating trigger over the
    // board and the sheet it opens. A rail row would be a second door to a
    // container that already has exactly one, and `chat.use` gates the
    // trigger, not a destination.
    const { labels } = railFor(["platform-admin"])

    expect(labels).not.toContain("Chat")
  })

  it("gives the same person a longer rail on a project they administer", () => {
    // Roles in force are the platform roles plus the current project's, so
    // scope is a real axis of the rail and not decoration. Verify adds no row
    // any more — its gate is a tab of Knowledge — so the extension shows as
    // Settings and Approvals.
    const { labels } = railFor(["viewer"], { p_test: ["project-admin"] })

    expect(labels).toContain("Settings")
    expect(labels).toContain("Approvals")
  })
})
