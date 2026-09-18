import type { CalendarDate } from "@internationalized/date"
import { parseDate, today } from "@internationalized/date"
import {
  Button as AriaButton,
  Calendar as AriaCalendar,
  CalendarCell,
  CalendarGrid,
  CalendarGridBody,
  CalendarGridHeader,
  CalendarHeaderCell,
  CalendarHeading,
  DateInput,
  DatePicker as AriaDatePicker,
  DateRangePicker as AriaDateRangePicker,
  DateSegment,
  Dialog,
  Group,
  Popover,
  RangeCalendar,
} from "react-aria-components"
import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import { Field } from "../form"
import { fieldDescriptionId, fieldLabelId } from "../form/ids"

import styles from "./date-picker.module.css"

/**
 * The kit's date picker, on the field envelope.
 *
 * Two components for two related but distinct asks: a single day and a range
 * of two days. Both are React Aria `DatePicker` / `DateRangePicker` underneath,
 * which carry the date grammar (arrow keys step segments, page-up jumps month,
 * the calendar icon opens the popover) and the a11y semantics (a combobox role
 * for the trigger, a grid for the cells, a labelled heading for the month).
 * The kit owns the look — the trigger chrome that matches the rest of the
 * form kit, the popover that matches the select's overlay, the field envelope
 * that gives it a name.
 *
 * The value on the wire is an **ISO 8601 string** (`"2026-09-14"` for a day,
 * `{ start, end }` for a range). Internally the components hold
 * `@internationalized/date`'s `CalendarDate`, which has no time component and
 * no timezone — a day is a day, the calendar in the locale of the operator's
 * machine. `null` stands for "nothing chosen", and the empty value reads as
 * the placeholder the rest of the form kit uses.
 */
export interface DatePickerFieldProps {
  id: string
  label: string
  /**
   * The chosen day as ISO 8601 (`YYYY-MM-DD`), or `null` for nothing chosen.
   *
   * Stored as a string so the call site's value survives a `JSON.stringify`,
   * a URL parameter, a query parameter on a copy-and-paste, and a
   * `Zod` parse. The conversion to and from React Aria's `CalendarDate`
   * lives on the boundary of this primitive; call sites never touch it.
   */
  value: string | null
  /**
   * The next day. `null` when the operator clears the field. The calendar
   * grammar is owned by React Aria; this is the wire-format write.
   */
  onValueChange: (next: string | null) => void
  /**
   * The earliest day the operator may choose. ISO 8601 or omitted.
   *
   * The popover greys out days before `minValue`, the field rejects typed
   * dates before it, and `onValueChange` is not called with one. The same
   * value the calendar uses to clamp is the same value the form validates
   * against.
   */
  minValue?: string
  /** The latest day the operator may choose. Same contract as `minValue`. */
  maxValue?: string
  /**
   * The form will not go without this field — see `FieldProps.required`.
   *
   * It reaches `Field` and nothing else, for the reason `ComboboxField`'s own
   * `required` spells out: the word in the label is the channel, and React
   * Aria's `isRequired` would bring the browser's native constraint bubble
   * along with it.
   */
  required?: boolean

  hint?: React.ReactNode
  error?: string | null
  /** Busy or structurally impossible. Never a permission denial. */
  disabled?: boolean
  "data-test"?: string
  /** Optional className applied to the inner group root. */
  className?: string
}

const DATE_FORMAT = /^\d{4}-\d{2}-\d{2}$/

/**
 * Parse an ISO date string into a `CalendarDate`. Returns `null` for an
 * invalid input rather than throwing — a value that the call site stored
 * before a schema change should not crash the field, it should clear.
 */
function toCalendarDate(iso: string | null | undefined): CalendarDate | null {
  if (!iso) return null
  try {
    return parseDate(iso)
  } catch {
    return null
  }
}

function toIsoString(date: CalendarDate | null): string | null {
  return date ? date.toString() : null
}

/** Local `Button` for the calendar header — a slot wrapper around React Aria
 *  `Button`, kept as a separate component so the header markup above does
 *  not have to repeat the slot prop and class list. */
function NavButton({
  slot,
  children,
}: {
  slot: "previous" | "next"
  children: React.ReactNode
}) {
  return (
    <AriaButton slot={slot} className={styles.navButton}>
      {children}
    </AriaButton>
  )
}

