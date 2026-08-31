import type { Meta, StoryObj } from "@storybook/react"

import { Section } from "./section"

/**
 * A titled region of a screen, in the two headings the product has — and only
 * those two. Neither lets a call site pick a tracking.
 */
const meta: Meta<typeof Section> = {
  title: "UI Kit/Layout/Section",
  component: Section,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Section>

function Body({ children }: { children: string }) {
  return (
    <p
      style={{
        margin: 0,
        fontFamily: "var(--font-ui)",
        fontSize: "var(--t-sm)",
        lineHeight: "var(--lh-body)",
        color: "var(--text-muted)",
      }}
    >
      {children}
    </p>
  )
}

/** The region label: mono, micro, wide tracking. It names a region, not a value. */
export const Region: Story = {
  args: {
    id: "story-region",
    title: "Running now",
    children: <Body>Whatever the region holds sits here.</Body>,
  },
}

/** A figure rides at the end of the heading line, in the tight gesture and tabular. */
export const RegionWithNote: Story = {
  name: "Region with a note",
  args: {
    id: "story-region-note",
    title: "Running now",
    note: "7 in flight · 3 queued",
    children: <Body>Whatever the region holds sits here.</Body>,
  },
}

/** The screen title: interface voice, with the paragraph that says what this is. */
export const Screen: Story = {
  args: {
    variant: "screen",
    title: "Pools",
    note: "Scaling is quota-aware plus the provider's capacity api, so every pool has two ceilings with two different owners. The one that is binding is the one refusing the next container.",
    children: <Body>Whatever the section holds sits here.</Body>,
  },
}

/** Two regions in a row, so the rhythm between a heading and its stack is visible. */
export const Stacked: Story = {
  render: () => (
    <div style={{ display: "flex", flexDirection: "column", gap: "var(--s8)" }}>
      <Section id="story-stacked-a" title="Needs you">
        <Body>Three runs are waiting on a human.</Body>
      </Section>
      <Section id="story-stacked-b" title="Running now" note="7 in flight">
        <Body>The swarm is working on seven.</Body>
      </Section>
    </div>
  ),
}
