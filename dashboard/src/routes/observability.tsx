import { createFileRoute, redirect } from "@tanstack/react-router"

/**
 * The observability screen is gone — its boards and connect guide are a
 * section of `/compute` now. The file stays so a URL somebody pastes lands on
 * where the section lives rather than a 404, and `routeTree.gen.ts` keeps its
 * shape without regeneration.
 */
export const Route = createFileRoute("/observability")({
  beforeLoad: () => {
    throw redirect({ to: "/compute" })
  },
})
