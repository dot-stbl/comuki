import type { Meta, StoryObj } from "@storybook/react"

import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/shared/ui/input-otp"

const meta: Meta<typeof InputOTP> = {
  title: "UI/InputOTP",
  component: InputOTP,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof InputOTP>

export const Default: Story = {
  render: () => (
    <InputOTP maxLength={6}>
      <InputOTPGroup>
        {[...Array(6)].map((_, i) => (
          <InputOTPSlot key={i} index={i} />
        ))}
      </InputOTPGroup>
    </InputOTP>
  ),
}

export const Loading: Story = {}

export const Disabled: Story = {}

export const Error: Story = {}

export const Empty: Story = {}

export const WithLongText: Story = {}