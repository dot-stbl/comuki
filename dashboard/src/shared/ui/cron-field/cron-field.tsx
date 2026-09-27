import { useMemo, useState, type ReactNode } from "react"
import { useTranslation } from "react-i18next"

import { ChoiceField } from "@/shared/ui/form/choice-field"
import { Field } from "@/shared/ui/form/field"
import { fieldDescriptionId, fieldLabelId } from "@/shared/ui/form/ids"
import { Select } from "@/shared/ui/select"

import styles from "./cron-field.module.css"

/** The translator the cron words resolve through — see `data-table`'s note. */
type CronTranslator = (key: string, options?: Record<string, unknown>) => string

/** `*` plus 0…59. The numbers are wire values; only "any" is a word. */
function minuteOptions(t: CronTranslator) {
  return [
    { value: "*", label: t("cron.any") },
    ...Array.from({ length: 60 }, (_, i) => ({
      value: i.toString(),
      label: i.toString(),
    })),
  ]
}

function hourOptions(t: CronTranslator) {
  return [
    { value: "*", label: t("cron.any") },
    ...Array.from({ length: 24 }, (_, i) => ({
      value: i.toString(),
      label: i.toString(),
    })),
  ]
}

function dayOfMonthOptions(t: CronTranslator) {
  return [
    { value: "*", label: t("cron.everyDay") },
    ...Array.from({ length: 31 }, (_, i) => ({
      value: (i + 1).toString(),
      label: (i + 1).toString(),
    })),
  ]
}

function monthOptions(t: CronTranslator) {
  return [
    { value: "*", label: t("cron.any") },
    ...Array.from({ length: 12 }, (_, i) => ({
      value: (i + 1).toString(),
      label: (i + 1).toString(),
    })),
  ]
}

/** Weekday keys in cron order — 0 is Sunday. */
const WEEKDAYS = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"] as const

function dayOfWeekOptions(t: CronTranslator) {
  return [
    { value: "*", label: t("cron.anyDay") },
    ...WEEKDAYS.map((day, index) => ({
      value: index.toString(),
      label: t(`cron.weekday.${day}`),
    })),
  ]
}

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
}

const PRESETS: CronPreset[] = [
  { value: "0 * * * *" },
  { value: "0 3 * * *" },
  { value: "0 3 * * 1" },
  { value: "0 3 1 * *" },
]

/** Catalogue key per preset, in `PRESETS` order. */
const PRESET_KEYS = ["hourly", "daily", "weekly", "monthly"] as const

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
  const { t } = useTranslation("kit")

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
        label={t("cron.cadence")}
        value={presetSelection}
        onValueChange={pickPreset}
        disabled={disabled}
        options={[
          ...PRESETS.map((preset, index) => ({
            value: preset.value,
            label: t(`cron.preset.${PRESET_KEYS[index]}`),
            description: t(`cron.preset.${PRESET_KEYS[index]}Description`),
          })),
          {
            value: CUSTOM_VALUE,
            label: t("cron.custom"),
            description: t("cron.customDescription"),
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
  const { t } = useTranslation("kit")

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
        label={t("cron.part.minute")}
        value={minute}
        options={minuteOptions(t)}
        disabled={disabled}
        onValueChange={(next) =>
          update([next, hour, dayOfMonth, month, dayOfWeek])
        }
      />
      <CronSelect
        id={`${id}-hour`}
        label={t("cron.part.hour")}
        value={hour}
        options={hourOptions(t)}
        disabled={disabled}
        onValueChange={(next) =>
          update([minute, next, dayOfMonth, month, dayOfWeek])
        }
      />
      <CronSelect
        id={`${id}-day`}
        label={t("cron.part.day")}
        value={dayOfMonth}
        options={dayOfMonthOptions(t)}
        disabled={disabled}
        onValueChange={(next) => update([minute, hour, next, month, dayOfWeek])}
      />
      <CronSelect
        id={`${id}-month`}
        label={t("cron.part.month")}
        value={month}
        options={monthOptions(t)}
        disabled={disabled}
        onValueChange={(next) =>
          update([minute, hour, dayOfMonth, next, dayOfWeek])
        }
      />
      <CronSelect
        id={`${id}-weekday`}
        label={t("cron.part.weekday")}
        value={dayOfWeek}
        options={dayOfWeekOptions(t)}
        disabled={disabled}
        onValueChange={(next) =>
          update([minute, hour, dayOfMonth, month, next])
        }
      />
      <span className={styles.preview} data-test={`${id}-preview`}>
        <span className={styles.previewLabel}>{t("cron.value")}</span>
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
