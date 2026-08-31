import { useMemo } from "react"
import { fireEvent, render, screen } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import type { CreateTaskInput, Task } from "@/domains/tasks/model/types"
import { CreateTaskDialog } from "@/domains/tasks/ui/create-task-dialog"
import { createTaskColumns, getTaskId } from "@/domains/tasks/ui/tasks-columns"
import { useSession, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { selectValues, setSelectValue } from "@/shared/ui/select/test-select"
import { DataTable } from "@/shared/ui"

/* jsdom lays nothing out and the table body is virtualized, so without a port
   depth the rows — and with them the buttons under test — never render. */
beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
  Object.defineProperty(HTMLElement.prototype, "offsetHeight", {
    configurable: true,
    value: 320,
  })
  Object.defineProperty(HTMLElement.prototype, "offsetWidth", {
    configurable: true,
    value: 960,
  })
})

/** Two tickets, two projects — which is what a shared backlog looks like. */
const TASKS: Task[] = [
  {
    id: "m-3055",
    projectId: "p_test",
    source: "manual",
    title: "retire the legacy webhook",
    app: "checkout-web",
    priority: "high",
    status: "new",
    age: "8 min",
  },
  {
    id: "m-3056",
    projectId: "p_other",
    source: "manual",
    title: "rotate the signing key",
    app: "identity-svc",
    priority: "normal",
    status: "new",
    age: "20 min",
  },
]

/** The backlog as `TasksPage` assembles it — session in the component, value
    in the factory, because a column `cell` is a function and not a render. */
function Backlog({ onDispatch }: { onDispatch: (task: Task) => void }) {
  const session = useSession()
  const columns = useMemo(
    () =>
      createTaskColumns({
        apps: [],
        projects: session.projects,
        dispatching: false,
        onDispatch,
        session,
      }),
    [onDispatch, session]
  )

  return (
    <DataTable
      columns={columns}
      data={TASKS}
      getRowId={getTaskId}
      density="compact"
    />
  )
}

function Intake({ onCreate }: { onCreate: (input: CreateTaskInput) => void }) {
  return (
    <CreateTaskDialog
      open
      apps={["checkout-web"]}
      onOpenChange={() => {}}
      onCreate={onCreate}
    />
  )
}

function mountBacklog(
  roles: Role[],
  projectRoles: Record<string, Role[]> = {}
) {
  const onDispatch = vi.fn()
  render(
    <TestSession roles={roles} projectRoles={projectRoles}>
      <Backlog onDispatch={onDispatch} />
    </TestSession>
  )
  return {
    onDispatch,
    here: screen.getByRole("button", {
      name: `Dispatch ${TASKS[0].title}`,
    }),
    there: screen.getByRole("button", {
      name: `Dispatch ${TASKS[1].title}`,
    }),
  }
}

function mountIntake(roles: Role[], projectRoles: Record<string, Role[]> = {}) {
  const onCreate = vi.fn()
  render(
    <TestSession roles={roles} projectRoles={projectRoles}>
      <Intake onCreate={onCreate} />
    </TestSession>
  )
  /* `SelectField` is the kit's one select now — a listbox trigger rather than
     a native `<select>` — so the values are read and written through the form
     element React Aria keeps beside it, which is what a `<form>` submit and
     browser autofill see. Every assertion below is the one it always was. */
  const project = screen.getByLabelText("Project")
  return {
    onCreate,
    project,
    options: selectValues(project),
    title: screen.getByLabelText("Title"),
    create: screen.getByRole("button", { name: "Create & queue" }),
  }
}

describe("the backlog's dispatch, by row", () => {
  it("answers per ticket, not per shift", () => {
    // Member on one project, viewer everywhere else: the same backlog carries
    // a ticket this person may hand to the swarm beside one they may not.
    const { here, there, onDispatch } = mountBacklog(["viewer"], {
      p_test: ["member"],
    })

    expect(here.hasAttribute("aria-disabled")).toBe(false)

    expect(there.getAttribute("aria-disabled")).toBe("true")
    // The sentence rides `data-denied`. Inside a kit tooltip the button drops
    // its native title rather than say the same thing twice.
    expect(there.getAttribute("data-denied")).toBe(
      "needs member, approver, project-admin, operator or platform-admin on other"
    )
    expect(there.hasAttribute("disabled")).toBe(false)

    fireEvent.click(there)
    expect(onDispatch).not.toHaveBeenCalled()

    fireEvent.click(here)
    expect(onDispatch).toHaveBeenCalledWith(TASKS[0])
  })

  it("keeps the button on a viewer's row and names what it needs", () => {
    const { here, onDispatch } = mountBacklog(["viewer"])

    expect(document.body.contains(here)).toBe(true)
    expect(here.getAttribute("aria-disabled")).toBe("true")
    expect(here.getAttribute("data-denied")).toBe(
      "needs member, approver, project-admin, operator or platform-admin on test"
    )
    expect(here.hasAttribute("disabled")).toBe(false)

    fireEvent.click(here)
    expect(onDispatch).not.toHaveBeenCalled()
  })

  it("hands both rows to a platform member", () => {
    const { here, there, onDispatch } = mountBacklog(["member"])

    expect(here.hasAttribute("aria-disabled")).toBe(false)
    expect(there.hasAttribute("aria-disabled")).toBe(false)

    fireEvent.click(there)
    expect(onDispatch).toHaveBeenCalledWith(TASKS[1])
  })

  it("shows each ticket's project by key", () => {
    mountBacklog(["member"])

    expect(screen.getByText("test")).toBeTruthy()
    expect(screen.getByText("other")).toBeTruthy()
  })
})

describe("manual intake, by project", () => {
  it("offers only the projects this shift may put work into", () => {
    // The choices behind an act are filtered even though the act itself stays
    // visible: a select is a list of things that can happen.
    const { options } = mountIntake(["viewer"], { p_test: ["member"] })

    expect(options).toEqual(["p_test"])
  })

  it("creates in the project that was chosen", () => {
    const { create, title, project, onCreate } = mountIntake(["member"])

    expect(selectValues(project).length).toBe(2)
    setSelectValue(project, "p_other")
    fireEvent.change(title, { target: { value: "look into the flake" } })
    fireEvent.click(create)

    expect(onCreate).toHaveBeenCalledWith({
      projectId: "p_other",
      title: "look into the flake",
      app: "checkout-web",
      priority: "normal",
      brief: undefined,
    })
  })

  it("lets a viewer fill the form in and refuses the queueing", () => {
    const { create, title, options, onCreate } = mountIntake(["viewer"])

    fireEvent.change(title, { target: { value: "look into the flake" } })

    // Nowhere to put it, so the form stays open and the submit carries the
    // one denial for the whole thing rather than each field carrying its own.
    expect(options).toEqual([])
    expect(document.body.contains(create)).toBe(true)
    expect(create.getAttribute("aria-disabled")).toBe("true")
    expect(create.getAttribute("title")).toBe(
      "needs member, approver, project-admin, operator or platform-admin"
    )
    // Not `disabled`: that would take the sentence out of reach of a pointer
    // and out of the tab order both.
    expect(create.hasAttribute("disabled")).toBe(false)

    fireEvent.click(create)
    expect(onCreate).not.toHaveBeenCalled()
  })

  it("queues the task for a member", () => {
    const { create, title, onCreate } = mountIntake(["member"])

    fireEvent.change(title, { target: { value: "look into the flake" } })
    fireEvent.click(create)

    expect(onCreate).toHaveBeenCalledWith({
      projectId: "p_test",
      title: "look into the flake",
      app: "checkout-web",
      priority: "normal",
      brief: undefined,
    })
  })
})
