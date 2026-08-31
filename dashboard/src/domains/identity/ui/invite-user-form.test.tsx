import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import { InviteUserForm } from "@/domains/identity/ui/invite-user-form"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import { setSelectValue } from "@/shared/ui/select/test-select"

/* The invite form moved out of a dialog and onto `/identity/users/new`. The
   two ways a person can start existing here are still genuinely two, and the
   submit still says which one is about to happen. */

function mount(roles: Role[] = ["platform-admin"], taken: string[] = []) {
  const onInvite = vi.fn()
  const onCancel = vi.fn()
  render(
    <TestSession roles={roles}>
      <InviteUserForm
        takenAddresses={taken}
        onInvite={onInvite}
        onCancel={onCancel}
      />
    </TestSession>
  )
  return {
    onInvite,
    onCancel,
    name: screen.getByLabelText("name"),
    email: screen.getByLabelText("address") as HTMLInputElement,
    // The kit's select is a listbox now, so its value is written through the
    // form element React Aria keeps beside the trigger — the same one a
    // `<form>` submit and browser autofill see.
    how: screen.getByLabelText("how"),
  }
}

const submitButton = () =>
  document.querySelector('[data-test="form-submit"]') as HTMLButtonElement

describe("two ways to start existing", () => {
  it("names the act it is about to perform, rather than saying save", () => {
    const { how } = mount()

    expect(submitButton().textContent).toBe("Send invitation")

    setSelectValue(how, "local")
    expect(submitButton().textContent).toBe("Create account")
  })

  it("sends an invitation, which is an account waiting to be accepted", () => {
    const { name, email, onInvite } = mount()

    fireEvent.change(name, { target: { value: "Ines Duarte" } })
    fireEvent.change(email, { target: { value: "Ines@Plexor.dev" } })
    fireEvent.click(submitButton())

    // The address is the account, so it is stored the one way it will always
    // be compared: folded.
    expect(onInvite).toHaveBeenCalledWith({
      name: "Ines Duarte",
      email: "ines@plexor.dev",
      invite: true,
    })
  })

  it("creates a local account, which works immediately", () => {
    const { name, email, how, onInvite } = mount()

    fireEvent.change(name, { target: { value: "Ines Duarte" } })
    fireEvent.change(email, { target: { value: "ines@plexor.dev" } })
    setSelectValue(how, "local")
    fireEvent.click(submitButton())

    expect(onInvite).toHaveBeenCalledWith({
      name: "Ines Duarte",
      email: "ines@plexor.dev",
      invite: false,
    })
  })
})

describe("an address is the account", () => {
  it("refuses something that is not one, and says so", () => {
    const { name, email, onInvite } = mount()

    fireEvent.change(name, { target: { value: "Ines" } })
    fireEvent.change(email, { target: { value: "ines" } })
    fireEvent.click(submitButton())

    expect(screen.getByRole("alert").textContent).toContain(
      "that does not look like an address"
    )
    expect(onInvite).not.toHaveBeenCalled()
  })

  it("refuses one somebody already has", () => {
    const { name, email, onInvite } = mount(
      ["platform-admin"],
      ["ines@plexor.dev"]
    )

    fireEvent.change(name, { target: { value: "Ines again" } })
    fireEvent.change(email, { target: { value: "ines@plexor.dev" } })
    fireEvent.click(submitButton())

    expect(screen.getByRole("alert").textContent).toContain(
      "somebody already has that address"
    )
    expect(onInvite).not.toHaveBeenCalled()
  })

  it("marks the field invalid for assistive tech, not just in ink", () => {
    const { name, email } = mount()

    fireEvent.change(name, { target: { value: "Ines" } })
    fireEvent.change(email, { target: { value: "ines" } })
    fireEvent.click(submitButton())

    expect(email.getAttribute("aria-invalid")).toBe("true")
    expect(email.getAttribute("aria-describedby")).toBe(
      "user-email-description"
    )
  })
})

describe("leaving the form", () => {
  it("hands cancelling back to the page", () => {
    const { onCancel } = mount()

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }))
    expect(onCancel).toHaveBeenCalled()
  })

  it("tells the page the moment there is something worth keeping", () => {
    const onDirtyChange = vi.fn()
    render(
      <TestSession roles={["platform-admin"]}>
        <InviteUserForm
          takenAddresses={[]}
          onInvite={() => {}}
          onCancel={() => {}}
          onDirtyChange={onDirtyChange}
        />
      </TestSession>
    )

    expect(onDirtyChange).toHaveBeenLastCalledWith(false)

    fireEvent.change(screen.getByLabelText("name"), {
      target: { value: "Ines" },
    })
    expect(onDirtyChange).toHaveBeenLastCalledWith(true)
  })
})

describe("a shift that may not administer identity", () => {
  it("keeps the act in the document and names what it needs", () => {
    const { name, email, onInvite } = mount(["operator"])

    fireEvent.change(name, { target: { value: "Ines" } })
    fireEvent.change(email, { target: { value: "ines@plexor.dev" } })

    const submit = submitButton()
    expect(submit.getAttribute("aria-disabled")).toBe("true")
    expect(submit.getAttribute("title")).toBe("needs platform-admin")
    expect(submit.hasAttribute("disabled")).toBe(false)

    fireEvent.click(submit)
    expect(onInvite).not.toHaveBeenCalled()
  })
})
