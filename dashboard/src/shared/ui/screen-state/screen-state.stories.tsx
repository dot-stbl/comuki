import type { Meta, StoryObj } from "@storybook/react"
import { ArrowLeft, RotateCw } from "lucide-react"

import { Button } from "../button"
import { Tooltip } from "../tooltip"

import { ScreenState, StateText } from "./screen-state"

const meta = {
  title: "UI Kit/Feedback/ScreenState",
  component: ScreenState,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  argTypes: {
    kind: {
      control: "inline-radio",
      options: ["empty", "error", "notFound", "forbidden"],
    },
    inset: {
      control: "inline-radio",
      options: ["flush", "gutter", "page", "none"],
    },
    title: { control: "text" },
    description: { control: "text" },
    hint: { control: "text" },
  },
} satisfies Meta<typeof ScreenState>

export default meta
type Story = StoryObj<typeof meta>

const retry = (
  <Tooltip content="Retry">
    <Button size="icon-sm" aria-label="Retry" data-test="story-retry">
      <RotateCw aria-hidden="true" />
    </Button>
  </Tooltip>
)

/** The request failed. The only kind that interrupts a screen reader. */
export const Failed: Story = {
  args: {
    kind: "error",
    title: "The backlog did not load",
    description: "The registry answered with 503 Service Unavailable.",
    action: retry,
  },
}

/** The request answered and the answer is nothing. Ordinary, often correct. */
export const Empty: Story = {
  args: {
    kind: "empty",
    title: "No runs yet",
    description:
      "Nothing has been accepted on this project. A run appears here as soon as the brain takes a ticket.",
  },
}

/** An id resolved to nothing — a stale tab or an old link, not a failure. */
export const NotFound: Story = {
  args: {
    kind: "notFound",
    title: "No project with that id",
    description:
      "The registry holds nothing under that address. A project id out of an old link is the ordinary way to arrive here.",
    hint: "pxr-4482-legacy",
    action: (
      <Tooltip content="Back to projects">
        <Button size="icon-sm" aria-label="Back to projects" variant="outline">
          <ArrowLeft aria-hidden="true" />
        </Button>
      </Tooltip>
    ),
  },
}

/** With no sentence at all — a title is the whole answer often enough. */
export const TitleOnly: Story = {
  args: { kind: "empty", title: "No runs yet." },
}

/** A state that genuinely carries more than one paragraph takes `StateText`. */
export const SeveralParagraphs: Story = {
  args: {
    kind: "notFound",
    title: "This container is gone",
    description:
      "It was torn down while this page was open. A stopped container is not kept — there is no record of it to go back to, and there is not meant to be.",
    children: (
      <StateText>
        It was idle when it went, so it was holding nothing and nothing returned
        to the queue.
      </StateText>
    ),
  },
}

/** The three insets, side by side against a ruled edge. */
export const Insets: Story = {
  args: { kind: "empty", title: "Nothing here" },
  render: () => (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s5)",
        borderInlineStart: "var(--hairline) solid var(--rule-strong)",
      }}
    >
      <ScreenState kind="empty" inset="flush" title="flush" />
      <ScreenState kind="empty" inset="gutter" title="gutter" />
      <ScreenState kind="empty" inset="page" title="page" />
      <ScreenState kind="empty" inset="none" title="none" />
    </div>
  ),
}
