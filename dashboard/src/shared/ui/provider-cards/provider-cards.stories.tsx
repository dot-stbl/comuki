import { useState } from "react"
import { SquareKanban } from "lucide-react"
import type { Meta, StoryObj } from "@storybook/react"

import { BrandIcon } from "../brand-icon"

import {
  ProviderCards,
  type ProviderCardOption,
} from "./provider-cards"

/**
 * The kit's card picker, with the only content a kit story is allowed to
 * know: marks the kit itself can draw, a lucide glyph standing in for the
 * spelled card, and sentences written for the story rather than for a
 * product's registry.
 */
const OPTIONS: ProviderCardOption[] = [
  {
    value: "github",
    label: "github",
    mark: <BrandIcon brand="github" size="lg" label={null} />,
    note: "issues land from a watched repository. one written here is stamped as the repo's.",
  },
  {
    value: "spelled",
    label: "a provider whose mark would be a guess",
    /* A mark that is not the kit's own takes its size from the icon scale at
       home; the story spells that inline so the spelled card and the drawn
       ones keep one rhythm. */
    mark: (
      <SquareKanban
        style={{
          inlineSize: "var(--icon-lg)",
          blockSize: "var(--icon-lg)",
        }}
        aria-hidden="true"
      />
    ),
    name: "a provider whose mark would be a guess",
    note: "no monochrome mark is published, so the card spells the name instead.",
  },
  {
    value: "jira",
    label: "jira",
    mark: <BrandIcon brand="jira" size="lg" label={null} />,
    note: "tickets land from a watched board. one written here is stamped as the board's.",
  },
]

const meta: Meta<typeof ProviderCards> = {
  title: "UI Kit/Inputs/Provider cards",
  component: ProviderCards,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ProviderCards>

function Picked() {
  const [value, setValue] = useState("github")
  return (
    <div style={{ inlineSize: "48rem" }}>
      <ProviderCards
        label="provider"
        name="story-provider"
        value={value}
        onValueChange={setValue}
        options={OPTIONS}
        hint="the kit draws the shape; the caller supplies the providers."
      />
    </div>
  )
}

/** Three cards, one chosen, the check and the wash saying which. */
export const Picked_: Story = {
  name: "Picked",
  render: () => <Picked />,
}

/** A home that already says the word above the cards — spoken, not drawn. */
export const LabelHidden_: Story = {
  name: "Label hidden",
  render: () => (
    <div style={{ inlineSize: "48rem" }}>
      <ProviderCards
        label="provider"
        name="story-provider-hidden"
        value="spelled"
        onValueChange={() => {}}
        labelHidden
        options={OPTIONS}
      />
    </div>
  ),
}

/** Every card refuses together, and keeps its place in the tab order. */
export const Disabled_: Story = {
  name: "Disabled",
  render: () => (
    <div style={{ inlineSize: "48rem" }}>
      <ProviderCards
        label="provider"
        name="story-provider-disabled"
        value="github"
        onValueChange={() => {}}
        disabled
        options={OPTIONS}
      />
    </div>
  ),
}
