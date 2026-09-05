import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { ProbeResult } from "@/domains/sources/model/types"
import { ConnectSourceForm } from "@/domains/sources/ui/connect-source-form"
import {
  type SecretReferenceDraft,
  type TestDraftInput,
} from "@/domains/sources/api/mutations"
import {
  connectSeedSource,
  probeSeedSourceDraft,
  resetSeedSources,
  type SeedSourceDraft,
} from "@/shared/api/mock/sources.store"
import type { Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"
import {
  selectValues,
  setSelectValue,
} from "@/shared/ui/select/test-select"

/* The form moved out of a dialog and onto `/sources/new`, and every rule it
   carried came with it: the four decisions in the order they constrain each
   other, the closed credential list, the base url that only exists where an
   instance can be, the secret-env NAME the host resolves at probe / webhook
   time, and the probe that has to answer before the save opens. These are
   the dialog's own assertions, on the component that replaced it.

   The form's password box is mock-only — real mode reads the env var on the
   host instead. The page around it — the crumbs, the cancel, the unsaved
   guard, the landing on the connection it made — lives in
   `connect-source-page.test.tsx`, because none of that needs the wire to be
   the source of truth. */

/** The one secret this file ever types in mock mode. Nothing may echo it back. */
const SECRET = "ghp-not-a-real-token-0001"

/** The env-var name the form collects on every mode. */
const SECRET_ENV_REF = "COMUKI_GITHUB_TOKEN"

interface HarnessProps {
  probe?: ProbeResult | null
  probing?: boolean
  busy?: boolean
  onTest?: (input: TestDraftInput) => void
  onCreate?: (draft: SecretReferenceDraft) => void
  onDraftChange?: () => void
  roles?: Role[]
  projectRoles?: Record<string, Role[]>
}

function mount({
  probe = null,
  probing = false,
  busy = false,
  onTest = () => {},
  onCreate = () => {},
  onDraftChange = () => {},
  roles = ["platform-admin"],
  projectRoles = {},
}: HarnessProps = {}) {
  render(
    <TestSession roles={roles} projectRoles={projectRoles}>
      <ConnectSourceForm
        probe={probe}
        probing={probing}
        busy={busy}
        onTest={onTest}
        onDraftChange={onDraftChange}
        onCreate={onCreate}
        onCancel={() => {}}
      />
    </TestSession>
  )
}

function control(testId: string) {
  return document.querySelector(`[data-test="${testId}"]`) as HTMLElement
}

/* `SelectField` is the kit's one select — a listbox trigger rather than a
   native `<select>` — so a select's values are read and written through the
   form element React Aria keeps beside the trigger, which is what a `<form>`
   submit and browser autofill see. `data-test` still lands on the trigger. */
function chooseIn(testId: string, value: string) {
  setSelectValue(control(testId), value)
}

function offeredIn(testId: string) {
  return selectValues(control(testId))
}

function fill(testId: string, value: string) {
  const field = control(testId) as HTMLInputElement
  fireEvent.change(field, { target: { value } })
  return field
}

function submitButton() {
  return control("form-submit") as HTMLButtonElement
}

function testButton() {
  return control("connect-test") as HTMLButtonElement
}

/** A complete github draft in mock mode. Real mode drops the secret box. */
function fillDraft(secret = SECRET) {
  fill("connect-name", "here/web-app")
  fill("connect-account", "svc-bot")
  fill("connect-secret-env", SECRET_ENV_REF)
  fill("connect-mock-secret", secret)
}

describe("test connection, before anything is saved", () => {
  it("will not save until the provider has answered", () => {
    mount()
    fillDraft()

    // Invalid, not forbidden: `disabled` and never `denied`, because an
    // untested form is incomplete rather than out of this role's reach, and the
    // two states must not look alike.
    expect(submitButton().hasAttribute("disabled")).toBe(true)
    expect(submitButton().hasAttribute("aria-disabled")).toBe(false)
    expect(
      screen.getByText(
        "test the connection before saving — an unreachable source looks exactly like a healthy one until something needs it."
      )
    ).toBeTruthy()
  })

  it("says it is reaching the provider while the probe is out", () => {
    mount({ probing: true })
    fillDraft()

    // The control is a glyph, so "probing" is spoken through `aria-busy` and
    // drawn by the spinner rather than by swapping a label there is no room
    // for. The sentence beside it still says it in words.
    expect(testButton().getAttribute("aria-label")).toBe("Test connection")
    expect(testButton().hasAttribute("disabled")).toBe(true)
    expect(testButton().getAttribute("aria-busy")).toBe("true")
    expect(screen.getByText("reaching the provider…")).toBeTruthy()
    // Still no save: pending is not an answer.
    expect(submitButton().hasAttribute("disabled")).toBe(true)
  })

  it("shows the provider's refusal and keeps the save shut", () => {
    mount({
      probe: {
        ok: false,
        message: "401 from api.github.com — the credential was rejected.",
      },
    })
    fillDraft()

    const notice = control("probe-result")
    expect(notice?.getAttribute("data-tone")).toBe("bad")
    expect(notice?.textContent).toContain(
      "401 from api.github.com — the credential was rejected."
    )
    expect(submitButton().hasAttribute("disabled")).toBe(true)
  })

  it("opens the save once the provider has said yes", () => {
    const onCreate = vi.fn()
    mount({
      probe: { ok: true, message: "reached api.github.com" },
      onCreate,
    })
    fillDraft()

    expect(submitButton().hasAttribute("disabled")).toBe(false)
    fireEvent.click(submitButton())
    expect(onCreate).toHaveBeenCalledTimes(1)
  })

  it("tells the page to forget the last answer on any edit", () => {
    const onDraftChange = vi.fn()
    mount({
      probe: { ok: true, message: "reached api.github.com" },
      onDraftChange,
    })

    // Every field, because a credential that worked before the host was changed
    // is not evidence about the host that is there now — and one field that
    // forgot to say so would be the one that ships a broken connection.
    fill("connect-account", "another-bot")
    expect(onDraftChange).toHaveBeenCalled()

    onDraftChange.mockClear()
    chooseIn("connect-project", "p_other")
    expect(onDraftChange).toHaveBeenCalled()
  })

  it("hands the env-var name to the probe and to nothing else", () => {
    const onTest = vi.fn()
    const onCreate = vi.fn()
    mount({
      probe: { ok: true, message: "reached api.github.com" },
      onTest,
      onCreate,
    })
    fillDraft()

    fireEvent.click(testButton())
    // The mock path carries the literal secret alongside the env-var name;
    // the real path never sees the literal value. The wire-shape contract:
    // the env-var name reaches the probe and the save, the literal does not.
    expect(onTest).toHaveBeenCalledWith({
      draft: expect.objectContaining({ name: "here/web-app" }),
      secretEnvRef: SECRET_ENV_REF,
      mockSecret: SECRET,
    })

    fireEvent.click(submitButton())
    const draft = onCreate.mock.calls[0][0] as SecretReferenceDraft
    // The save carries the env-var name, never the secret.
    expect(JSON.stringify(draft)).toContain(SECRET_ENV_REF)
    expect(JSON.stringify(draft)).not.toContain(SECRET)
  })
})

describe("the secret is named once, where it is configured", () => {
  it("warns at the env-var box rather than after the button", () => {
    mount()

    const field = control("connect-secret-env")
    expect(field?.getAttribute("type")).not.toBe("password")
    // Hint copy lives on the field, the way the secret-notice once did —
    // a rule explained after the act is an apology, not an explanation.
    expect(field?.getAttribute("placeholder")).toBe("COMUKI_GITHUB_TOKEN")
  })

  it("shows the mock-only credential box in mock mode", () => {
    mount()

    const mockBox = control("connect-mock-secret")
    expect(mockBox).not.toBeNull()
    expect(mockBox.getAttribute("type")).toBe("password")
  })

  it("never echoes the mock secret back once the form is gone", () => {
    const { unmount } = render(
      <TestSession roles={["platform-admin"]}>
        <ConnectSourceForm
          probe={{ ok: true, message: "reached api.github.com" }}
          probing={false}
          onTest={() => {}}
          onDraftChange={() => {}}
          onCreate={() => {}}
          onCancel={() => {}}
        />
      </TestSession>
    )
    fillDraft()

    // While it is open the operator can see what they are typing, and the box
    // is a password field so a shoulder cannot.
    const field = control("connect-mock-secret") as HTMLInputElement
    expect(field.getAttribute("type")).toBe("password")
    expect(field.value).toBe(SECRET)

    fireEvent.click(submitButton())
    unmount()

    expect(document.body.textContent ?? "").not.toContain(SECRET)
  })

  it("keeps no copy of it in the store either", () => {
    resetSeedSources()
    const draft: SeedSourceDraft = {
      projectId: "p_test",
      kind: "github",
      name: "here/web-app",
      auth: "pat",
      account: "svc-bot",
      baseUrl: "",
    }

    // The probe reads it and answers; the store's create never receives it.
    expect(probeSeedSourceDraft(draft, SECRET).ok).toBe(true)
    const created = connectSeedSource(draft)

    expect(JSON.stringify(created)).not.toContain(SECRET)
    // What survives is a date, which is all the product will ever say about it.
    expect(created.secretStoredAt).toBe("just now")
    resetSeedSources()
  })
})

describe("the form only asks for what the connector can use", () => {
  it("offers a base url for a self-hosted provider and not for a cloud one", () => {
    mount()

    expect(control("connect-base-url")).toBeNull()

    chooseIn("connect-kind", "gitlab")

    expect(control("connect-base-url")).not.toBeNull()
  })

  it("never offers native, which every project already has", () => {
    mount()
    expect(offeredIn("connect-kind")).toEqual([
      "github",
      "gitlab",
      "yandex-tracker",
      "jira",
    ])
  })

  it("shows the settings json the host will store", () => {
    mount()
    fill("connect-name", "here/web-app")
    fill("connect-account", "svc-bot")
    fill("connect-secret-env", SECRET_ENV_REF)

    // The preview is one of the page's clearest signals: the operator
    // sees the literal json blob the host will persist and can sanity-check
    // before submitting.
    const preview = control("settings-preview")
    expect(preview).not.toBeNull()
    expect(preview.textContent).toContain('"auth":"pat"')
    expect(preview.textContent).toContain('"account":"svc-bot"')
  })
})

describe("saving answers to the project the form picked", () => {
  it("explains the save on a project this session only watches", () => {
    mount({
      roles: ["viewer"],
      projectRoles: { p_test: ["project-admin"] },
      probe: { ok: true, message: "reached api.github.com" },
    })
    fillDraft()

    // p_test first, where this session administers: the save is live.
    // `aria-disabled` is omitted when the button is permitted (the kit renders
    // it only when the button is blocked); absence is the live state.
    expect(submitButton().getAttribute("aria-disabled")).toBeNull()
    expect(submitButton().getAttribute("data-denied")).toBeNull()

    chooseIn("connect-project", "p_other")

    // The same form, the same credential, a different project — and the button
    // stays where it was and says what would open it.
    expect(submitButton().getAttribute("aria-disabled")).toBe("true")
    expect(submitButton().getAttribute("data-denied")).toBe(
      "needs project-admin or platform-admin on other"
    )
    expect(submitButton().getAttribute("title")).toBe(
      "needs project-admin or platform-admin on other"
    )
    expect(submitButton().hasAttribute("disabled")).toBe(false)
    expect(submitButton().getAttribute("title")).toBe(
      "needs project-admin or platform-admin on other"
    )
    expect(submitButton().hasAttribute("disabled")).toBe(false)
  })

  it("refuses the submit itself, not only the button that carries it", () => {
    const onCreate = vi.fn()
    mount({
      roles: ["viewer"],
      projectRoles: { p_test: ["project-admin"] },
      probe: { ok: true, message: "reached api.github.com" },
      onCreate,
    })
    fillDraft()
    chooseIn("connect-project", "p_other")

    // A form can be submitted with a keyboard from inside a text field, which
    // never touches the button at all.
    fireEvent.submit(control("connect-source"))
    expect(onCreate).not.toHaveBeenCalled()
  })
})