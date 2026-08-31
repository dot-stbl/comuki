import type { Meta, StoryObj } from "@storybook/react"

import { ForbiddenState } from "./forbidden-state"

const meta = {
  title: "UI Kit/Feedback/ForbiddenState",
  component: ForbiddenState,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  argTypes: {
    needs: { control: "text" },
    subject: { control: "text" },
  },
} satisfies Meta<typeof ForbiddenState>

export default meta
type Story = StoryObj<typeof meta>

/** The sentence comes from `needsLabel("plans.approve")`. */
export const Default: Story = {
  args: {
    needs: "needs approver, project-admin or platform-admin",
    subject: "Approvals",
  },
}

/** A whole screen the current roles cannot open — `needsLabel("settings.live")`. */
export const Settings: Story = {
  args: {
    needs: "needs project-admin, operator or platform-admin",
    subject: "Settings",
  },
}

/** The platform tier, where exactly one role answers. */
export const PlatformOnly: Story = {
  args: {
    needs: "needs platform-admin",
    subject: "Identity",
  },
}

/** Without a subject it names nothing in particular — the list-body case. */
export const Unnamed: Story = {
  args: {
    needs: "needs project-admin, operator or platform-admin",
  },
}

/** A screen that has something more useful to add takes the extra line. */
export const WithExtraLine: Story = {
  args: {
    needs: "needs approver, project-admin or platform-admin",
    subject: "Approvals",
    children: (
      <p
        style={{
          margin: 0,
          fontSize: "var(--t-sm)",
          color: "var(--text-faint)",
        }}
      >
        You hold approver on comuki.
      </p>
    ),
  },
}
