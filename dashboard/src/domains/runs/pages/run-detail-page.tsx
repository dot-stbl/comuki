import { getRouteApi } from "@tanstack/react-router"

import { AppShell } from "@/app/layout/app-shell"
import { DomainStub } from "@/shared/ui/domain-stub"

const runDetailRoute = getRouteApi("/runs/$runId")

export function RunDetailPage() {
  const { runId } = runDetailRoute.useParams()

  return (
    <AppShell>
      <DomainStub
        title={`Run ${runId}`}
        description={`mock-first · W1–W3 · detail for ${runId}`}
      />
    </AppShell>
  )
}
