import { createFileRoute } from "@tanstack/react-router"

import { RunsPage } from "@/domains/runs"

export const Route = createFileRoute("/runs/")({
  component: RunsPage,
})
