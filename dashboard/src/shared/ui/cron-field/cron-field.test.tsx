import { useState } from "react"
import { act, render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { CronField } from "./cron-field"

/* A cron field is a preset picker and, when the operator asks for more, five
 * small selects that compose a five-field cron string. The three things that
 * make it a control rather than a styled `<input>` are: the presets set the
 * whole wire (not the typed text), the custom editor composes the wire as
 * the operator moves each select, and switching from preset to custom keeps
 * the value rather than resetting it. */

/* The kit's `Select` is the seam these tests write through. It exposes a
 * hidden native `<select>` so a test can change the value without driving
 * the popover — the same seam `select.test.tsx` already uses. */
import {
  nativeSelect,
  setSelectValue,
} from "@/shared/ui/select/test-select"

function Harness({
  initial = "",
  onChange,
  disabled = false,
}: {
  initial?: string
  onChange?: (next: string) => void
  disabled?: boolean
}) {
  const [value, setValue] = useState(initial)
  return (
    <CronField
      id="cron"
      label="cron"
      value={value}
      disabled={disabled}
      onValueChange={(next) => {
        setValue(next)
        onChange?.(next)
      }}
    />
  )
}

// The preset row's accessible name is `<label>` joined with the description,
// so a regex anchored to the row's name matches without pulling in the
// description sentence that follows.
const preset = (label: string) =>
  screen.getByRole("radio", { name: new RegExp(`^${label}`) }) as HTMLInputElement

// React Aria renders the `id` directly on the trigger button — the seam
// `test-select.ts` already understands: its `parentElement` carries the
// hidden native `<select>` beside it.
const cronTrigger = (id: string): HTMLElement =>
  document.querySelector<HTMLElement>(`#${id}`) as HTMLElement

/** Read the value the select holds. */
const cronValue = (id: string): string => nativeSelect(cronTrigger(id)).value

describe("the presets are the operator's first vocabulary", () => {
  it("lists every cadence the product ships", () => {
    render(<Harness />)
    expect(preset("Hourly")).not.toBeNull()
    expect(preset("Daily at 03:00")).not.toBeNull()
    expect(preset("Weekly Monday")).not.toBeNull()
    expect(preset("Monthly 1st")).not.toBeNull()
    expect(preset("Custom…")).not.toBeNull()
  })

  it("sets the whole wire when a preset is picked", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await user.click(preset("Weekly Monday"))

    expect(onChange).toHaveBeenLastCalledWith("0 3 * * 1")
  })

  it("starts in preset mode when the value the field arrived with is a preset", () => {
    render(<Harness initial="0 3 * * 1" />)

    // Weekly Monday is the row whose wire matches the seeded value.
    expect(preset("Weekly Monday").checked).toBe(true)
    // The custom editor is closed — the form holds the preset, not the
    // editor's five selects.
    expect(screen.queryByRole("listbox")).toBeNull()
  })

  it("starts in custom mode when the value the field arrived with is not a preset", () => {
    render(<Harness initial="*/30 * * * *" />)

    // `Custom…` is the row that owns the off-preset values.
    expect(preset("Custom…").checked).toBe(true)
    // The custom editor is open — five selects, one per cron field. The
    // first option on each list is `*`, so a wire like `*/30 * * * *`
    // selects `*` for the four wildcard fields and falls back to "" on
    // the minute, which has no `*/N` shape — the editor surfaces it as
    // the empty row rather than silently dropping the value.
    expect(cronValue("cron-minute")).toBe("")
    expect(cronValue("cron-hour")).toBe("*")
    expect(cronValue("cron-day")).toBe("*")
    expect(cronValue("cron-month")).toBe("*")
    expect(cronValue("cron-weekday")).toBe("*")
  })
})

