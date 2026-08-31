import { useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { SelectField } from "../form"
import { Select } from "./select"

/**
 * The product's one select.
 *
 * It is shown twice on purpose: as a form field and as a toolbar filter. That
 * pair used to be two different components — a native `<select>` on forms and a
 * React Aria listbox in the data table's toolbar — and the whole point of the
 * promotion is that the difference between them is now three props.
 */
const meta: Meta<typeof Select> = {
  title: "UI Kit/Inputs/Select",
  component: Select,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Select>

const ROLES = [
  { value: "viewer", label: "viewer" },
  { value: "member", label: "member" },
  { value: "approver", label: "approver" },
  { value: "operator", label: "operator" },
  { value: "project-admin", label: "project-admin" },
  { value: "platform-admin", label: "platform-admin" },
]

const APPS = [
  { value: "plexor", label: "plexor" },
  { value: "auth-svc", label: "auth-svc" },
  { value: "checkout-web", label: "checkout-web" },
]

function Column({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s5)",
        inlineSize: "22rem",
      }}
    >
      {children}
    </div>
  )
}

function Labelled({
  id,
  label,
  children,
}: {
  id: string
  label: string
  children: ReactNode
}) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "var(--s2)" }}>
      <label
        id={`${id}-label`}
        htmlFor={id}
        style={{
          fontFamily: "var(--font-ui)",
          fontSize: "var(--t-micro)",
          fontWeight: "var(--fw-semibold)",
          letterSpacing: "var(--tracking-data)",
          color: "var(--text-faint)",
        }}
      >
        {label}
      </label>
      {children}
    </div>
  )
}

/** On a form: a closed list, at the height every other field stands at. */
export const OnAForm: Story = {
  render: function OnAFormStory() {
    const [role, setRole] = useState("viewer")
    return (
      <Column>
        <SelectField
          id="story-role"
          label="role"
          value={role}
          options={ROLES}
          hint="Roles live in code — these six are the whole set, and there is no way to add one."
          onValueChange={setRole}
        />
      </Column>
    )
  },
}

/**
 * In a toolbar: denser, with a row that puts the filter back to "all". The
 * control is the same one — `size`, `clearable` and `active` are the whole
 * difference.
 */
export const InAToolbar: Story = {
  render: function InAToolbarStory() {
    const [app, setApp] = useState("")
    return (
      <Column>
        <Labelled id="story-app" label="app">
          <Select
            id="story-app"
            size="sm"
            clearable
            value={app}
            options={APPS}
            placeholder="all apps"
            active={app !== ""}
            aria-labelledby="story-app-label"
            onValueChange={setApp}
          />
        </Labelled>
      </Column>
    )
  },
}

/** The two side by side, which is the argument for having one of them. */
export const OneControlTwoDensities: Story = {
  render: function BothStory() {
    const [role, setRole] = useState("operator")
    const [app, setApp] = useState("plexor")
    return (
      <Column>
        <Labelled id="story-both-role" label="role">
          <Select
            id="story-both-role"
            value={role}
            options={ROLES}
            aria-labelledby="story-both-role-label"
            onValueChange={setRole}
          />
        </Labelled>
        <Labelled id="story-both-app" label="app">
          <Select
            id="story-both-app"
            size="sm"
            clearable
            value={app}
            options={APPS}
            placeholder="all apps"
            active={app !== ""}
            aria-labelledby="story-both-app-label"
            onValueChange={setApp}
          />
        </Labelled>
      </Column>
    )
  },
}

/** Nothing chosen, something wrong, and nothing to choose from. */
export const States: Story = {
  render: function StatesStory() {
    const [empty, setEmpty] = useState("")
    return (
      <Column>
        <SelectField
          id="story-empty"
          label="scope"
          value={empty}
          options={[
            { value: "platform", label: "platform" },
            { value: "project", label: "project" },
          ]}
          placeholder="pick a scope"
          onValueChange={setEmpty}
        />
        <SelectField
          id="story-invalid"
          label="subject"
          value=""
          options={ROLES}
          placeholder="pick a subject"
          error="A grant needs somebody to grant to."
          onValueChange={() => {}}
        />
        <SelectField
          id="story-disabled"
          label="project"
          value=""
          options={[]}
          placeholder="no projects yet"
          disabled
          hint="No projects to scope a grant to yet."
          onValueChange={() => {}}
        />
      </Column>
    )
  },
}
