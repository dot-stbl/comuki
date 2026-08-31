import { screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"

import { CLEAR_KEY } from "./select"

/**
 * How a test drives the kit's `Select`.
 *
 * A native `<select>` was its own test seam: `fireEvent.change` set it and
 * `.options` read it, synchronously, from the element `getByLabelText` handed
 * back. A listbox-based control has neither, and rewriting every form test in
 * the product into an async open-then-press dance would have changed what most
 * of them are about — they are about which values a form offers and what it
 * does with the one that was chosen, not about how a popover opens.
 *
 * So the seam is the control's own form element. React Aria keeps a
 * visually-hidden native `<select>` beside the trigger, carrying every option,
 * so a browser can autofill the control and a `<form>` can submit it; a change
 * event on it is the same state write the popover performs. Reading and
 * writing through it is therefore driving the real control, not a mock of it —
 * and it works while the trigger is disabled or the list is empty, which is
 * exactly where a popover-driven helper cannot go.
 *
 * `pickOption` is the other half: the operator's own path, open the list and
 * press a row. `select.test.tsx` uses it to prove the two agree.
 *
 * Every function here takes the *trigger* — the element `getByLabelText(label)`
 * returns, because the field's label points at the control it opens.
 */

/** React Aria's hidden native select, from the trigger beside it. */
export function nativeSelect(trigger: HTMLElement): HTMLSelectElement {
  const found = trigger.parentElement?.querySelector("select")
  if (!found) {
    throw new Error(
      "no native select beside this trigger — is it a kit `Select`?"
    )
  }
  return found
}

/**
 * The values the control offers, in order.
 *
 * Two rows are dropped because neither is one of the control's own values and
 * neither ever reaches `onValueChange`: React Aria's blank option, which
 * stands for "nothing chosen", and a `clearable` select's own clear row.
 */
export function selectValues(trigger: HTMLElement): string[] {
  return [...nativeSelect(trigger).options]
    .map((option) => option.value)
    .filter((value) => value !== "" && value !== CLEAR_KEY)
}

/** The value the control currently holds. */
export function selectedValue(trigger: HTMLElement): string {
  return nativeSelect(trigger).value
}

/** Set the value, through the control's own form element. */
export function setSelectValue(trigger: HTMLElement, value: string): void {
  const select = nativeSelect(trigger)
  // A clearable select spells "nothing chosen" as its own row rather than as
  // the blank option, so write the row when there is one.
  if (value === "" && select.querySelector(`option[value="${CLEAR_KEY}"]`)) {
    value = CLEAR_KEY
  }
  const setter = Object.getOwnPropertyDescriptor(
    HTMLSelectElement.prototype,
    "value"
  )?.set
  setter?.call(select, value)
  select.dispatchEvent(new Event("change", { bubbles: true }))
}

/**
 * Choose an option the way an operator does: open the list, press the row.
 *
 * The list is a popover and renders into a portal, so it is found on `screen`
 * rather than inside any container the test rendered.
 */
export async function pickOption(
  trigger: HTMLElement,
  label: string | RegExp
): Promise<void> {
  const user = userEvent.setup()
  await user.click(trigger)
  const list = await screen.findByRole("listbox")
  await user.click(within(list).getByRole("option", { name: label }))
}
