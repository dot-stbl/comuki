import type { ComponentType } from "react"
import { ShieldAlert, ShieldCheck, TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"

import { cn } from "@/shared/lib/utils"
import { badgeShell } from "@/shared/ui"
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
 * The words resolve from the `approvals` catalogue so the chip and the
 * decision buttons beside it keep reading as one vocabulary in every locale.
 */

export interface ApprovalTypeBadgeProps {
  type: ApprovalType
  className?: string
}

export function ApprovalTypeBadge({ type, className }: ApprovalTypeBadgeProps) {
  const { t } = useTranslation("approvals")
  const { icon: Icon } = APPROVAL_TYPE_META[type]

  return (
    <span
      data-test="approval-type-badge"
      data-type={type}
      className={cn(badgeShell(), styles.badge, styles.type, className)}
    >
      <Icon aria-hidden="true" />
      {t(`type.${type}`)}
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
  risk: ApprovalRisk | null
  className?: string
}

export function ApprovalRiskBadge({ risk, className }: ApprovalRiskBadgeProps) {
  const { t } = useTranslation("approvals")
  // No risk reading on the wire, no badge: an invented medium would carry a
  // hue a human would weigh, and the queue's judgement must stay its own.
  if (risk === null) {
    return null
  }

  const Icon = riskIcons[risk]

  return (
    <span
      data-test="approval-risk-badge"
      data-risk={risk}
      className={cn(badgeShell(), styles.badge, styles[risk], className)}
    >
      <Icon aria-hidden="true" />
      {t(`risk.${risk}`)}
    </span>
  )
}
