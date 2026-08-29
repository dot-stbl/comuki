import { createFileRoute } from "@tanstack/react-router"

import { KnowledgePage } from "@/domains/knowledge"

export const Route = createFileRoute("/knowledge")({
  component: KnowledgePage,
})
