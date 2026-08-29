export type ApprovalType = "plan" | "deploy" | "baseline"
export type ApprovalRisk = "low" | "medium" | "high"
export type ApprovalDecision = "approve" | "reject" | "review"

export interface Approval {
  id: string
  type: ApprovalType
  app: string
  runId: string
  age: string
  risk: ApprovalRisk
  summary: string
  assumptions: string[]
}
