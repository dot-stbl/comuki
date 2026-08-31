import { ChevronDown } from "lucide-react"
import {
  Button as AriaButton,
  Select as AriaSelect,
  ListBox,
  ListBoxItem,
  Popover,
  SelectValue,
} from "react-aria-components"

import { cn } from "@/shared/lib/utils"

import styles from "./select.module.css"

/**
 * React Aria keys cannot be the empty string, so the row that puts a value back
 * to "nothing chosen" needs a token of its own. It never leaves the kit:
 * `value` and `onValueChange` speak in the product's own strings, and the only
 * other reader is `test-select.ts`, which has to know the row is not a value.
 */
export const CLEAR_KEY = "__clear"

export interface SelectOption {
  value: string
  label: string
}

export interface SelectProps {
  /**
   * The id a `<label for>` points at. It lands on the trigger, which is a
   * `<button>` and therefore a labelable element — so the label names the
   * control it opens, and clicking the label opens it.
   */
  id?: string
  /** The chosen value. `""` means nothing is chosen. */
  value: string
  onValueChange: (next: string) => void
  /** Everything this control may be, in the order it should be read. */
  options: readonly SelectOption[]
  /**
   * The words on the trigger when nothing is chosen — and, when `clearable`,
   * the words on the row that clears it. One prop, read twice, because they
   * are the same sentence said in two places: "all apps" is both the state and
   * the way back to it.
   */
  placeholder?: string
  /**
   * Adds a first row that sets the value back to `""`.
   *
   * A filter has one — narrowing to nothing is a real state an operator has to
   * be able to get back to. A form's select does not: a closed list is the
   * point on a form, and a row that empties a required field is an affordance
   * for a mistake.
   */
  clearable?: boolean
  /**
   * `md` stands at the form control step, the height every `TextField` in the
   * product stands at, because the two sit side by side in every form. `sm` is
   * the toolbar's denser 1.5rem — WCAG 2.2's minimum target, and no lower.
   */
  size?: "sm" | "md"
  disabled?: boolean
  /**
   * A control that is currently doing something. It marks itself with its own
   * border and its own text weight — never with a coloured fill.
   */
  active?: boolean
  /** Something is wrong with the value. Draws the rule in the failure hue. */
  invalid?: boolean
  /** Name the control. One of these two is required — see the note below. */
  "aria-label"?: string
  "aria-labelledby"?: string
  /** The hint or the error under the control, whichever is showing. */
  "aria-describedby"?: string
  className?: string
  "data-test"?: string
}

/**
 * The product's select. There is one.
 *
 * There used to be two. `SelectField` rendered a native `<select>` and argued
 * for it — the platform hands you the keyboard, the screen reader and the
 * touch picker for free — while the data table's toolbar built a React Aria
 * listbox for its filter row, because a filter needs an "all" state, a denser
 * height and an option list that reads like the rest of the chrome. Both were
 * defensible on their own and indefensible together: a form and a toolbar on
 * the same screen wore different controls, and the form's looked like the
 * operating system rather than like Comuki.
 *
 * So this is the React Aria one, promoted, and the differences between the two
 * call sites are props on it rather than a second component: `size` for the
 * toolbar's density, `clearable` for its "all" row, `active` for the mark a
 * filter wears when it is doing something.
 *
 * ## What the native control gave away, and what this had to earn back
 *
 * Every one of these is a behaviour `<select>` had for free, so every one is
 * covered by a case in `select.test.tsx` rather than taken on trust:
 *
 * - **Keyboard.** The trigger opens on `Enter`, `Space`, `ArrowDown` and
 *   `ArrowUp`; the open list moves on the arrows, `Home` and `End`, commits on
 *   `Enter` and abandons on `Escape`.
 * - **Type-ahead, both closed and open.** React Aria runs `useTypeSelect` on
 *   the *trigger*, so typing `b` with the control focused and shut moves
 *   straight to the first option starting with `b` without opening anything —
 *   which is exactly what the native control did.
 * - **An accessible name.** React Aria points the trigger's `aria-labelledby`
 *   at its own value node, and that *replaces* any `<label for>` association
 *   for name computation. So a name has to be given: pass `aria-labelledby`
 *   (the id of the label element beside it) or `aria-label`. The composed name
 *   ends up "value, label", which is the listbox pattern's own order.
 * - **Touch.** The list is a popover with real rows at the control height, not
 *   a hover menu: it opens on press, it is reachable with a thumb, and it
 *   closes on an outside press. The one thing the platform picker gave that no
 *   web control can is the OS wheel, and that is the trade the promotion makes
 *   knowingly.
 * - **Forms and autofill.** React Aria keeps a visually-hidden native
 *   `<select>` in the DOM for exactly this, so a browser can still autofill
 *   the control and a `<form>` can still submit it.
 *
 * ## Stated limit
 *
 * The trigger carries `data-invalid` rather than `aria-invalid`: React Aria's
 * `Button` filters unknown ARIA attributes off the element and offers no seam
 * for that one. The reading survives — the error text is `role="alert"` and
 * the trigger points `aria-describedby` at it — but the state is not on the
 * control itself, and that is a gap rather than a decision.
 */
export function Select({
  id,
  value,
  onValueChange,
  options,
  placeholder,
  clearable = false,
  size = "md",
  disabled = false,
  active = false,
  invalid = false,
  className,
  "aria-label": ariaLabel,
  "aria-labelledby": ariaLabelledBy,
  "aria-describedby": ariaDescribedBy,
  "data-test": dataTest,
}: SelectProps) {
  const empty = value === ""
  // Nothing chosen is either a real row (a filter's "all") or no selection at
  // all (a form's placeholder). React Aria spells the second one `null`.
  const selectedKey = empty ? (clearable ? CLEAR_KEY : null) : value
  const clearLabel = placeholder ?? "any"

  return (
    <AriaSelect
      className={cn(styles.root, className)}
      id={id}
      isDisabled={disabled}
      placeholder={placeholder}
      selectedKey={selectedKey}
      aria-label={ariaLabel}
      aria-labelledby={ariaLabelledBy}
      aria-describedby={ariaDescribedBy}
      onSelectionChange={(key) => {
        onValueChange(key === CLEAR_KEY || key === null ? "" : String(key))
      }}
    >
      <AriaButton
        className={cn(styles.trigger, size === "sm" && styles.sm)}
        data-test={dataTest}
        data-active={active ? "" : undefined}
        data-invalid={invalid ? "" : undefined}
        data-empty={empty ? "" : undefined}
      >
        <SelectValue className={styles.value} />
        <ChevronDown className={styles.icon} aria-hidden="true" />
      </AriaButton>
      <Popover className={styles.popover} placement="bottom start">
        <ListBox className={styles.listBox}>
          {clearable ? (
            <ListBoxItem
              id={CLEAR_KEY}
              className={styles.option}
              textValue={clearLabel}
            >
              {clearLabel}
            </ListBoxItem>
          ) : null}
          {options.map((option) => (
            <ListBoxItem
              key={option.value}
              id={option.value}
              className={styles.option}
              textValue={option.label}
            >
              {option.label}
            </ListBoxItem>
          ))}
        </ListBox>
      </Popover>
    </AriaSelect>
  )
}
