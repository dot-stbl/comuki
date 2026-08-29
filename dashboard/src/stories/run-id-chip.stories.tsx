import type { Meta, StoryObj } from "@storybook/react"

import { RunIdChip } from "@/shared/ui/run-id-chip"

const meta: Meta<typeof RunIdChip> = {
  title: "Comuki/RunIdChip",
  component: RunIdChip,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof RunIdChip>

export const Default: Story = {
  args: { id: "01JXRW3Q8KVKPND7YFGT4R2E5" },
}

export const ShortId: Story = {
  args: { id: "abc123" },
}

export const LongId: Story = {
  args: { id: "01JXRW3Q8KVKPND7YFGT4R2E5A1B2C3D4E5F6G7H8I9J0K1L2" },
}

export const WithLongText: Story = {
  args: { id: "01JXRW3Q8KVKPND7YFGT4R2E5" },
}