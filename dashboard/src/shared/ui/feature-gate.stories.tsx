import type { Meta, StoryObj } from "@storybook/react"

import { FeatureGate } from "./feature-gate"

const meta = {
  title: "UI Kit/Feedback/FeatureGate",
  component: FeatureGate,
} satisfies Meta<typeof FeatureGate>

export default meta

type Story = StoryObj<typeof meta>

/** The default paid-state render — feature is covered. */
export const Available: Story = {
  args: {
    feature: "multi-repo",
    available: true,
    children: <p>multi-project selector, open</p>,
  },
}

/** The Community render — feature is not covered, kit's own fallback. */
export const Locked: Story = {
  args: {
    feature: "multi-repo",
    available: false,
    children: <p>multi-project selector, open</p>,
  },
}

/**
 * The in-flight render — snapshot has not landed. The gate optimistically
 * renders `children`, on purpose: flashing a locked state at first paint
 * would tell the reader the page is broken, and the gate's enforcement
 * lives on the server.
 */
export const Loading: Story = {
  args: {
    feature: "multi-repo",
    available: undefined,
    children: <p>multi-project selector, open</p>,
  },
}

/** A locked render where the caller has overridden the fallback. */
export const CustomFallback: Story = {
  args: {
    feature: "enterprise-sso",
    available: false,
    fallback: (
      <a href="mailto:sales@example.com" data-test="feature-gate-custom-fallback">
        contact sales to enable enterprise-sso
      </a>
    ),
    children: <p>sso settings panel, open</p>,
  },
}
