import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import { beforeAll, describe, expect, it, vi } from "vitest"

import type {
  Budgets,
  TrackerProvider,
} from "@/domains/settings/model/types"
import type { BudgetFormValues } from "@/domains/settings/model/budget-form"
import { BudgetsPanel } from "@/domains/settings/ui/budgets-panel"
import { TrackerPanel } from "@/domains/settings/ui/tracker-panel"
import { useCan, type Role } from "@/shared/session"
import { TestSession } from "@/shared/session/test-session"

/* The two kill-switch toggles measure themselves on mount, and jsdom has no
   ResizeObserver to measure with. Without this the panel throws before a
   single permission is asked. */
beforeAll(() => {
  if (!("ResizeObserver" in globalThis)) {
    globalThis.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver
  }
})

const BUDGETS: Budgets = {
  perTaskUsd: 2,
  perAppUsd: 120,
  globalUsd: 900,
  usedUsd: 410,
  killSwitch: false,
  pauseSwarm: false,
}

const TRACKERS: TrackerProvider[] = [
  { id: "jira", name: "Jira", connected: true, meta: "PLX board", last: "4 min" },
  { id: "linear", name: "Linear", connected: false, meta: "not linked" },
]

/** The settings screen's arrangement: one `settings.live` answer for every
    panel that writes, because every one of them writes the same kind of thing. */
function LiveSettings({
  onSave,
  onToggleStop,
}: {
  onSave: (values: BudgetFormValues) => void
  onToggleStop: () => void
}) {
  const may = useCan("settings.live")
  return (
    <>
      <BudgetsPanel
        budgets={BUDGETS}
        save={may}
        onSave={onSave}
        onToggleStop={onToggleStop}
      />
      <TrackerPanel trackers={TRACKERS} edit={may} />
    </>
  )
}

function mount(roles: Role[]) {
  const onSave = vi.fn()
  const onToggleStop = vi.fn()
  render(
    <TestSession roles={roles}>
      <LiveSettings onSave={onSave} onToggleStop={onToggleStop} />
    </TestSession>
  )
  return {
    onSave,
    // The submit keeps its words — it commits the form. The two tracker acts
    // are glyphs now, and each names the provider it acts on, because the grid
    // draws one card per tracker.
    save: screen.getByRole("button", { name: "Save budgets" }),
    sync: screen.getByRole("button", { name: "Sync Jira" }),
    connect: screen.getByRole("button", { name: "Connect Linear" }),
  }
}

describe("live settings, by role", () => {
  it("leaves a member the forms and refuses every write", async () => {
    const { save, sync, connect, onSave } = mount(["member"])

    for (const control of [save, sync, connect]) {
      expect(document.body.contains(control)).toBe(true)
      expect(control.getAttribute("aria-disabled")).toBe("true")
      // The refusal is asserted where it is always written — `data-denied`
      // carries the sentence whether or not a tooltip is also carrying it.
      expect(control.getAttribute("data-denied")).toBe(
        "needs project-admin, operator or platform-admin"
      )
    }

    // The submit is not inside a kit tooltip, so it still hands the sentence
    // to a pointer itself. The two glyphs beside it deliberately do not: their
    // tooltip is already saying it, and twice is once too many.
    expect(save.getAttribute("title")).toBe(
      "needs project-admin, operator or platform-admin"
    )
    expect(sync.hasAttribute("title")).toBe(false)

    // The fields themselves stay writable — a cap you cannot apply is still a
    // cap worth working out before you ask for it.
    const perTask = screen.getByLabelText("per task")
    expect(perTask.hasAttribute("disabled")).toBe(false)
    fireEvent.change(perTask, { target: { value: "5" } })

    fireEvent.click(save)
    await waitFor(() => {
      expect(onSave).not.toHaveBeenCalled()
    })
  })

  it("applies the cap for an operator", async () => {
    const { save, sync, onSave } = mount(["operator"])

    expect(save.hasAttribute("aria-disabled")).toBe(false)
    expect(sync.hasAttribute("aria-disabled")).toBe(false)

    fireEvent.change(screen.getByLabelText("per task"), {
      target: { value: "5" },
    })
    fireEvent.click(save)

    await waitFor(() => {
      expect(onSave).toHaveBeenCalledWith(
        expect.objectContaining({ perTaskUsd: 5 })
      )
    })
  })
})
