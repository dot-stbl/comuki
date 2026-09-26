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
    /* `/^name/`, not `"name"`: the field is marked required, and the marker is
       a real word inside the label — so the accessible name is "name required"
       and an exact match stops finding it. The marker is deliberately not
       hidden from assistive tech (see `RequiredMark` in the kit), so the
       matcher is what gives here, not the label. */
    name: screen.getByLabelText(/^name/),
    slug: screen.getByLabelText(/^slug/) as HTMLInputElement,
    repo: screen.getByLabelText("git profile repository"),
    icon: screen.getByLabelText(/^icon/),
    colour: screen.getByLabelText("accent colour") as HTMLInputElement,
    tags: screen.getByLabelText(/^tags/),
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
      // The identity fields ride along empty — an empty create is a real
      // answer, not an incomplete one.
      icon: null,
      color: null,
      tags: [],
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
      icon: null,
      color: null,
      tags: [],
    })
  })

  it("carries the identity fields onto the create payload", () => {
    const { name, icon, colour, tags, create, onCreate } = mount([
      "platform-admin",
    ])

    fireEvent.change(name, { target: { value: "Comuki" } })
    fireEvent.change(icon, { target: { value: "🛰️" } })
    fireEvent.change(colour, { target: { value: "#3c5a86" } })
    fireEvent.change(tags, { target: { value: "Web" } })
    fireEvent.keyDown(tags, { key: "Enter" })
    fireEvent.click(create)

    expect(onCreate).toHaveBeenCalledWith({
      name: "Comuki",
      slug: "comuki",
      gitProfileRepo: null,
      icon: "🛰️",
      color: "#3c5a86",
      // Normalised on commit — the chip shows what every list will show.
      tags: ["web"],
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
    const { name, slug, create, onCreate } = mount(
      ["platform-admin"],
      ["atlas"]
    )

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

    fireEvent.change(screen.getByLabelText(/^name/), {
      target: { value: "Vega" },
    })
    expect(onDirtyChange).toHaveBeenLastCalledWith(true)
  })

  it("counts a chosen colour or a committed tag as worth keeping too", () => {
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

    expect(onDirtyChange).toHaveBeenLastCalledWith(false)

    const colour = screen.getByLabelText("accent colour")
    fireEvent.change(colour, { target: { value: "#3c5a86" } })
    expect(onDirtyChange).toHaveBeenLastCalledWith(true)
  })
})

describe("the tag entry", () => {
  function mountTags() {
    const onCreate = vi.fn()
    render(
      <TestSession roles={["platform-admin"]}>
        <CreateProjectForm
          takenSlugs={[]}
          onCreate={onCreate}
          onCancel={() => {}}
        />
      </TestSession>
    )
    return {
      onCreate,
      name: screen.getByLabelText(/^name/),
      colour: screen.getByLabelText("accent colour"),
      tags: screen.getByLabelText(/^tags/),
      create: screen.getByRole("button", { name: "Create project" }),
    }
  }

  it("commits a tag on Enter and on a comma, normalised", () => {
    const { tags } = mountTags()

    fireEvent.change(tags, { target: { value: "  Billing " } })
    fireEvent.keyDown(tags, { key: "Enter" })
    fireEvent.change(tags, { target: { value: "web," } })

    expect(screen.getByText("billing")).toBeTruthy()
    expect(screen.getByText("web")).toBeTruthy()
    // The draft cleared both ways: by the Enter commit and by the comma.
    expect((tags as HTMLInputElement).value).toBe("")
  })

  it("refuses a duplicate, whichever case it arrives in", () => {
    const { tags } = mountTags()

    fireEvent.change(tags, { target: { value: "web" } })
    fireEvent.keyDown(tags, { key: "Enter" })
    fireEvent.change(tags, { target: { value: "WEB" } })
    fireEvent.keyDown(tags, { key: "Enter" })

    // One chip, and the draft is dropped — the second copy said nothing the
    // first did not.
    expect(screen.getAllByText("web").length).toBe(1)
    expect((tags as HTMLInputElement).value).toBe("")
  })

  it("removes the last chip on Backspace in an empty field", () => {
    const { tags } = mountTags()

    fireEvent.change(tags, { target: { value: "web" } })
    fireEvent.keyDown(tags, { key: "Enter" })
    fireEvent.change(tags, { target: { value: "billing" } })
    fireEvent.keyDown(tags, { key: "Enter" })
    fireEvent.keyDown(tags, { key: "Backspace" })

    expect(screen.queryByText("billing")).toBeNull()
    expect(screen.getByText("web")).toBeTruthy()
  })

  it("removes one chip by its own ×", () => {
    const { tags } = mountTags()

    fireEvent.change(tags, { target: { value: "web" } })
    fireEvent.keyDown(tags, { key: "Enter" })
    fireEvent.click(screen.getByRole("button", { name: "Remove tag web" }))

    expect(screen.queryByText("web")).toBeNull()
  })

  it("never disables the submit over the identity fields", () => {
    const { name, tags, colour, create } = mountTags()

    // The submit's gate is the name and busy, nothing else — the identity
    // fields are optional by contract, entered or not.
    fireEvent.change(name, { target: { value: "Vega" } })
    fireEvent.change(tags, { target: { value: "web" } })
    fireEvent.keyDown(tags, { key: "Enter" })
    fireEvent.change(colour, { target: { value: "#3c5a86" } })
    expect(create.hasAttribute("disabled")).toBe(false)

    // Clearing the name is the one empty that refuses the act.
    fireEvent.change(name, { target: { value: "" } })
    expect(create.hasAttribute("disabled")).toBe(true)
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
    expect(create.getAttribute("title")).toBe(
      "needs operator or platform-admin"
    )
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