describe("the custom editor composes the cron string live", () => {
  it("reflects the value back into its five selects", () => {
    render(<Harness initial="15 6 1 * 1" />)

    // `15 6 1 * 1` is not a preset, so the harness lands in custom mode.
    expect(preset("Custom…").checked).toBe(true)

    expect(cronValue("cron-minute")).toBe("15")
    expect(cronValue("cron-hour")).toBe("6")
    expect(cronValue("cron-day")).toBe("1")
    expect(cronValue("cron-month")).toBe("*")
    expect(cronValue("cron-weekday")).toBe("1")
  })

  it("writes the new wire when one of the five selects changes", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness initial="0 3 * * *" onChange={onChange} />)

    // `0 3 * * *` is a preset; switch to the custom editor to expose the
    // five selects. The value the editor reflects is the preset itself.
    await user.click(preset("Custom…"))
    act(() => {
      setSelectValue(cronTrigger("cron-minute"), "30")
    })

    expect(onChange).toHaveBeenLastCalledWith("30 3 * * *")
  })

  it("preserves the other four fields when one changes", () => {
    const onChange = vi.fn()
    render(<Harness initial="15 6 1 * 1" onChange={onChange} />)

    act(() => {
      setSelectValue(cronTrigger("cron-weekday"), "5")
    })

    expect(onChange).toHaveBeenLastCalledWith("15 6 1 * 5")
  })

  it("writes through `onValueChange` once per operator action, not per field", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness initial="0 3 * * *" onChange={onChange} />)

    await user.click(preset("Custom…"))
    act(() => {
      setSelectValue(cronTrigger("cron-minute"), "30")
    })

    // One change fires one write — the cron string, not five.
    expect(onChange).toHaveBeenCalledTimes(1)
  })
})

describe("switching modes keeps the value intact", () => {
  it("Custom… opens the editor with the default five fields", async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)

    await user.click(preset("Custom…"))
    // The first action was switching to custom mode — the value did not
    // change. The cron string the field holds is still the empty wire.
    expect(onChange).not.toHaveBeenCalled()

    // The custom editor reflects the empty wire as the default five fields.
    expect(cronValue("cron-minute")).toBe("0")
    expect(cronValue("cron-hour")).toBe("3")
  })
})

/* The five selects and the preview line must agree on first render.
 *
 * A new schedule lands with `value === ""` — the wire is empty. The five
 * selects key off `parseCron("")` and resolve to `DEFAULT_PARTS` ("0 3 * * *"),
 * but the preview used to render `value || "—"` and showed "—". The operator
 * saw five selects saying `0 3 * * *` and a preview saying "—" until they
 * clicked one, at which point `onValueChange` synced the wire and the
 * placeholder vanished. The fix: the preview reads the parsed wire, so all
 * three views (five selects, preview, and the value the field holds) say
 * the same thing from the very first render. */
describe("the five selects and the preview agree on first render", () => {
  /** The preview's text — the assembled cron string the field holds. */
  const preview = (): string => {
    // The cron preview carries `data-test="cron-preview"`, not the `data-testid`
    // React Testing Library looks for by default. `querySelector` reads the
    // exact attribute the kit ships.
    const node = document.querySelector(
      '[data-test="cron-preview"] code'
    ) as HTMLElement | null
    return node?.textContent ?? ""
  }

  it("shows the default wire in the preview when the field is empty", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    // Open the custom editor to expose the five selects and the preview.
    await user.click(preset("Custom…"))

    // The five selects already key off the parsed defaults. The preview now
    // does too — no more "—" placeholder on first render.
    expect(preview()).toBe("0 3 * * *")
    expect(cronValue("cron-minute")).toBe("0")
    expect(cronValue("cron-hour")).toBe("3")
    expect(cronValue("cron-day")).toBe("*")
    expect(cronValue("cron-month")).toBe("*")
    expect(cronValue("cron-weekday")).toBe("*")
  })

  it("keeps the preview and the selects in sync as the operator edits", async () => {
    const user = userEvent.setup()
    render(<Harness />)

    await user.click(preset("Custom…"))
    expect(preview()).toBe("0 3 * * *")

    act(() => {
      setSelectValue(cronTrigger("cron-minute"), "30")
    })

    // One change moves the preview with it — no longer desynced from the
    // selects on first render, and the preview tracks every move after.
    expect(preview()).toBe("30 3 * * *")
    expect(cronValue("cron-minute")).toBe("30")
  })
})

describe("disabled is the same voice every other disabled field wears", () => {
  it("refuses the preset rows and the custom selects", () => {
    render(<Harness initial="0 3 * * 1" disabled />)

    expect(preset("Hourly").disabled).toBe(true)
    expect(preset("Weekly Monday").disabled).toBe(true)
  })
})

// Suppress the lint rule for the `within` import — kept for future tests
// that may want to assert against the popover (currently the custom editor
// tests write through the hidden native select, not the popover).
void within