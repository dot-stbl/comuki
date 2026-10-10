import { useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { SearchField } from "./search-field"

const meta: Meta<typeof SearchField> = {
  title: "UI Kit/Inputs/Search field",
  component: SearchField,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof SearchField>

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

function Toolbar() {
  const [value, setValue] = useState("")
  return (
    <Column>
      <SearchField
        value={value}
        onValueChange={setValue}
        aria-label="Filter runs"
        placeholder="search runs…"
        size="sm"
        data-active={value.length > 0}
      />
    </Column>
  )
}

/** The toolbar's density — `sm`, with the mark the field wears while it
 *  is narrowing something. The shape the data table's toolbar has
 *  promoted from a text filter since the kit's `Select` landed. */
export const InAToolbar: Story = {
  name: "Toolbar density",
  render: () => <Toolbar />,
}

function Form() {
  const [value, setValue] = useState("Plexor")
  return (
    <Column>
      <SearchField
        value={value}
        onValueChange={setValue}
        aria-label="Filter rules, docs and skills"
        placeholder="Search rules, docs, skills…"
        data-active={value.length > 0}
      />
    </Column>
  )
}

/** A non-empty value with the field's "active" mark — the same voice the
 *  active select trigger wears. The clear button is the operator's way
 *  back to "all rows". */
export const Active: Story = {
  name: "Active filter",
  render: () => <Form />,
}

function Empty() {
  const [value, setValue] = useState("")
  return (
    <Column>
      <SearchField
        value={value}
        onValueChange={setValue}
        aria-label="Filter rules, docs and skills"
        placeholder="Search rules, docs, skills…"
      />
    </Column>
  )
}

/** The empty field with its placeholder showing. */
export const Default: Story = {
  name: "Empty",
  render: () => <Empty />,
}

function DisabledStory() {
  return (
    <Column>
      <SearchField
        value="Plexor"
        onValueChange={() => {}}
        aria-label="Filter runs"
        placeholder="search runs…"
        disabled
        size="sm"
      />
    </Column>
  )
}

/** The disabled state — the cursor and the field both read faint, and
 *  the clear button hides because the operator is not the one driving. */
export const Disabled: Story = {
  name: "Disabled",
  render: () => <DisabledStory />,
}

function CustomPlaceholderStory() {
  const [value, setValue] = useState("")
  return (
    <Column>
      <SearchField
        value={value}
        onValueChange={setValue}
        aria-label="Filter projects"
        placeholder="Search by name, owner or app…"
      />
    </Column>
  )
}

export const CustomPlaceholder: Story = {
  name: "Custom placeholder",
  render: () => <CustomPlaceholderStory />,
}
