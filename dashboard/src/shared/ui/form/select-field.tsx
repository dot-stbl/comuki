import type { ReactNode } from "react"

import { Select, type SelectOption } from "../select"

import { Field } from "./field"
import { fieldDescriptionId, fieldLabelId } from "./ids"

export type SelectFieldOption = SelectOption

export interface SelectFieldProps {
  id: string
  label: string
  value: string
  onValueChange: (next: string) => void
  /**
   * Everything this field may be, and nothing else.
   *
   * A closed list is the point on this screen: a connector can only speak the
   * credentials it implements, so the auth select is exactly the ones the
   * chosen provider offers and there is no affordance that could ask for a
   * seventh. There is deliberately no row that empties the field either — the
   * kit's `clearable` belongs to a filter, where "all" is a real answer.
   */
  options: readonly SelectFieldOption[]
  /** The words on the trigger before anything has been chosen. */
  placeholder?: string
  hint?: ReactNode
  error?: string | null
  disabled?: boolean
  "data-test"?: string
}

/**
 * A labelled select, on the kit's own control.
 *
 * This used to be a native `<select>`, and the argument for it was a decent
 * one: a form visited once a month gets the platform's keyboard, screen reader
 * and touch picker for free, and a list of values does not need reimplementing
 * to be one. What that argument left out is that the product already had a
 * second select — the data table's toolbar builds a React Aria listbox for its
 * filter row — so a form and a toolbar on the same screen wore different
 * controls, and the form's was the one that looked like the operating system.
 * Two spellings of one control is the more expensive defect, so the decision
 * was reversed: `shared/ui/select` is the one select, and this is the field
 * that puts a label, a hint and an error around it.
 *
 * What the platform used to give for free is now bought and tested — keyboard,
 * type-ahead, an accessible name, a touch-sized list, a hidden native select
 * for autofill and form submission. The argument, and what was checked, is on
 * the primitive itself.
 *
 * The one wiring detail that lives here: React Aria composes the trigger's
 * accessible name from its own value node, which *replaces* the `<label for>`
 * association, so the label's id is handed to the control explicitly. That is
 * why `Field` stamps `fieldLabelId(id)` on its label.
 */
export function SelectField({
  id,
  label,
  value,
  onValueChange,
  options,
  placeholder,
  hint,
  error,
  disabled,
  "data-test": dataTest,
}: SelectFieldProps) {
  return (
    <Field id={id} label={label} hint={hint} error={error}>
      <Select
        id={id}
        value={value}
        onValueChange={onValueChange}
        options={options}
        placeholder={placeholder}
        disabled={disabled}
        invalid={error ? true : undefined}
        aria-labelledby={fieldLabelId(id)}
        aria-describedby={hint || error ? fieldDescriptionId(id) : undefined}
        data-test={dataTest}
      />
    </Field>
  )
}
