import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import {
  AutonomyModeMark,
  EnvTags,
  KeyStatusMark,
  RuleKindMark,
} from "./settings-badges"

function Row({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexWrap: "wrap",
        gap: "var(--s4)",
        alignItems: "center",
        padding: "var(--s6)",
      }}
    >
      {children}
    </div>
  )
}

const meta: Meta<typeof RuleKindMark> = {
  title: "Settings/Marks",
  component: RuleKindMark,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof RuleKindMark>

/**
 * A rule is binding or advisory, and neither of those is a run status — so
 * neither takes a status hue. The lock and the quill carry the reading, and the
 * border weight says which one the swarm may not talk its way past.
 */
export const RuleKinds: Story = {
  render: () => (
    <Row>
      <RuleKindMark kind="hard" />
      <RuleKindMark kind="soft" />
    </Row>
  ),
}

/**
 * A provider key's health *is* a state somebody has to act on, so this one
 * takes a hue — and it says the provider's own words rather than the enum, so
 * `budget 67%` reaches the screen instead of being flattened to `warn`.
 */
export const KeyHealth: Story = {
  render: () => (
    <Row>
      <KeyStatusMark status="ok" label="ok" />
      <KeyStatusMark status="warn" label="budget 67%" />
      <KeyStatusMark status="warn" label="rotation overdue" />
    </Row>
  ),
}

/**
 * Who decides a class of change. `human` takes the product's waiting hue on
 * purpose: a change class set to `human` is exactly what the run status
 * `waiting on a human` describes, one level up.
 */
export const AutonomyModes: Story = {
  render: () => (
    <Row>
      <AutonomyModeMark mode="auto" />
      <AutonomyModeMark mode="human" />
    </Row>
  ),
}

/**
 * Environments are furniture, not status: hairline chips in the chrome's own
 * material, no fill and no hue. An app that deploys nowhere says so.
 */
export const Environments: Story = {
  render: () => (
    <Row>
      <EnvTags envs={["prod", "staging", "preview"]} />
      <EnvTags envs={["prod"]} />
      <EnvTags envs={[]} />
    </Row>
  ),
}

/**
 * Every mark carries a silhouette as well as a hue, so the whole set survives
 * greyscale — which is the only proof that hue is never doing the work alone.
 */
export const InGreyscale: Story = {
  render: () => (
    <div style={{ filter: "grayscale(1)" }}>
      <Row>
        <RuleKindMark kind="hard" />
        <RuleKindMark kind="soft" />
        <KeyStatusMark status="ok" label="ok" />
        <KeyStatusMark status="warn" label="budget 67%" />
        <AutonomyModeMark mode="auto" />
        <AutonomyModeMark mode="human" />
      </Row>
    </div>
  ),
}
