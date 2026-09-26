import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react-vite"

import { TagEntryField } from "./tag-entry-field"

/**
 * Tag entry held by the state that owns it — the chips are the committed
 * vocabulary, and the field hands the next one up rather than keeping it.
 */
function Live({
  initial,
  disabled = false,
}: {
  initial: string[]
  disabled?: boolean
}) {
  const [tags, setTags] = useState<string[]>(initial)

  return (
    <div style={{ inlineSize: "26rem" }}>
      <TagEntryField
        id="story-tags"
        value={tags}
        onValueChange={setTags}
        disabled={disabled}
        hint="Optional. What this project is — press Enter or comma to add each one, Backspace to remove the last."
      />
    </div>
  )
}

const meta: Meta<typeof TagEntryField> = {
  title: "Projects/Tag entry field",
  component: TagEntryField,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof TagEntryField>

/**
 * The field as it opens: no vocabulary yet, the placeholder naming the
 * normal shapes of one. Type `billing` and press Enter to watch it become a
 * chip; press Backspace on the empty field to take the last one back.
 */
export const Empty: Story = {
  render: () => <Live initial={[]} />,
}

/**
 * A vocabulary in progress. Each chip carries its own removal, a duplicate
 * is refused by silence (type `web` again), and a comma in the middle of a
 * paste commits everything before it — the separators are the same key
 * the operator will use to read the list back.
 */
export const WithChips: Story = {
  render: () => <Live initial={["web", "billing"]} />,
}

/**
 * Enough chips to wrap. The field grows rather than scrolls — a vocabulary
 * is read back as chips or not at all, and a scrolled single line would
 * hide exactly the words the operator came to check.
 */
export const ManyChips: Story = {
  render: () => (
    <Live
      initial={[
        "platform",
        "orchestration",
        "web",
        "billing",
        "infra",
        "internal",
        "experimental",
        "compliance",
      ]}
    />
  ),
}

/** The chips and the box refuse together — a frozen vocabulary is still readable. */
export const Disabled: Story = {
  render: () => <Live initial={["web", "billing"]} disabled />,
}
