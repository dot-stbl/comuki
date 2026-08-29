import { createFileRoute } from "@tanstack/react-router"

import { RunDetailPage } from "@/domains/runs"

export const Route = createFileRoute("/runs/$runId")({
  component: RunDetailPage,
})
