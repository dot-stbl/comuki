import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { KnowledgeSearch } from "./knowledge-search"

const meta: Meta<typeof KnowledgeSearch> = {
  title: "Knowledge/Search",
  component: KnowledgeSearch,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div style={{ display: "flex", padding: "var(--s6)" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof KnowledgeSearch>

/**
 * The screen's one control, drawn to the toolbar's own measurements.
 *
 * **A gap in the kit**: the kit's search field is private to
 * `DataTableToolbar`, promoted out of a column's `meta.filter` — and knowledge
 * filters a list of entries that are not table rows, so it has no column to
 * declare one on. Every value here is the toolbar's, so the two read as one
 * control on two screens rather than as two opinions about a search box.
 */
export const Empty: Story = {
  render: function Render() {
    const [value, setValue] = useState("")
    return <KnowledgeSearch value={value} onValueChange={setValue} />
  },
}

/** Carrying a query — what arriving on `/knowledge?q=secrets` looks like. */
export const Filtering: Story = {
  render: function Render() {
    const [value, setValue] = useState("secrets")
    return <KnowledgeSearch value={value} onValueChange={setValue} />
  },
}
