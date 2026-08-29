import type { Approval } from "@/domains/approvals/model/types"
import type { SeedApproval } from "@/shared/api/mock"

export function toApproval(seed: SeedApproval): Approval {
  return {
    id: seed.id,
    type: seed.type,
    app: seed.app,
    runId: seed.run,
    age: seed.age,
    risk: seed.risk,
    summary: seed.summary,
    assumptions: seed.assumptions,
  }
}
