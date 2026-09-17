import type { Meta, StoryObj } from "@storybook/react"

import { Fact, FactList } from "./fact-list"

const meta: Meta<typeof FactList> = {
  title: "UI Kit/Data/Fact List",
  component: FactList,
  parameters: { layout: "padded" },
  argTypes: {
    layout: { control: "radio", options: ["rows", "split", "stack"] },
    size: { control: "radio", options: ["sm", "md"] },
  },
}

export default meta
type Story = StoryObj<typeof meta>

const source = (
  <>
    <Fact name="provider">github</Fact>
    <Fact name="auth">personal access token</Fact>
    <Fact name="instance">https://api.github.com/repos/dot-stbl/comuki</Fact>
    <Fact name="account">comuki-bot</Fact>
    <Fact name="last sync" absent>
      never
    </Fact>
  </>
)

/**
 * The default. A name track and the value beside it — and the track is sized on
 * the *list*, so every value starts at the same edge however long the names are.
 */
export const Rows: Story = {
  args: { children: source },
}

/**
 * Bounded and divided: four facts about one record, read as a block. The name
 * track goes fixed here, because a framed list is read down the names as much
 * as across the rows.
 */
export const Framed: Story = {
  args: {
    framed: true,
    children: (
      <>
        <Fact name="slug">atlas</Fact>
        <Fact name="name" voice="prose">
          Atlas — ingest and reconciliation
        </Fact>
        <Fact name="git profile repository" selectable>
          git@github.com:dot-stbl/atlas-profile.git
        </Fact>
        <Fact name="created">2026-02-11</Fact>
      </>
    ),
  },
}

/** The sheet's arrangement, at the sheet's step. */
export const Split: Story = {
  args: {
    layout: "split",
    size: "sm",
    children: (
      <>
        <Fact name="scope">platform</Fact>
        <Fact name="updated">2026-03-04</Fact>
      </>
    ),
  },
}

/** Pairs flowing into as many columns as the board has room for. */
export const Stack: Story = {
  args: {
    layout: "stack",
    children: (
      <>
        <Fact name="address">nadia@comuki.local</Fact>
        <Fact name="name" voice="prose">
          Nadia Orlova
        </Fact>
        <Fact name="account">active</Fact>
        <Fact name="oidc subject" absent>
          local only
        </Fact>
        <Fact name="last seen" absent>
          never
        </Fact>
        <Fact name="created">2026-01-04</Fact>
      </>
    ),
  },
}

/** The two voices side by side — a value, and the one field a human wrote. */
export const Voices: Story = {
  args: {
    children: (
      <>
        <Fact name="slug">atlas</Fact>
        <Fact name="name" voice="prose">
          Atlas — ingest and reconciliation
        </Fact>
      </>
    ),
  },
}
