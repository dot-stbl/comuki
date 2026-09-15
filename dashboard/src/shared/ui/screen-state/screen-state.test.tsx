import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { ForbiddenState } from "./forbidden-state"
import { ScreenState } from "./screen-state"

/* The four states have to be interchangeable in a list's body, and the whole
   argument for one component instead of four is that nothing but the words and
   the interruption may differ between them. Both halves of that are checkable
   here: jsdom lays nothing out, but it does render the DOM the reading is made
   of. */

describe("only the failure interrupts", () => {
  it("gives an error state the alert role", () => {
    render(<ScreenState kind="error" title="The backlog did not load" />)
    expect(document.querySelector("[role='alert']")).not.toBeNull()
  })

  it.each(["empty", "notFound", "forbidden"] as const)(
    "leaves a %s state silent",
    (kind) => {
      render(<ScreenState kind={kind} title="Nothing here" />)
      // An empty list, a missing id and a closed view are all ordinary
      // arrivals. A screen that shouted every one of them would teach the
      // operator to stop listening to the one that matters.
      expect(document.querySelector("[role='alert']")).toBeNull()
    }
  )
})

describe("the state renders only what it was given", () => {
  it("draws no sentence, hint or action slot when none were passed", () => {
    const { container } = render(
      <ScreenState kind="empty" title="No runs yet." />
    )
    expect(container.querySelectorAll("p")).toHaveLength(1)
    expect(container.querySelectorAll("div")).toHaveLength(1)
  })

  it("names the kind on the element, so a screen can be inspected", () => {
    render(<ScreenState kind="notFound" title="No project with that id" />)
    expect(document.querySelector("[data-state='notFound']")).not.toBeNull()
  })
})

describe("Forbidden is the same state with the words already written", () => {
  it("keeps the test hook the app's permission guard looks for", () => {
    render(<ForbiddenState needs="needs platform-admin" subject="Identity" />)
    const state = document.querySelector("[data-test='forbidden-state']")
    expect(state).not.toBeNull()
    expect(state?.getAttribute("data-state")).toBe("forbidden")
    expect(state?.textContent).toContain("Identity is closed to your roles")
    expect(state?.textContent).toContain("needs platform-admin")
  })

  it("does not announce itself as a failure", () => {
    // A closed view is not a bigger event than an empty one: the operator did
    // nothing wrong, they hold a different role.
    render(<ForbiddenState needs="needs approver" />)
    expect(document.querySelector("[role='alert']")).toBeNull()
  })
})
