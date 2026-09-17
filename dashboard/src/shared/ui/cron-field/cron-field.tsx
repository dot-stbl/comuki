import { useMemo, useState, type ReactNode } from "react"

import { ChoiceField } from "@/shared/ui/form/choice-field"
import { Field } from "@/shared/ui/form/field"
import { fieldDescriptionId, fieldLabelId } from "@/shared/ui/form/ids"
import { Select } from "@/shared/ui/select"

import styles from "./cron-field.module.css"

const MINUTE_OPTIONS = [
  { value: "*", label: "any" },
  ...Array.from({ length: 60 }, (_, i) => ({
    value: i.toString(),
    label: i.toString(),
  })),
]
const HOUR_OPTIONS = [
  { value: "*", label: "any" },
  ...Array.from({ length: 24 }, (_, i) => ({
    value: i.toString(),
    label: i.toString(),
  })),
]
const DAY_OF_MONTH_OPTIONS = [
  { value: "*", label: "every day" },
  ...Array.from({ length: 31 }, (_, i) => ({
    value: (i + 1).toString(),
    label: (i + 1).toString(),
  })),
]
const MONTH_OPTIONS = [
  { value: "*", label: "any" },
  ...Array.from({ length: 12 }, (_, i) => ({
    value: (i + 1).toString(),
    label: (i + 1).toString(),
  })),
]
const DAY_OF_WEEK_OPTIONS = [
  { value: "*", label: "any day" },
  { value: "0", label: "Sun" },
  { value: "1", label: "Mon" },
  { value: "2", label: "Tue" },
  { value: "3", label: "Wed" },
  { value: "4", label: "Thu" },
  { value: "5", label: "Fri" },
  { value: "6", label: "Sat" },
]

export interface CronFieldProps {
  id: string
  label: string
  /** The label is real but not drawn — see `FieldProps.labelHidden`. */
  labelHidden?: boolean
  /** The cron expression, in five fields: `minute hour day-of-month month day-of-week`. */
  value: string
  onValueChange: (next: string) => void
  /**
   * The form will not go without this field — see `FieldProps.required`.
   *
   * It reaches `Field` and nothing else, for the reason `ComboboxField`'s own
   * `required` spells out: the word in the label is the channel, and React
   * Aria's `isRequired` would bring the browser's native constraint bubble
   * along with it.
   */
  required?: boolean

  hint?: ReactNode
  error?: string | null
  disabled?: boolean
  "data-test"?: string
}

/**
 * A five-field cron entry, dressed as presets the operator reaches for first.
 *
 * The wire form is the standard five fields — `0 3 STAR STAR STAR`,
 * `STAR/30 STAR STAR STAR STAR`, every hour on the half — but the operator
 * does not arrive at the platform fluent in cron. The presets cover the four
 * cadences that actually exist in the product: hourly, daily at 03:00,
 * weekly Monday, monthly on the first, plus a `Custom…` reveal that drops
 * the operator into the five-field editor. Whatever the operator does, the
 * value the field writes is the cron string the schedule engine stores —
 * presets and the custom editor speak the same wire format, and switching
 * from one to the other keeps the value intact.
 *
 * What the field hides — six-field cron (with the seconds field the platform
 * does not use) and a free-form text box. The wire is five fields; an extra
 * text box would let the operator type something the platform cannot parse
 * and quietly store it. The presets are the whole vocabulary the product
 * already uses, and the custom editor is the rest of the wire — no escape
 * hatch into a string the schedule engine cannot read.
 */
export function CronField({
  id,
  label,
  labelHidden,
  value,
  onValueChange,
  required,
  hint,
  error,
  disabled = false,
  "data-test": dataTest,
}: CronFieldProps) {
  return (
    <Field
      id={id}
      label={label}
      labelHidden={labelHidden}
      required={required}
      hint={hint}
      error={error}
    >
      <CronControl
        id={id}
        value={value}
        onValueChange={onValueChange}
        disabled={disabled}
        data-test={dataTest}
      />
    </Field>
  )
}

interface CronPreset {
  value: string
  label: string
  description: string
}

const PRESETS: CronPreset[] = [
  {
    value: "0 * * * *",
    label: "Hourly",
    description: "Every hour, on the hour.",
  },
  {
    value: "0 3 * * *",
    label: "Daily at 03:00",
    description: "Once a day, in the project's quiet window.",
  },
  {
    value: "0 3 * * 1",
    label: "Weekly Monday",
    description: "Mondays at 03:00.",
  },
  {
    value: "0 3 1 * *",
    label: "Monthly 1st",
    description: "First of the month at 03:00.",
  },
]

const CUSTOM_VALUE = "__custom"

