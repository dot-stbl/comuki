import type { ComponentType } from "react"
import { ShieldAlert, ShieldCheck, TriangleAlert } from "lucide-react"

import { cn } from "@/shared/lib/utils"
import type {
  ApprovalRisk,
  ApprovalType,
} from "@/domains/approvals/model/types"

import { APPROVAL_TYPE_META } from "./approval-type-meta"
import styles from "./approval-badges.module.css"

/**
 * What is being decided, and how much it costs to get it wrong.
 *
 * Two marks rather than the kit's `StatusBadge`, for the reason the queue's own
 * badges give: `StatusBadge` speaks the six *run* statuses, and neither of
 * these is one. An approval is a `plan`, a `deploy` or a `baseline` — three
 * kinds of decision — and a risk is a judgement about it. Widening a shared
 * primitive to carry a vocabulary one screen speaks is how a primitive stops
 * being shared, so these follow its construction instead: an icon, a hue and a
 * hairline, from the same tokens, at the small step.
 *
 * The words are the ones the model stores, spelled the way it stores them. The
 * chip used to say `Plan` while the button beside it said "approve the plan",
 * which reads as two vocabularies for one value.
 */

export interface ApprovalTypeBadgeProps {
  type: ApprovalType
  className?: string
}

export function ApprovalTypeBadge({ type, className }: ApprovalTypeBadgeProps) {
  const { icon: Icon, noun } = APPROVAL_TYPE_META[type]

  return (
    <span
      data-test="approval-type-badge"
      data-type={type}
      className={cn(styles.badge, styles.type, className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {noun}
    </span>
  )
}

/**
 * Risk carries its own silhouette as well as its own hue — it used to be a
 * single warning triangle in three tints, which in greyscale said the same
 * thing three times.
 */
const riskIcons: Record<ApprovalRisk, ComponentType<{ className?: string }>> = {
  high: TriangleAlert,
  medium: ShieldAlert,
  low: ShieldCheck,
}

export interface ApprovalRiskBadgeProps {
  risk: ApprovalRisk
  className?: string
}

export function ApprovalRiskBadge({ risk, className }: ApprovalRiskBadgeProps) {
  const Icon = riskIcons[risk]

  return (
    <span
      data-test="approval-risk-badge"
      data-risk={risk}
      className={cn(styles.badge, styles[risk], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {risk}
    </span>
  )
}
