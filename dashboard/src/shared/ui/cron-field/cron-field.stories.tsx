import { useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { CronField } from "./cron-field"

/**
 * A five-field cron entry — four presets the operator reaches for first, and a
 * `Custom…` reveal that drops the operator into the minute/hour/day/month/
 * weekday editor. Whichever path the operator takes, the value the field
 * writes is the cron string the schedule engine stores.
 */
const meta: Meta<typeof CronField> = {
  title: "UI Kit/Inputs/CronField",
  component: CronField,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof CronField>

function Column({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s5)",
        inlineSize: "32rem",
      }}
    >
      {children}
    </div>
  )
}

/** The presets row, no value chosen — the placeholder sits where a selection
 * would, and the field is empty until the operator picks. */
export const Presets: Story = {
  render: function PresetsStory() {
    const [value, setValue] = useState("")
    return (
      <Column>
        <CronField
          id="story-presets"
          label="cadence"
          value={value}
          hint="The four cadences the product ships with, and a Custom… reveal for the rest of the wire."
          onValueChange={setValue}
        />
      </Column>
    )
  },
}

/** A preset is selected — the cron string sits on the form. The custom
 * editor stays closed. */
export const WithPreset: Story = {
  render: function WithPresetStory() {
    const [value, setValue] = useState("0 3 * * 1")
    return (
      <Column>
        <CronField
          id="story-preset"
          label="cadence"
          value={value}
          hint="Mondays at 03:00 — the cron string is what the schedule engine stores."
          onValueChange={setValue}
        />
      </Column>
    )
  },
}

/** `Custom…` is chosen — the five-field editor opens below the preset row.
 * Whatever the operator types in the editor is the value the form holds. */
export const Custom: Story = {
  render: function CustomStory() {
    const [value, setValue] = useState("*/30 * * * *")
    return (
      <Column>
        <CronField
          id="story-custom"
          label="cadence"
          value={value}
          hint="The five selects compose the cron string live — minute, hour, day, month, weekday."
          onValueChange={setValue}
        />
      </Column>
    )
  },
}

/** A wrong value the field refuses — the read on `error` is the same one
 * `TextField` and `SelectField` carry. */
export const WithError: Story = {
  render: function WithErrorStory() {
    const [value, setValue] = useState("0 3 * * *")
    return (
      <Column>
        <CronField
          id="story-error"
          label="cadence"
          value={value}
          error="the schedule engine needs five fields — this one has six"
          onValueChange={setValue}
        />
      </Column>
    )
  },
}

/** Disabled: the preset row reads faint, the custom selects refuse clicks,
 * the whole field stops being a control. */
export const Disabled: Story = {
  render: function DisabledStory() {
    const [value, setValue] = useState("0 3 * * 1")
    return (
      <Column>
        <CronField
          id="story-disabled"
          label="cadence"
          value={value}
          disabled
          hint="The schedule is paused — read only until the run finishes."
          onValueChange={setValue}
        />
      </Column>
    )
  },
}
