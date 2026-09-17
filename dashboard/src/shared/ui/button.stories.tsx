import type { Meta, StoryObj } from "@storybook/react"
import { Check } from "lucide-react"

import { Button } from "./button"

const meta = {
  title: "UI Kit/Actions/Button",
  component: Button,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
  args: { children: "Run task" },
  argTypes: {
    variant: {
      control: "select",
      options: [
        "default",
        "outline",
        "secondary",
        "ghost",
        "destructive",
        "link",
      ],
    },
    size: {
      control: "select",
      options: ["default", "sm", "lg", "icon", "icon-sm", "icon-lg"],
    },
  },
} satisfies Meta<typeof Button>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const Outline: Story = {
  args: { variant: "outline" },
}

export const Secondary: Story = {
  args: { variant: "secondary" },
}

export const Ghost: Story = {
  args: { variant: "ghost" },
}

export const Destructive: Story = {
  args: { variant: "destructive", children: "Abort" },
}

export const Link: Story = {
  args: { variant: "link", children: "Open run" },
}

export const Small: Story = {
  args: { size: "sm" },
}

export const Disabled: Story = {
  args: { disabled: true, children: "Disabled" },
}

/**
 * Busy at a text size: the spinner leads, the word stays. A button that swapped
 * "Run task" for "Running…" would resize itself under the pointer and spend the
 * one reading that says *what* is running — so the label is the part that does
 * not move. The act is announced by `aria-busy`; the glyph is `aria-hidden`, and
 * saying it twice would be saying it wrong.
 */
export const Loading: Story = {
  args: { loading: true },
}

/**
 * Busy at an icon size, where the whole content is one glyph — so the spinner
 * takes its place rather than crowding in beside it. Which of the two happens is
 * read off `size`, not off a second prop: the call site says `loading` and the
 * button already knows how much room it has.
 *
 * The name survives the swap because it never lived in the glyph: `aria-label`
 * still says "Approve", and the spinner is `aria-hidden` like the check it
 * replaced.
 */
export const LoadingIcon: Story = {
  args: {
    loading: true,
    size: "icon",
    "aria-label": "Approve",
    children: <Check aria-hidden="true" />,
  },
}

/**
 * The pair that cannot both be true, and what the button does when it is handed
 * both anyway. A refused click never starts an act, so there is no act to spin
 * for — the refusal wins whole: no spinner, no native `disabled`, and the
 * control stays focusable and hoverable so the sentence naming what is missing
 * is still reachable. `loading` does not get to trade that away.
 */
export const LoadingDenied: Story = {
  args: { loading: true, denied: "needs approver" },
}
