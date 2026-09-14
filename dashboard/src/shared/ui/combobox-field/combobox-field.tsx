import { ChevronDown } from "lucide-react"
import type { ReactNode } from "react"
import {
  Button as AriaButton,
  ComboBox as AriaComboBox,
  FieldError,
  Input as AriaInput,
  Label,
  ListBox,
  ListBoxItem,
  Popover,
  useFilter,
} from "react-aria-components"

import { Field } from "@/shared/ui/form/field"
import { fieldDescriptionId, fieldLabelId } from "@/shared/ui/form/ids"
import { cn } from "@/shared/lib/utils"

import styles from "./combobox-field.module.css"

export interface ComboboxFieldOption {
  value: string
  label: string
  /** A second line under the label — model id + vendor, role + description. */
  hint?: string
}

export interface ComboboxFieldProps {
  id: string
  label: string
  /** The label is real but not drawn — see `FieldProps.labelHidden`. */
  labelHidden?: boolean
  value: string
  onValueChange: (next: string) => void
  /**
   * Everything this field may be, and nothing else.
   *
   * A closed list is the point: a model id is what a virtual key has access to,
   * and a value the platform does not know is a value the platform cannot
   * budget. `allowsCustomValue` is the way to soften the boundary — see below
   * — but the option set itself is the list.
   */
  options: readonly ComboboxFieldOption[]
  /** The words on the trigger before anything has been chosen. */
  placeholder?: string
  hint?: ReactNode
  error?: string | null
  /**
   * `md` stands at the form control step, the height every `TextField` in the
   * product stands at, because the two sit side by side in every form. `sm`
   * is the toolbar's denser 1.5rem.
   */
  size?: "sm" | "md"
  disabled?: boolean
  /**
   * Whether the operator may type a value that is not in the option list.
   *
   * Defaults to `false` — a closed list is the point — but a model id is a
   * value that grows as new releases ship, and refusing to type a brand-new
   * name until it has been catalogued is the wrong gesture. With it on, the
   * combobox still suggests; what it no longer forbids is a name not yet in
   * the suggestions.
   */
  allowsCustomValue?: boolean
  /**
   * Optional render override for an option's body. The default is one line of
   * label; the override is for the cases where an option carries a second
   * reading — a model's vendor, a role's description.
   */
  renderOption?: (option: ComboboxFieldOption) => ReactNode
  "data-test"?: string
}

/**
 * A labelled combobox — type to filter a closed list, or accept what is not in
 * the list yet.
 *
 * ## Why this is a control of its own, not a `Select` in a longer coat
 *
 * `SelectField` and `ComboboxField` end up at the same height with the same
 * hairline, because they sit on the same kind of line — a value chosen out of a
 * set — and the rest of the form has to keep one rhythm. What they are *not*
 * is the same control:
 *
 * - A **select** holds the value, and the operator opens the list only when
 *   they have forgotten what the choices are. The reading is "current value",
 *   the list is a memory aid.
 * - A **combobox** holds a string the operator types; the list is a filter, not
 *   a menu. The reading is "the value being typed right now", the list is a
 *   shortcut for the ones already known.
 *
 * A model id is the second case — there is no keyboard on the planet that can
 * pick `lead-xl-2` out of five rows by arrow, but typing `l-e-a-d` lands on it
 * without the list ever being opened. The two are different gestures; the kit
 * keeps them as different components.
 *
 * ## `allowsCustomValue`, and why the default is closed
 *
 * The option list is closed by default: a model that is not in the list is a
 * model the platform cannot route, and the field is not the place to learn that.
 * `allowsCustomValue` widens the seam — for the model id use case specifically,
 * where the list is what the platform shipped last week and the value the
 * operator wants is what shipped this week. With it on, the field still
 * suggests; it just refuses to forbid the value.
 *
 * ## Same surface, same control
 *
 * The popover below is the same surface the `Select` opens: one border, one
 * shadow, the same `--surface-raised` token, the same rows at the same height.
 * A form with a combobox next to a select reads as one form, because the two
 * are two spellings of one control.
 */
export function ComboboxField({
  id,
  label,
  labelHidden,
  value,
  onValueChange,
  options,
  placeholder,
  hint,
  error,
  size = "md",
  disabled = false,
  allowsCustomValue = false,
  renderOption,
  "data-test": dataTest,
}: ComboboxFieldProps) {
  const filter = useFilter({ sensitivity: "base" })

  return (
    <Field
      id={id}
      label={label}
      labelHidden={labelHidden}
      hint={hint}
      error={error}
    >
      <AriaComboBox
        className={cn(styles.root, size === "sm" && styles.sm)}
        id={id}
        isDisabled={disabled}
        allowsCustomValue={allowsCustomValue}
        selectedKey={value === "" ? null : value}
        inputValue={value}
        /* The empty mark lives on the root so the chevron's CSS can read
           it: a populated field's chevron stands at the value's own voice,
           and only an empty field stays muted — the same read `SelectField`
           gives the placeholder. The presence of the attribute is the
           signal, the value is decorative. */
        data-empty={value === "" ? "" : undefined}
        onSelectionChange={(key) => {
          // A `null` here means one of two very different things, and the
          // difference is `allowsCustomValue`:
          //
          // - **closed seam** — the typed text did not match any option,
          //   and React Aria fired `onSelectionChange(null)` to revert the
          //   selection. The platform has rejected the value, so the field
          //   should clear it.
          // - **open seam** — the operator typed a value the list does not
          //   know and pressed Enter; React Aria's `commitCustomValue` set
          //   the internal selection to `null` but left `inputValue` alone,
          //   because the typed text is now the value. Wiping here would
          //   race the typing and clobber what the operator just committed.
          if (key !== null) {
            onValueChange(String(key))
          } else if (!allowsCustomValue) {
            onValueChange("")
          }
        }}
        onInputChange={(next) => {
          onValueChange(next)
        }}
        aria-labelledby={fieldLabelId(id)}
        aria-describedby={hint || error ? fieldDescriptionId(id) : undefined}
        data-test={dataTest}
        defaultFilter={filter.contains}
      >
        <Label className={styles.srOnly} id={fieldLabelId(id)}>
          {label}
        </Label>
        <AriaInput
          className={cn(styles.input, size === "sm" && styles.inputSm)}
          placeholder={placeholder}
        />
        <AriaButton className={styles.trigger} aria-label="open list">
          <ChevronDown className={styles.icon} aria-hidden="true" />
        </AriaButton>
        {error ? (
          <FieldError className={styles.srOnly}>{error}</FieldError>
        ) : null}
        <Popover className={styles.popover} placement="bottom start">
          <ListBox className={styles.listBox}>
            {options.map((option) => (
              <ListBoxItem
                key={option.value}
                id={option.value}
                textValue={option.label}
                className={styles.option}
              >
                {renderOption ? (
                  renderOption(option)
                ) : option.hint ? (
                  <span className={styles.optionBody}>
                    <span className={styles.optionLabel}>{option.label}</span>
                    <span className={styles.optionHint}>{option.hint}</span>
                  </span>
                ) : (
                  option.label
                )}
              </ListBoxItem>
            ))}
          </ListBox>
        </Popover>
      </AriaComboBox>
    </Field>
  )
}