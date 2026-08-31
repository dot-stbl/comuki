import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Button } from "./button"
import {
  ConfirmDialog,
  type ConfirmDialogProps,
} from "./confirm-dialog"

function Demo(args: ConfirmDialogProps) {
  const [open, setOpen] = useState(args.open)
  const [done, setDone] = useState(false)

  return (
    <>
      <Button
        data-test="story-confirm-open"
        variant={args.danger ? "destructive" : "default"}
        onClick={() => {
          setDone(false)
          setOpen(true)
        }}
      >
        Open confirm
      </Button>
      {done ? <p data-test="story-confirm-done">Confirmed</p> : null}
      <ConfirmDialog
        {...args}
        open={open}
        onConfirm={() => {
          setDone(true)
          setOpen(false)
        }}
        onCancel={() => {
          setOpen(false)
        }}
      />
    </>
  )
}

const meta = {
  title: "UI Kit/Overlays/ConfirmDialog",
  component: ConfirmDialog,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
  args: {
    open: false,
    title: "Abort run?",
    body: "Workers will stop after the current work item. Partial artifacts stay in the run log.",
    confirmLabel: "Abort",
    cancelLabel: "Keep running",
    danger: true,
    onConfirm: () => undefined,
    onCancel: () => undefined,
  },
  argTypes: {
    danger: { control: "boolean" },
  },
  render: (args) => <Demo {...args} />,
} satisfies Meta<typeof ConfirmDialog>

export default meta
type Story = StoryObj<typeof meta>

export const Danger: Story = {}

export const Neutral: Story = {
  args: {
    danger: false,
    title: "Resume swarm?",
    body: "Paused workers will claim the next queued tickets.",
    confirmLabel: "Resume",
    cancelLabel: "Stay paused",
  },
}
