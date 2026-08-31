import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import { CreateProjectForm } from "@/domains/projects/ui/create-project-form"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* The form moved out of a dialog and onto `/projects/new`, and every rule it
   carried came with it. These are the same assertions the dialog's tests made:
   the slug proposal, the four ways a handle can be refused, and the gated
   submit that explains itself instead of going grey. The page around it — the
   crumbs, the cancel, the unsaved guard — is `create-project-page.test.tsx`,
   because none of that needs three fields to be true. */

function mount(roles: Role[], taken: string[] = []) {
  const onCreate = vi.fn()
  const onCancel = vi.fn()
  render(
    <TestSession roles={roles}>
      <CreateProjectForm
        takenSlugs={taken}
        onCreate={onCreate}
        onCancel={onCancel}
      />
    </TestSession>
  )
  return {
    onCreate,
    onCancel,
    name: screen.getByLabelText("name"),
    slug: screen.getByLabelText("slug") as HTMLInputElement,
    repo: screen.getByLabelText("git profile repository"),
    create: screen.getByRole("button", { name: "Create project" }),
    cancel: screen.getByRole("button", { name: "Cancel" }),
  }
}

describe("creating a project", () => {
  it("proposes a slug from the name until somebody touches the field", () => {
    const { name, slug } = mount(["platform-admin"])

    fireEvent.change(name, { target: { value: "Payments Platform" } })
    expect(slug.value).toBe("payments-platform")

    // Touched: the proposal stops, because a handle nobody chose is a handle
    // nobody will recognise in the column it lands in.
    fireEvent.change(slug, { target: { value: "payments" } })
    fireEvent.change(name, { target: { value: "Payments Platform v2" } })
    expect(slug.value).toBe("payments")
  })

  it("creates with the slug that is actually in the field", () => {
    const { name, repo, create, onCreate } = mount(["platform-admin"])

    fireEvent.change(name, { target: { value: "Payments Platform" } })
    fireEvent.change(repo, {
      target: { value: "git@github.com:acme/profiles.git" },
    })
    fireEvent.click(create)

    expect(onCreate).toHaveBeenCalledWith({
      name: "Payments Platform",
      slug: "payments-platform",
      gitProfileRepo: "git@github.com:acme/profiles.git",
    })
  })

  it("treats an empty repository as running on the platform defaults", () => {
    const { name, create, onCreate } = mount(["platform-admin"])

    fireEvent.change(name, { target: { value: "Vega" } })
    fireEvent.click(create)

    expect(onCreate).toHaveBeenCalledWith({
      name: "Vega",
      slug: "vega",
      gitProfileRepo: null,
    })
  })

  it("refuses a slug that is not a handle, and says which rule it broke", () => {
    const { name, slug, create, onCreate } = mount(["platform-admin"])

    fireEvent.change(name, { target: { value: "Payments" } })
    fireEvent.change(slug, { target: { value: "Payments Platform" } })

    expect(screen.getByRole("alert").textContent).toContain(
      "no spaces — use a hyphen"
    )

    fireEvent.click(create)
    expect(onCreate).not.toHaveBeenCalled()
  })

  it("refuses a slug somebody already has", () => {
    const { name, slug, create, onCreate } = mount(["platform-admin"], ["atlas"])

    fireEvent.change(name, { target: { value: "Atlas again" } })
    fireEvent.change(slug, { target: { value: "atlas" } })

    expect(screen.getByRole("alert").textContent).toContain(
      "that slug is taken"
    )

    fireEvent.click(create)
    expect(onCreate).not.toHaveBeenCalled()
  })

  it("marks the field invalid for assistive tech, not just in ink", () => {
    const { name, slug } = mount(["platform-admin"])

    fireEvent.change(name, { target: { value: "Atlas" } })
    fireEvent.change(slug, { target: { value: "Atlas" } })

    expect(slug.getAttribute("aria-invalid")).toBe("true")
    expect(slug.getAttribute("aria-describedby")).toBe(
      "project-slug-description"
    )
  })

  it("hands cancelling back to the page rather than deciding where to go", () => {
    const { name, cancel, onCancel } = mount(["platform-admin"])

    fireEvent.change(name, { target: { value: "Vega" } })
    fireEvent.click(cancel)

    // The form knows the fields; only the page knows where the operator came
    // from. Cancel is not a form act, so it is not the form's decision.
    expect(onCancel).toHaveBeenCalled()
  })

  it("tells the page the moment there is something worth keeping", () => {
    const onDirtyChange = vi.fn()
    render(
      <TestSession roles={["platform-admin"]}>
        <CreateProjectForm
          takenSlugs={[]}
          onCreate={() => {}}
          onCancel={() => {}}
          onDirtyChange={onDirtyChange}
        />
      </TestSession>
    )

    // Nothing typed is not an unsaved form, and a page that asked about it
    // would be asking about nothing.
    expect(onDirtyChange).toHaveBeenLastCalledWith(false)

    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Vega" },
    })
    expect(onDirtyChange).toHaveBeenLastCalledWith(true)
  })
})

describe("a shift that may not create one", () => {
  it("keeps the act in the document and names what it needs", () => {
    // `projects.create` is a platform permission: project roles never answer
    // for it, so a viewer holding three projects still cannot create a fourth.
    const { name, create, onCreate } = mount(["viewer"])

    fireEvent.change(name, { target: { value: "Payments Platform" } })

    expect(document.body.contains(create)).toBe(true)
    expect(create.getAttribute("aria-disabled")).toBe("true")
    expect(create.getAttribute("title")).toBe("needs operator or platform-admin")
    // Not `disabled`: that would put the sentence out of reach of a pointer
    // and out of the tab order both.
    expect(create.hasAttribute("disabled")).toBe(false)

    fireEvent.click(create)
    expect(onCreate).not.toHaveBeenCalled()
  })

  it("is not opened by a project role, however senior", () => {
    const onCreate = vi.fn()
    render(
      <TestSession roles={[]} projectRoles={{ p_test: ["project-admin"] }}>
        <CreateProjectForm
          takenSlugs={[]}
          onCreate={onCreate}
          onCancel={() => {}}
        />
      </TestSession>
    )

    const create = screen.getByRole("button", { name: "Create project" })
    expect(create.getAttribute("aria-disabled")).toBe("true")
  })
})