/** Type guard: the wire string is a well-formed `YYYY-MM-DD`. */
export function isIsoDate(value: string): boolean {
  return DATE_FORMAT.test(value)
}

/** Today's ISO date, useful for stories and tests. */
export function todayIso(): string {
  return today("UTC").toString()
}

/** Add days to an ISO date string. Exported for stories and tests that
 *  want to construct a date a known offset from today. */
export function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

/**
 * The single-day date picker.
 *
 * A label, a control that opens a calendar, and one line under it. Same
 * shape every other field in the form kit wears — the envelope is `Field`,
 * the trigger is the same box as a `TextField`, the popover is the same
 * overlay as `Select`'s list, and the error replaces the hint the way
 * every other field's error does.
 */
export function DatePickerField({
  id,
  label,
  value,
  onValueChange,
  minValue,
  maxValue,
  required,
  hint,
  error,
  disabled = false,
  "data-test": dataTest,
  className,
}: DatePickerFieldProps) {
  const date = toCalendarDate(value)
  const min = toCalendarDate(minValue)
  const max = toCalendarDate(maxValue)
  const empty = date === null

  return (
    <Field id={id} label={label} required={required} hint={hint} error={error}>
      <AriaDatePicker
        className={cn(styles.group, className)}
        value={date}
        minValue={min ?? undefined}
        maxValue={max ?? undefined}
        isDisabled={disabled}
        granularity="day"
        aria-labelledby={fieldLabelId(id)}
        aria-describedby={hint || error ? fieldDescriptionId(id) : undefined}
        data-test={dataTest}
        onChange={(next) => {
          onValueChange(toIsoString(next))
        }}
      >
        <Group
          className={styles.trigger}
          data-empty={empty ? "" : undefined}
          data-invalid={error ? "" : undefined}
          data-disabled={disabled || undefined}
        >
          <DateInput>
            {(segment) =>
              segment.type === "literal" ? (
                <span className={styles.literal}>{segment.text}</span>
              ) : (
                <DateSegment
                  segment={segment}
                  className={cn(
                    styles.segment,
                    segment.isPlaceholder && styles.segmentPlaceholder
                  )}
                />
              )
            }
          </DateInput>
          {/* The calendar button is the operator's way in — pressing the
              field itself is reserved for editing the segments. */}
          <AriaButton
            className={styles.iconButton}
            aria-label="Open calendar"
            data-test={dataTest ? `${dataTest}-calendar` : undefined}
          >
            <CalendarDays className={styles.icon} aria-hidden="true" />
          </AriaButton>
        </Group>
        <Popover className={styles.popover} placement="bottom start">
          <Dialog className={styles.dialog}>
            <AriaCalendar className={styles.calendar}>
              <header className={styles.calendarHeader}>
                <NavButton slot="previous">
                  <ChevronLeft className={styles.navIcon} aria-hidden="true" />
                </NavButton>
                <CalendarHeading className={styles.calendarHeading} />
                <NavButton slot="next">
                  <ChevronRight className={styles.navIcon} aria-hidden="true" />
                </NavButton>
              </header>
              <CalendarGrid className={styles.calendarGrid}>
                <CalendarGridHeader className={styles.weekHeader}>
                  {(day) => (
                    <CalendarHeaderCell className={styles.weekDay}>
                      {day}
                    </CalendarHeaderCell>
                  )}
                </CalendarGridHeader>
                <CalendarGridBody className={styles.calendarGridBody}>
                  {(date) => (
                    <CalendarCell date={date} className={styles.day} />
                  )}
                </CalendarGridBody>
              </CalendarGrid>
            </AriaCalendar>
          </Dialog>
        </Popover>
      </AriaDatePicker>
    </Field>
  )
}

/**
 * The day-range picker. The same envelope, the same overlay, two inputs in
 * a single control with a dash between them. The wire format is
 * `{ start, end }`, where either side may be `null` while the operator is
 * still picking — the second side fills in when the calendar commits.
 */
export interface DateRangePickerFieldProps {
  id: string
  label: string
  /**
   * The chosen range as `{ start, end }`, ISO 8601 each. `null` on a side
   * means "not picked yet"; `null` for the whole range means "nothing
   * chosen".
   */
  value: { start: string | null; end: string | null } | null
  onValueChange: (
    next: { start: string | null; end: string | null } | null
  ) => void
  minValue?: string
  maxValue?: string
  /**
   * The form will not go without this field — see `FieldProps.required`.
   *
   * It reaches `Field` and nothing else, for the reason `ComboboxField`'s own
   * `required` spells out: the word in the label is the channel, and React
   * Aria's `isRequired` would bring the browser's native constraint bubble
   * along with it.
   */
  required?: boolean

