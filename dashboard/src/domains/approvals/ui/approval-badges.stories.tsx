import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import type {
  ApprovalRisk,
  ApprovalType,
} from "@/domains/approvals/model/types"

import { ApprovalRiskBadge, ApprovalTypeBadge } from "./approval-badges"

const TYPES: ApprovalType[] = ["plan", "deploy", "baseline"]
const RISKS: ApprovalRisk[] = ["low", "medium", "high"]

function Row({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexWrap: "wrap",
        gap: "var(--s4)",
        alignItems: "center",
        padding: "var(--s6)",
      }}
    >
      {children}
    </div>
  )
}

const meta: Meta<typeof ApprovalRiskBadge> = {
  title: "Approvals/Badges",
  component: ApprovalRiskBadge,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof ApprovalRiskBadge>

/**
 * What is being decided. No hue: the kind of decision is the subject, not the
 * urgency — the risk badge beside it is the one worth a colour. The word is
 * the one the model stores, so the chip and the button's sentence ("approve the
 * deploy for checkout-web") speak one vocabulary rather than two.
 */
export const Types: Story = {
  render: () => (
    <Row>
      {TYPES.map((type) => (
        <ApprovalTypeBadge key={type} type={type} />
      ))}
    </Row>
  ),
}

/**
 * How much it costs to get it wrong. `high` escalates its own badge — a
 * stronger border and a full tint — the same rule the kit's `failed` follows,
 * because a high-risk deploy should be visible before it is read.
 */
export const Risks: Story = {
  render: () => (
    <Row>
      {RISKS.map((risk) => (
        <ApprovalRiskBadge key={risk} risk={risk} />
      ))}
    </Row>
  ),
}

/**
 * Each risk carries its own silhouette as well as its own hue, so the set
 * survives greyscale. It used to be one warning triangle in three tints, which
 * greyscale flattened into the same mark three times.
 */
export const RiskInGreyscale: Story = {
  render: () => (
    <div style={{ filter: "grayscale(1)" }}>
      <Row>
        {RISKS.map((risk) => (
          <ApprovalRiskBadge key={risk} risk={risk} />
        ))}
      </Row>
    </div>
  ),
}