function CronControl({
  id,
  value,
  onValueChange,
  disabled,
  "data-test": dataTest,
}: {
  id: string
  value: string
  onValueChange: (next: string) => void
  disabled: boolean
  "data-test"?: string
}) {
  // A value the operator typed freehand — neither a preset nor in the
  // custom editor — defaults to custom view so it is editable rather than
  // silently truncated to a preset.
  const isPreset = PRESETS.some((preset) => preset.value === value)
  const [mode, setMode] = useState<"preset" | "custom">(
    isPreset ? "preset" : "custom"
  )

  // The preset row's checked-state is one of the four presets or `Custom…`
  // — whichever is in force. An empty wire and an off-preset value both
  // surface as `Custom…`, so the operator sees the row they are on.
  const presetSelection = isPreset
    ? value
    : mode === "preset"
      ? ""
      : CUSTOM_VALUE

  function pickPreset(next: string): void {
    if (next === CUSTOM_VALUE) {
      setMode("custom")
      return
    }
    setMode("preset")
    onValueChange(next)
  }

  return (
    <div className={styles.stack} data-test={dataTest}>
      <ChoiceField
        name={`${id}-preset`}
        label="cadence"
        value={presetSelection}
        onValueChange={pickPreset}
        disabled={disabled}
        options={[
          ...PRESETS.map((preset) => ({
            value: preset.value,
            label: preset.label,
            description: preset.description,
          })),
          {
            value: CUSTOM_VALUE,
            label: "Custom…",
            description: "Pick the minute, hour, day, month, weekday.",
          },
        ]}
      />
      {mode === "custom" ? (
        <CustomCronEditor
          id={id}
          value={value}
          onValueChange={onValueChange}
          disabled={disabled}
        />
      ) : null}
    </div>
  )
}

function CustomCronEditor({
  id,
  value,
  onValueChange,
  disabled,
}: {
  id: string
  value: string
  onValueChange: (next: string) => void
  disabled: boolean
}) {
  // `value` may be empty on first render (a brand-new schedule) — key the
  // five selects and the preview off `DEFAULT_VALUE` in that case so all
  // three views agree on first paint. The same wire `Daily at 03:00` writes.
  const parsed = useMemo(() => parseCron(value || DEFAULT_VALUE), [value])
  const [minute, hour, dayOfMonth, month, dayOfWeek] = parsed

  function update(next: CronParts): void {
    onValueChange(next.join(" "))
  }

  return (
    <div
      className={styles.editor}
      aria-labelledby={fieldLabelId(id)}
      aria-describedby={fieldDescriptionId(id)}
    >
      <CronSelect
        id={`${id}-minute`}
        label="minute"
        value={minute}
        options={MINUTE_OPTIONS}
        disabled={disabled}
        onValueChange={(next) =>
          update([next, hour, dayOfMonth, month, dayOfWeek])
        }
      />
      <CronSelect
        id={`${id}-hour`}
        label="hour"
        value={hour}
        options={HOUR_OPTIONS}
        disabled={disabled}
        onValueChange={(next) =>
          update([minute, next, dayOfMonth, month, dayOfWeek])
        }
      />
      <CronSelect
        id={`${id}-day`}
        label="day"
        value={dayOfMonth}
        options={DAY_OF_MONTH_OPTIONS}
        disabled={disabled}
        onValueChange={(next) => update([minute, hour, next, month, dayOfWeek])}
      />
      <CronSelect
        id={`${id}-month`}
        label="month"
        value={month}
        options={MONTH_OPTIONS}
        disabled={disabled}
        onValueChange={(next) =>
          update([minute, hour, dayOfMonth, next, dayOfWeek])
        }
      />
      <CronSelect
        id={`${id}-weekday`}
        label="weekday"
        value={dayOfWeek}
        options={DAY_OF_WEEK_OPTIONS}
        disabled={disabled}
        onValueChange={(next) =>
          update([minute, hour, dayOfMonth, month, next])
        }
      />
      <span className={styles.preview} data-test={`${id}-preview`}>
        <span className={styles.previewLabel}>value</span>
        <code className={styles.previewValue}>
          {/* The preview reads the parsed wire, not the raw `value` prop.
             A new schedule's wire is empty, the five selects key off
             `DEFAULT_PARTS`, and the preview used to show "—" while the
             selects already said `0 3 * * *` — the desync the operator saw
             on first render. Now all three say the same thing. */}
          {parsed.join(" ")}
        </code>
      </span>
    </div>
  )
}

interface CronSelectProps {
  id: string
  label: string
  value: string
  options: ReadonlyArray<{ value: string; label: string }>
  disabled: boolean
  onValueChange: (next: string) => void
}

function CronSelect({
  id,
  label,
  value,
  options,
  disabled,
  onValueChange,
}: CronSelectProps) {
  return (
    <div className={styles.cell}>
      <span className={styles.cellLabel} id={`${id}-label`}>
        {label}
      </span>
      <Select
        id={id}
        size="sm"
        value={value}
        options={options}
        disabled={disabled}
        aria-labelledby={`${id}-label`}
        onValueChange={onValueChange}
      />
    </div>
  )
}

type CronParts = [string, string, string, string, string]

/**
 * The cron string a brand-new schedule lands on.
 *
 * The five selects key off `parseCron(DEFAULT_VALUE)` when `value` is empty,
 * and the preview shows the same string. A new operator sees one value, not
 * five selects that disagree with a placeholder — the desync that happened
 * when the preview rendered `value || "—"` while the selects rendered the
 * parsed defaults.
 *
 * "0 3 * * *" is the same wire the `Daily at 03:00` preset writes — the one
 * the field's own description calls "the project's quiet window". The
 * default IS the preset; the two views agree because they are one value.
 */
const DEFAULT_VALUE = "0 3 * * *"

const DEFAULT_PARTS: CronParts = ["0", "3", "*", "*", "*"]

function parseCron(value: string): CronParts {
  const parts = value.trim().split(/\s+/)
  if (parts.length !== 5) {
    return DEFAULT_PARTS
  }
  return [parts[0], parts[1], parts[2], parts[3], parts[4]]
}