  hint?: React.ReactNode
  error?: string | null
  disabled?: boolean
  "data-test"?: string
  className?: string
}

export function DateRangePickerField({
  id,
  label,
  value,
  onValueChange,
  minValue,
  maxValue,
  required,
  hint,
  error,
  disabled = false,
  "data-test": dataTest,
  className,
}: DateRangePickerFieldProps) {
  const min = toCalendarDate(minValue)
  const max = toCalendarDate(maxValue)
  const start = toCalendarDate(value?.start ?? null)
  const end = toCalendarDate(value?.end ?? null)
  // `DateRangePicker` does not accept a half-set range: when one side is
  // null, fall back to today on that side. The picker writes back the
  // real shape in `onChange` — this is a render-time bridge, never a value
  // the call site sees.
  const range =
    start !== null || end !== null
      ? { start: start ?? today("UTC"), end: end ?? today("UTC") }
      : undefined
  const empty = start === null && end === null

  return (
    <Field id={id} label={label} required={required} hint={hint} error={error}>
      <AriaDateRangePicker
        className={cn(styles.group, className)}
        value={range}
        minValue={min ?? undefined}
        maxValue={max ?? undefined}
        isDisabled={disabled}
        granularity="day"
        aria-labelledby={fieldLabelId(id)}
        aria-describedby={hint || error ? fieldDescriptionId(id) : undefined}
        data-test={dataTest}
        onChange={(next) => {
          onValueChange(
            next
              ? {
                  start: toIsoString(next.start as CalendarDate | null),
                  end: toIsoString(next.end as CalendarDate | null),
                }
              : null
          )
        }}
      >
        <Group
          className={styles.trigger}
          data-empty={empty ? "" : undefined}
          data-invalid={error ? "" : undefined}
          data-disabled={disabled || undefined}
        >
          <DateInput slot="start">
            {(segment) =>
              segment.type === "literal" ? (
                <span className={styles.literal}>{segment.text}</span>
              ) : (
                <DateSegment
                  segment={segment}
                  className={cn(
                    styles.segment,
                    segment.isPlaceholder && styles.segmentPlaceholder
                  )}
                />
              )
            }
          </DateInput>
          <span className={styles.dash} aria-hidden="true">
            –
          </span>
          <DateInput slot="end">
            {(segment) =>
              segment.type === "literal" ? (
                <span className={styles.literal}>{segment.text}</span>
              ) : (
                <DateSegment
                  segment={segment}
                  className={cn(
                    styles.segment,
                    segment.isPlaceholder && styles.segmentPlaceholder
                  )}
                />
              )
            }
          </DateInput>
          <AriaButton
            className={styles.iconButton}
            aria-label="Open calendar"
            data-test={dataTest ? `${dataTest}-calendar` : undefined}
          >
            <CalendarDays className={styles.icon} aria-hidden="true" />
          </AriaButton>
        </Group>
        <Popover className={styles.popover} placement="bottom start">
          <Dialog className={styles.dialog}>
            <RangeCalendar className={styles.calendar}>
              <header className={styles.calendarHeader}>
                <NavButton slot="previous">
                  <ChevronLeft className={styles.navIcon} aria-hidden="true" />
                </NavButton>
                <CalendarHeading className={styles.calendarHeading} />
                <NavButton slot="next">
                  <ChevronRight className={styles.navIcon} aria-hidden="true" />
                </NavButton>
              </header>
              <CalendarGrid className={styles.calendarGrid}>
                <CalendarGridHeader className={styles.weekHeader}>
                  {(day) => (
                    <CalendarHeaderCell className={styles.weekDay}>
                      {day}
                    </CalendarHeaderCell>
                  )}
                </CalendarGridHeader>
                <CalendarGridBody className={styles.calendarGridBody}>
                  {(date) => (
                    <CalendarCell date={date} className={styles.day} />
                  )}
                </CalendarGridBody>
              </CalendarGrid>
            </RangeCalendar>
          </Dialog>
        </Popover>
      </AriaDateRangePicker>
    </Field>
  )
}
