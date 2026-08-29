import { createFileRoute } from "@tanstack/react-router"

import { ApprovalsPage } from "@/domains/approvals"

export const Route = createFileRoute("/approvals")({
  component: ApprovalsPage,
})
