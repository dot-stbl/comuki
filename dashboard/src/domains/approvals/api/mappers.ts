import type { Approval } from "@/domains/approvals/model/types"
import type { SeedApproval } from "@/shared/api/mock"
import { PROJECT_BY_APP } from "@/shared/api/mock/runs.seed"

export function toApproval(seed: SeedApproval): Approval {
  return {
    id: seed.id,
    type: seed.type,
    app: seed.app,
    // An app is built inside one project and does not move, so the run, the
    // ticket and the approval can never disagree about where they live.
    projectId: PROJECT_BY_APP[seed.app] ?? "",
    runId: seed.run,
    age: seed.age,
    risk: seed.risk,
    summary: seed.summary,
    assumptions: seed.assumptions,
  }
}
