import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { TagFilter } from "./tag-filter"

/**
 * The filter held by the state that owns it — selected tags and the flip
 * handler, the same contract the registry page wires into its rows memo.
 */
function Live({
  available,
  initialSelected,
}: {
  available: string[]
  initialSelected: string[]
}) {
  const [selected, setSelected] = useState<string[]>(initialSelected)

  const toggle = (tag: string) => {
    setSelected((current) =>
      current.includes(tag)
        ? current.filter((entry) => entry !== tag)
        : [...current, tag]
    )
  }

  return (
    <div style={{ inlineSize: "34rem" }}>
      <TagFilter
        availableTags={available}
        selectedTags={selected}
        onToggleTag={toggle}
      />
    </div>
  )
}

const meta: Meta<typeof TagFilter> = {
  title: "Projects/Tag filter",
  component: TagFilter,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof TagFilter>

/**
 * The bar as it narrows: one vocabulary, two of its words pressed. A
 * pressed chip reads through the lane's own material — click `web` to watch
 * the fill swap, click it again to give it back.
 */
export const Default: Story = {
  render: () => (
    <Live
      available={["web", "billing", "infra", "compliance"]}
      initialSelected={["web", "infra"]}
    />
  ),
}

/**
 * No vocabulary: a registry whose rows carry no tags has no filter to
 * offer, and the slot is absent rather than occupied by an empty group —
 * which is why this story renders as a blank canvas.
 */
export const Empty: Story = {
  render: () => <Live available={[]} initialSelected={[]} />,
}

/**
 * Enough tags to overflow the bar's share of the toolbar. The chips stay
 * on one line and the strip scrolls silently — the vocabulary is read by
 * scrolling it, and the toolbar never grows a second row at the expense of
 * the table below.
 */
export const ManyTags: Story = {
  render: () => (
    <Live
      available={[
        "platform",
        "orchestration",
        "web",
        "billing",
        "infra",
        "internal",
        "experimental",
        "compliance",
        "agents",
        "data",
        "observability",
        "sandbox",
      ]}
      initialSelected={["platform", "web", "data"]}
    />
  ),
}
