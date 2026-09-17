import type { Meta, StoryObj } from "@storybook/react"
import type { CSSProperties } from "react"

import { Meter } from "./meter"

/** A band roughly the width a budget tile or a ranking row hands the bar. */
const BAND: CSSProperties = { inlineSize: "22rem" }

/**
 * The seam, as a call site spells it: the state attribute that colours the
 * figures sets `--st` on an ancestor, and the meter reads it. Here there are no
 * figures to colour, so the wrapper sets it directly.
 */
function heat(token: string): CSSProperties {
  return { ...BAND, "--st": `var(--${token})` } as CSSProperties
}

const meta = {
  title: "UI Kit/Data/Meter",
  component: Meter,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  argTypes: {
    track: { control: "inline-radio", options: ["tile", "row"] },
    tone: { control: "inline-radio", options: ["neutral", "heat"] },
    hatched: {
      control: "inline-radio",
      options: [undefined, "queued", "failed"],
    },
    edge: { control: "boolean" },
  },
  args: { value: 0.62 },
  decorators: [
    (Story) => (
      <div style={BAND}>
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof Meter>

export default meta
type Story = StoryObj<typeof meta>

/** The default track: a bar with a band of its own, on a tile or a card. */
export const Tile: Story = {}

/**
 * Two pixels, under the figures inside a one-line table cell. The corner scale
 * bottoms out at the hairline here — a 2px bar has no room for a 3px corner.
 */
export const Row: Story = { args: { track: "row" } }

/**
 * Neutral at every length, on purpose. A ranking carries no status: nobody is
 * being asked to do anything about being third, and length is what ranks.
 */
export const Neutral: Story = { args: { value: 0.93 } }

/**
 * The same length with the call site's heat behind it. `--st` comes from the
 * attribute that is already colouring the figures beside the bar, so the two
 * can never disagree.
 */
export const Heat: Story = {
  args: { value: 0.93, tone: "heat" },
  render: (args) => (
    <span style={heat("st-waiting")}>
      <Meter {...args} />
    </span>
  ),
}

/**
 * A cap that is recorded but not enforced. The length is still true — the spend
 * is real — but the weave withdraws the claim that anything happens at the end.
 */
export const HatchedQueued: Story = {
  args: { value: 0.48, tone: "heat", hatched: "queued" },
  render: (args) => (
    <span style={heat("st-waiting")}>
      <Meter {...args} />
    </span>
  ),
}

/**
 * Past the cap, where the kill-switch has already fired. The bar cannot grow
 * past full, so the second channel is a weave rather than more length.
 */
export const HatchedFailed: Story = {
  args: { value: 1, tone: "heat", hatched: "failed" },
  render: (args) => (
    <span style={heat("st-failed")}>
      <Meter {...args} />
    </span>
  ),
}

/**
 * The bordered channel, for a meter standing on the page's own surface rather
 * than inside a tile that already frames it.
 */
export const Edge: Story = { args: { edge: true } }

/**
 * `null` is not zero. A provider that did not answer has no fraction to draw,
 * and an empty channel would say "nothing used" — the one thing it is not
 * saying.
 */
export const NoReading: Story = { args: { value: null } }

/** Clamped, not trusted: a share above one draws full and never overruns. */
export const Overrun: Story = { args: { value: 1.4 } }
