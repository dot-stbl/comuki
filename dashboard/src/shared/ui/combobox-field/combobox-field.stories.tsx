import { useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { ComboboxField } from "./combobox-field"

/**
 * The labelled combobox — type to filter a closed list, with `allowsCustomValue`
 * for the cases where the list will always be one step behind the answer.
 *
 * Shown as a column of every state, because the states only read together: a
 * combobox without an option list is the same chrome as one with three rows;
 * the difference is whether the operator can get to the value without typing it
 * from memory.
 */
const meta: Meta<typeof ComboboxField> = {
  title: "UI Kit/Inputs/ComboboxField",
  component: ComboboxField,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ComboboxField>

const MODELS = [
  { value: "lead-xl-2", label: "lead-xl-2", hint: "provider-A · plan + contract" },
  { value: "lead-mid-2", label: "lead-mid-2", hint: "provider-A · review" },
  { value: "lead-xl-1", label: "lead-xl-1", hint: "provider-C · rollback path" },
  { value: "worker-sm-4", label: "worker-sm-4", hint: "provider-B · profile steps" },
  { value: "judge-mid-1", label: "judge-mid-1", hint: "provider-B · diff gate" },
  { value: "worker-sm-oss", label: "worker-sm-oss", hint: "self-hosted · degraded" },
]

function Column({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s5)",
        inlineSize: "26rem",
      }}
    >
      {children}
    </div>
  )
}

/** The model id use case. Closed list — a value not in the list is rejected. */
export const OnAForm: Story = {
  render: function OnAFormStory() {
    const [value, setValue] = useState("")
    return (
      <Column>
        <ComboboxField
          id="story-lead"
          label="lead model"
          value={value}
          options={MODELS}
          placeholder="pick a model"
          hint="The role that decomposes, contracts and reviews."
          onValueChange={setValue}
        />
      </Column>
    )
  },
}

/** A model id typed that is not yet in the seed. The reason `allowsCustomValue`
 * exists — the list is what the platform shipped, the value is what shipped
 * yesterday. */
export const AllowsCustomValue: Story = {
  render: function AllowsCustomValueStory() {
    const [value, setValue] = useState("lead-xl-3")
    return (
      <Column>
        <ComboboxField
          id="story-lead-custom"
          label="lead model"
          value={value}
          options={MODELS}
          allowsCustomValue
          hint="Type a model id that is not in the list — it still gets through."
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
    const [value, setValue] = useState("garbage")
    return (
      <Column>
        <ComboboxField
          id="story-bad"
          label="lead model"
          value={value}
          options={MODELS}
          error="unknown model — pick one of the six"
          onValueChange={setValue}
        />
      </Column>
    )
  },
}

/** Disabled: the chevron still shows, but the box reads faint and the operator
 * cannot type into it. */
export const Disabled: Story = {
  render: function DisabledStory() {
    const [value, setValue] = useState("lead-xl-2")
    return (
      <Column>
        <ComboboxField
          id="story-disabled"
          label="lead model"
          value={value}
          options={MODELS}
          disabled
          hint="The router is paused — read only until the run finishes."
          onValueChange={setValue}
        />
      </Column>
    )
  },
}

/** The label is real but not painted — the sentence around the field names
 * it. */
export const LabelHidden: Story = {
  render: function LabelHiddenStory() {
    const [value, setValue] = useState("lead-xl-2")
    return (
      <Column>
        <div style={{ display: "flex", alignItems: "center", gap: "var(--s3)" }}>
          <span style={{ fontFamily: "var(--font-data)", color: "var(--text)" }}>
            lead →
          </span>
          <ComboboxField
            id="story-label-hidden"
            label="lead model"
            labelHidden
            value={value}
            options={MODELS}
            onValueChange={setValue}
          />
        </div>
      </Column>
    )
  },
}

/** Toolbar density. The same control at the same floor every other toolbar
 * stands at. */
export const InAToolbar: Story = {
  render: function InAToolbarStory() {
    const [value, setValue] = useState("")
    return (
      <Column>
        <ComboboxField
          id="story-toolbar"
          label="model"
          size="sm"
          value={value}
          options={MODELS}
          placeholder="filter by model"
          onValueChange={setValue}
        />
      </Column>
    )
  },
}