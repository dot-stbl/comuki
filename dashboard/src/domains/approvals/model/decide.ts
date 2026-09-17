import type { Approval } from "@/domains/approvals/model/types"
import type { Permission } from "@/shared/session"

/**
 * Which act a decision on this card writes. Approving a plan and adopting a
 * rule are different judgements held by different grants — one queue, two
 * permissions — so the card and the page both resolve the act through here
 * instead of each naming one permission for everything.
 */
export function decisionPermissionOf(approval: Approval): Permission {
  return approval.type === "learning" ? "learning.review" : "plans.approve"
}
