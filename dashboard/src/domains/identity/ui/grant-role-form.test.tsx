import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { ApiKeyRow, UserRow } from "@/domains/identity/model/types"
import { GrantRoleForm } from "@/domains/identity/ui/grant-role-form"
import { ROLES, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { selectValues, setSelectValue } from "@/shared/ui/select/test-select"

/* The grant form moved out of a dialog and onto `/identity/grants/new`. Every
   rule it carried came with it — above all the one this file exists for: the
   six roles are the whole set, and nothing anywhere offers a seventh. */

const USERS: UserRow[] = [
  {
    id: "u_rhea",
    name: "Rhea Okafor",
    email: "rhea@comuki.local",
    oidcSubject: "oidc|comuki|4f21ba9c",
    status: "active",
    lastSeenAt: "2026-08-30 08:12",
    createdAt: "2026-03-04",
    scopes: ["platform"],
  },
  {
    id: "u_tomas",
    name: "Tomas Lindqvist",
    email: "tomas@comuki.local",
    oidcSubject: null,
    status: "disabled",
    lastSeenAt: "2026-07-18 11:26",
    createdAt: "2026-04-02",
    scopes: ["platform"],
  },
]

const KEYS: ApiKeyRow[] = [
  {
    id: "k_ci",
    name: "ci-pipeline",
    prefix: "cmk_4e9c",
    status: "active",
    createdAt: "2026-06-02",
    lastUsedAt: "2026-08-30 09:41",
    expiresAt: null,
    expiresInDays: null,
    grants: ["member on comuki"],
  },
  {
    id: "k_legacy",
    name: "legacy-import",
    prefix: "cmk_77aa",
    status: "revoked",
    createdAt: "2026-01-09",
    lastUsedAt: "2026-05-30 14:52",
    expiresAt: null,
    expiresInDays: null,
    grants: [],
  },
]

const PROJECTS = [
  { id: "p_comuki", slug: "comuki", name: "Comuki platform" },
  { id: "p_atlas", slug: "atlas", name: "Atlas" },
]

function mount(roles: Role[] = ["platform-admin"]) {
  const onGrant = vi.fn()
  const onCancel = vi.fn()
  render(
    <TestSession roles={roles}>
      <GrantRoleForm
        users={USERS}
        keys={KEYS}
        projects={PROJECTS}
        onGrant={onGrant}
        onCancel={onCancel}
      />
    </TestSession>
  )
  return {
    onGrant,
    onCancel,
    kind: screen.getByLabelText("subject kind"),
    subject: screen.getByLabelText("subject"),
    role: screen.getByLabelText("role"),
    scope: screen.getByLabelText("scope"),
    grant: screen.getByRole("button", { name: "Grant" }),
  }
}

/* The kit's select is a listbox now, not a native `<select>`, so the values
   are read and written through the form element React Aria keeps beside the
   trigger — the same one autofill and a `<form>` submit see. What each case
   asserts is unchanged; only the way it reaches the control is. */
const optionsOf = selectValues

describe("roles cannot be created, only granted", () => {
  it("offers exactly the six the code has, and no seventh", () => {
    const { role } = mount()

    expect(optionsOf(role)).toEqual([...ROLES])
    expect(optionsOf(role).length).toBe(6)
  })

  it("carries no affordance anywhere that would add one", () => {
    mount()

    // The two acts on this form are the whole set: grant, and cancel. A page
    // has more room than a dialog did, and the room bought nothing new here on
    // purpose — there is no role editor because there is no role editor.
    const buttons = screen
      .getAllByRole("button")
      // The kit's select is a listbox trigger, and a listbox trigger is a
      // `<button>`. It is a control, not an act, and this case is counting
      // acts: what the form lets somebody *do*.
      .filter((button) => button.getAttribute("aria-haspopup") !== "listbox")
      .map((button) => button.textContent?.trim())
    expect(buttons).toEqual(["Grant", "Cancel"])

    expect(
      screen.queryByText(/new role|create role|add role|custom role/i)
    ).toBeNull()
  })

  it("says why, rather than leaving the missing button to be noticed", () => {
    mount()

    // A rule that is only enforced reads as an omission. This one is written
    // under the field it constrains.
    expect(
      screen.getByText(
        "Roles live in code — these six are the whole set, and there is no way to add one."
      )
    ).toBeTruthy()
  })
})

describe("the three things a grant is", () => {
  it("grants on the platform when that is the scope", () => {
    const { role, grant, onGrant } = mount()

    setSelectValue(role, "operator")
    fireEvent.click(grant)

    expect(onGrant).toHaveBeenCalledWith({
      subjectKind: "user",
      subjectId: "u_rhea",
      role: "operator",
      projectId: null,
    })
  })

  it("asks which project only once the scope is a project", () => {
    const { scope, grant, onGrant } = mount()

    expect(screen.queryByLabelText("project")).toBeNull()

    setSelectValue(scope, "project")
    const project = screen.getByLabelText("project")
    expect(optionsOf(project)).toEqual(["p_comuki", "p_atlas"])

    setSelectValue(project, "p_atlas")
    fireEvent.click(grant)

    expect(onGrant).toHaveBeenCalledWith({
      subjectKind: "user",
      subjectId: "u_rhea",
      role: "viewer",
      projectId: "p_atlas",
    })
  })

  it("switches the subject list entirely when the kind changes", () => {
    const { kind, grant, onGrant } = mount()

    setSelectValue(kind, "api-key")
    const subject = screen.getByLabelText("subject")

    // A key is a first-class subject: it is granted roles exactly like a
    // person. A revoked one is not offered a new grant.
    expect(optionsOf(subject)).toEqual(["k_ci"])

    fireEvent.click(grant)
    expect(onGrant).toHaveBeenCalledWith({
      subjectKind: "api-key",
      subjectId: "k_ci",
      role: "viewer",
      projectId: null,
    })
  })

  it("does not offer a new grant to a disabled account", () => {
    const { subject } = mount()

    // The disabled account keeps the grants it has — that is a different
    // list, and a different act.
    expect(optionsOf(subject)).toEqual(["u_rhea"])
  })
})

describe("leaving the form", () => {
  it("hands cancelling back to the page", () => {
    const { onCancel } = mount()

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))
    expect(onCancel).toHaveBeenCalled()
  })

  it("counts a form still on its defaults as nothing worth keeping", () => {
    const onDirtyChange = vi.fn()
    render(
      <TestSession roles={["platform-admin"]}>
        <GrantRoleForm
          users={USERS}
          keys={KEYS}
          projects={PROJECTS}
          onGrant={() => {}}
          onCancel={() => {}}
          onDirtyChange={onDirtyChange}
        />
      </TestSession>
    )

    expect(onDirtyChange).toHaveBeenLastCalledWith(false)

    setSelectValue(screen.getByLabelText("role"), "operator")
    expect(onDirtyChange).toHaveBeenLastCalledWith(true)
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps the act in the document and names what it needs", () => {
    const { grant, onGrant } = mount(["operator"])

    expect(document.body.contains(grant)).toBe(true)
    expect(grant.getAttribute("aria-disabled")).toBe("true")
    expect(grant.getAttribute("title")).toBe("needs platform-admin")
    expect(grant.hasAttribute("disabled")).toBe(false)

    fireEvent.click(grant)
    expect(onGrant).not.toHaveBeenCalled()
  })
})
