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
 * The screen's one control: the kit's `SearchField` at the toolbar's own
 * density, carrying this screen's two words — the name it announces itself by
 * and the words on the empty box. There is no second drawing of a search box
 * here any more, and nothing left in this file that can drift from the one the
 * data table's toolbar uses.
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
