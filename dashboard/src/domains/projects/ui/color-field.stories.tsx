import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react-vite"

import { ColorField } from "./color-field"

/**
 * The colour control as the two surfaces that collect it use it — a swatch,
 * a refusal beside it, and the sentence that says which of the two meanings
 * "no colour" carries on this screen.
 *
 * The wrapper holds the value because the field is controlled by design: the
 * committed `#rrggbb` (or `null`) is the screen's state, not the control's.
 */
function Live({
  initial,
  disabled = false,
}: {
  initial: string | null
  disabled?: boolean
}) {
  const [value, setValue] = useState<string | null>(initial)

  return (
    <div style={{ inlineSize: "26rem" }}>
      <ColorField
        id="story-color"
        label="accent colour"
        value={value}
        onValueChange={setValue}
        clearLabel="no colour"
        disabled={disabled}
        hint="Optional. Paints the dot beside the mark and tints the tags — never the interface."
      />
    </div>
  )
}

const meta: Meta<typeof ColorField> = {
  title: "Projects/Color field",
  component: ColorField,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ColorField>

/** A colour chosen: the swatch wears it, the refusal sits unpressed beside it. */
export const Default: Story = {
  render: () => <Live initial="#3c5a86" />,
}

/**
 * Nothing chosen: the swatch shows the control's own grey — the one value
 * that is a placeholder rather than data — and the refusal is pressed,
 * because `null` is the committed answer until the operator picks.
 */
export const Unset: Story = {
  render: () => <Live initial={null} />,
}

/**
 * Both controls refuse the pointer together. A disabled colour field still
 * shows which colour is stored, so a read-only identity says in one glance
 * what a frozen form would hide behind an empty box.
 */
export const Disabled: Story = {
  render: () => <Live initial="#6d5b4b" disabled />,
}
