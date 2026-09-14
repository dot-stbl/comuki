import { useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { DatePickerField, DateRangePickerField, todayIso } from "./date-picker"

const meta: Meta<typeof DatePickerField> = {
  title: "UI Kit/Inputs/Date picker",
  component: DatePickerField,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof DatePickerField>

function Column({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s5)",
        inlineSize: "24rem",
      }}
    >
      {children}
    </div>
  )
}

function Single() {
  const [value, setValue] = useState<string | null>(todayIso())
  return (
    <Column>
      <DatePickerField
        id="story-date"
        label="date"
        value={value}
        onValueChange={setValue}
      />
    </Column>
  )
}

/** A single-day picker with today's date selected on render. */
export const Default: Story = {
  name: "Single day",
  render: () => <Single />,
}

function SingleWithHint() {
  const [value, setValue] = useState<string | null>(null)
  return (
    <Column>
      <DatePickerField
        id="story-date-hint"
        label="deadline"
        hint="The last day a ticket may still be admitted."
        value={value}
        onValueChange={setValue}
      />
    </Column>
  )
}

export const WithHint: Story = {
  name: "With hint",
  render: () => <SingleWithHint />,
}

function SingleWithMinMax() {
  const [value, setValue] = useState<string | null>(null)
  const today = todayIso()
  return (
    <Column>
      <DatePickerField
        id="story-date-clamp"
        label="release window"
        hint="Only days in the next two weeks are selectable."
        minValue={today}
        maxValue={addDays(today, 14)}
        value={value}
        onValueChange={setValue}
      />
    </Column>
  )
}

/** Clamped between a `min` and `max`; days outside the window are
 *  disabled in the calendar. */
export const WithMinMax: Story = {
  name: "Clamped range",
  render: () => <SingleWithMinMax />,
}

function SingleError() {
  const [value, setValue] = useState<string | null>(null)
  return (
    <Column>
      <DatePickerField
        id="story-date-error"
        label="date"
        value={value}
        onValueChange={setValue}
        error="Pick a date — this run cannot start without one."
      />
    </Column>
  )
}

export const Error: Story = {
  name: "With error",
  render: () => <SingleError />,
}

function SingleDisabled() {
  return (
    <Column>
      <DatePickerField
        id="story-date-disabled"
        label="date"
        value={todayIso()}
        onValueChange={() => {}}
        disabled
        hint="The release window has not opened yet."
      />
    </Column>
  )
}

export const Disabled: Story = {
  name: "Disabled",
  render: () => <SingleDisabled />,
}

function Range() {
  const [value, setValue] = useState<{
    start: string | null
    end: string | null
  } | null>(null)
  return (
    <Column>
      <DateRangePickerField
        id="story-range"
        label="window"
        hint="The calendar commits when both ends are picked."
        value={value}
        onValueChange={setValue}
      />
    </Column>
  )
}

/** A range picker. The two inputs in the trigger take the same `min` and
 *  `max` as a single picker would. */
export const Range_: Story = {
  name: "Range",
  render: () => <Range />,
}

function RangeErrorStory() {
  const [value, setValue] = useState<{
    start: string | null
    end: string | null
  } | null>({ start: "2026-09-10", end: "2026-09-08" })
  return (
    <Column>
      <DateRangePickerField
        id="story-range-error"
        label="window"
        value={value}
        onValueChange={setValue}
        error="End must be on or after start."
      />
    </Column>
  )
}

export const RangeWithError: Story = {
  name: "Range with error",
  render: () => <RangeErrorStory />,
}

function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}
