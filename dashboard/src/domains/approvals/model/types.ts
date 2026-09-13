export type ApprovalType = "plan" | "deploy" | "baseline" | "gate"
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
  /**
   * A judgement about what getting this wrong costs. `null` when the source
   * carries none — an escalated run says nothing about its own risk, and an
   * invented medium is a reading somebody would act on.
   */
  risk: ApprovalRisk | null
  summary: string
  assumptions: string[]
}
