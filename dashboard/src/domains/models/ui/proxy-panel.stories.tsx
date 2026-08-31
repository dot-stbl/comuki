import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { PROXY_SEED } from "@/shared/api/mock/models.seed"
import { PROJECTS_SEED } from "@/shared/api/mock/session.seed"
import { SessionProvider, type Role } from "@/shared/session"

import { ProxyPanel } from "./proxy-panel"

/** A shift with exactly the platform roles a story wants to look at. */
function Shift({ roles, children }: { roles: Role[]; children: ReactNode }) {
  return (
    <SessionProvider
      user={{
        id: "u_story",
        name: "Story",
        email: "story@comuki.local",
        platformRoles: roles,
        projectRoles: {},
      }}
      projects={PROJECTS_SEED}
    >
      {children}
    </SessionProvider>
  )
}

const meta: Meta<typeof ProxyPanel> = {
  title: "Models/Proxy panel",
  component: ProxyPanel,
  parameters: { layout: "padded" },
  tags: ["autodocs"],
  args: { proxy: PROXY_SEED, onToggle: () => undefined },
  decorators: [
    (Story) => (
      <Shift roles={["platform-admin"]}>
        <Story />
      </Shift>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof ProxyPanel>

/**
 * Off — the seeded state, and the one that needed designing. The figures stay
 * so the argument for turning it back on is visible, marked as the last metered
 * window rather than as now.
 */
export const Off: Story = {}

/** On: the resting arrangement, and it wears no mark at all. */
export const On: Story = {
  args: { proxy: { ...PROXY_SEED, enabled: true, changedAgoSec: 19 * 86_400 } },
}

/** Mid-flight. `disabled` here is busy, which is what `disabled` is for. */
export const Switching: Story = {
  args: { busy: true },
}

/**
 * A role that may not turn it. The button stays where it was and explains
 * itself — never `disabled`, because a disabled control fires no pointer events
 * and its tooltip would be unreachable.
 */
export const Denied: Story = {
  decorators: [
    (Story) => (
      <Shift roles={["member"]}>
        <Story />
      </Shift>
    ),
  ],
}
