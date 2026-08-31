export type ApprovalType = "plan" | "deploy" | "baseline"
export type ApprovalRisk = "low" | "medium" | "high"
export type ApprovalDecision = "approve" | "reject" | "review"

export interface Approval {
  id: string
  type: ApprovalType
  app: string
  /**
   * Which project this decision belongs to. The queue mixes them, and the
   * right to approve is held per project — so the card asks about *this*
   * project rather than about the shift.
   */
  projectId: string
  runId: string
  age: string
  risk: ApprovalRisk
  summary: string
  assumptions: string[]
}
