import { createFileRoute } from "@tanstack/react-router"

import { CostPage } from "@/domains/cost"

export const Route = createFileRoute("/cost")({
  component: CostPage,
})
