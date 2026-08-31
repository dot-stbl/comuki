import { useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { ChoiceField } from "./choice-field"
import { CopyButton } from "./copy-button"
import { FormDialog } from "./form-dialog"
import { Notice } from "./notice"
import { SecretValue } from "./secret-value"
import { SelectField } from "./select-field"
import { SwitchField } from "./switch-field"
import { TextField } from "./text-field"
import { TextareaField } from "./textarea-field"

/**
 * The kit's form controls. Shown together because they are only ever used
 * together — a form is the unit, not a field.
 */
const meta: Meta<typeof TextField> = {
  title: "UI Kit/Inputs/Form controls",
  component: TextField,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof TextField>

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

function Fields() {
  const [name, setName] = useState("Payments Platform")
  const [slug, setSlug] = useState("Payments Platform")
  const [lifetime, setLifetime] = useState("90")

  return (
    <Column>
      <TextField
        id="story-name"
        label="name"
        value={name}
        onValueChange={setName}
      />
      <TextField
        id="story-slug"
        label="slug"
        value={slug}
        hint="Shown as a column in every list in the product."
        error={/\s/.test(slug) ? "no spaces — use a hyphen" : null}
        onValueChange={setSlug}
      />
      <SelectField
        id="story-lifetime"
        label="expires"
        value={lifetime}
        options={[
          { value: "0", label: "no expiry" },
          { value: "30", label: "in 30 days" },
          { value: "90", label: "in 90 days" },
        ]}
        hint="A key with no expiry is a key nobody will notice is still working."
        onValueChange={setLifetime}
      />
    </Column>
  )
}

/** A label, a control, and one line under it — hint, or the reason it is unhappy. */
export const Fields_: Story = {
  name: "Fields",
  render: () => <Fields />,
}

function Areas() {
  const [body, setBody] = useState(
    "The importer drops the last page of every export. Reproduced on the 40k-row file."
  )
  const [filter, setFilter] = useState('labels has "agent" and status != "done"')

  return (
    <Column>
      <TextareaField
        id="story-body"
        label="body"
        value={body}
        hint="What a person wrote for another person to read — the interface voice."
        onValueChange={setBody}
      />
      <TextareaField
        id="story-filter"
        label="filter"
        voice="code"
        value={filter}
        hint="A value the product consumes — the data voice, and it wraps rather than scrolls."
        onValueChange={setFilter}
      />
    </Column>
  )
}

/** The two voices, made a prop: a textarea genuinely carries both. */
export const Areas_: Story = {
  name: "Textareas",
  render: () => <Areas />,
}

function Switches() {
  const [watching, setWatching] = useState(true)

  return (
    <Column>
      <SwitchField
        id="story-watch"
        label="Admit tickets from this connection"
        checked={watching}
        onLabel="watching"
        offLabel="paused"
        hint="Nothing already admitted is withdrawn when this goes off."
        onCheckedChange={setWatching}
      />
      <SwitchField
        id="story-watch-denied"
        label="Admit tickets from this connection"
        checked={false}
        denied="needs project-admin"
        hint="Denied, not disabled — the sentence has to stay reachable."
        onCheckedChange={() => {}}
      />
    </Column>
  )
}

/**
 * Rectilinear, and the reading is the thumb's position *and* the word beside
 * it — never the fill's hue on its own.
 */
export const Switches_: Story = {
  name: "Switch",
  render: () => <Switches />,
}

function Choices() {
  const [mode, setMode] = useState("review")

  return (
    <Column>
      <ChoiceField
        name="story-mode"
        legend="what an admitted ticket does"
        value={mode}
        options={[
          {
            value: "queue",
            label: "queue only",
            description: "It lands on the queue and waits for a person to start it.",
          },
          {
            value: "review",
            label: "start, then review",
            description: "A worker takes it straight away; the result waits for approval.",
          },
          {
            value: "auto",
            label: "start and merge",
            description: "No human step at all. Only for projects that have earned it.",
          },
        ]}
        onValueChange={setMode}
      />
    </Column>
  )
}

/** Boxes rather than radio dots — each option carries the sentence that tells it apart. */
export const Choices_: Story = {
  name: "Choice",
  render: () => <Choices />,
}

/** The thing the operator has to know before they act, not after. Three tones, three marks. */
export const NoticeBand: Story = {
  render: () => (
    <Column>
      <Notice>
        The secret appears once, on the next screen. Copy it there — it is
        stored hashed and cannot be shown again.
      </Notice>
      <Notice tone="ok">Connected. The credential answered on the first try.</Notice>
      <Notice tone="bad">
        No answer from that host. Nothing was saved.
      </Notice>
    </Column>
  ),
}

/** A value the product produced: selectable, copyable, never editable. */
export const Secret: Story = {
  render: () => (
    <Column>
      <SecretValue
        id="story-secret"
        label="secret"
        value="cmk_4e9c_9f3b1c7a02d5486eb1c0d7f4a83e5619"
        hint="Stored as cmk_4e9c. That prefix is all the key list will ever show."
      />
    </Column>
  ),
}

/** The copy control alone. It says it copied, because the value cannot be checked twice. */
export const Copy: Story = {
  render: () => <CopyButton value="cmk_4e9c_9f3b1c7a02d5486eb1c0d7f4a83e5619" />,
}

function Dialog() {
  const [open, setOpen] = useState(true)
  const [value, setValue] = useState("")

  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        open
      </button>
      <FormDialog
        open={open}
        title="New project"
        description="A project owns its applications, its runs and its budget."
        submitLabel="Create project"
        submitDisabled={value.trim().length === 0}
        onSubmit={() => setOpen(false)}
        onCancel={() => setOpen(false)}
      >
        <TextField
          id="story-dialog-name"
          label="name"
          value={value}
          onValueChange={setValue}
        />
      </FormDialog>
    </>
  )
}

/** The modal a form lives in — React Aria behaviour, CSS Module look. */
export const Dialog_: Story = {
  name: "Form dialog",
  parameters: { layout: "fullscreen" },
  render: () => <Dialog />,
}

/** Denied is not disabled: the act stays reachable so its explanation is too. */
export const DeniedSubmit: Story = {
  parameters: { layout: "fullscreen" },
  render: () => (
    <FormDialog
      open
      title="Grant a role"
      description="A grant is a subject, a role and a scope."
      submitLabel="Grant"
      denied="needs platform-admin"
      onSubmit={() => {}}
      onCancel={() => {}}
    >
      <TextField
        id="story-denied"
        label="subject"
        value="rhea@comuki.local"
        onValueChange={() => {}}
      />
    </FormDialog>
  ),
}
