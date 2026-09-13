import type { Meta, StoryObj } from "@storybook/react"

import { useState } from "react"

import { Button } from "./button"
import { Dialog } from "./dialog"

const meta: Meta<typeof Dialog> = {
  title: "UI Kit/Overlays/Dialog",
  component: Dialog,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div
        style={{
          minBlockSize: "24rem",
          padding: "var(--s4)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof Dialog>

/** A dismissable, informational dialog — the shape the anomaly breakdown uses. */
function InformationalStory() {
  const [open, setOpen] = useState(true)
  return (
    <>
      <Button onClick={() => setOpen(true)}>Open dialog</Button>
      <Dialog open={open} onOpenChange={setOpen} title="Cost spike · run 8f3c2a91">
        <p>
          This run spent <strong>$14.21</strong> on a single ticket — 12× the
          median cost across the same project.
        </p>
        <p>
          The token volume is unusual: <strong>2.3M input</strong> against
          <strong>1.8M output</strong> from the planner step.
        </p>
      </Dialog>
    </>
  )
}

export const Informational: Story = {
  render: () => <InformationalStory />,
}

/** A dialog with a custom action button on the left. */
function WithActionStory() {
  const [open, setOpen] = useState(true)
  return (
    <>
      <Button onClick={() => setOpen(true)}>Open dialog</Button>
      <Dialog
        open={open}
        onOpenChange={setOpen}
        title="Cancel run 8f3c2a91?"
        footer={<Button variant="destructive">Cancel run</Button>}
      >
        <p>
          The container will be torn down and the lease released. Work already
          merged stays.
        </p>
      </Dialog>
    </>
  )
}

export const WithAction: Story = {
  render: () => <WithActionStory />,
}

/** A non-dismissable dialog — escape and the scrim are off, only the
 *  button in the footer can close it. */
function ConfirmStory() {
  const [open, setOpen] = useState(true)
  return (
    <>
      <Button onClick={() => setOpen(true)}>Open dialog</Button>
      <Dialog
        open={open}
        onOpenChange={setOpen}
        dismissable={false}
        title="Stop the swarm?"
        footer={<Button variant="destructive">Stop swarm</Button>}
      >
        <p>
          This stops every active worker and cancels the running tickets.
          The decision is not reversible.
        </p>
      </Dialog>
    </>
  )
}

export const Confirm: Story = {
  render: () => <ConfirmStory />,
}
