import { createFileRoute, redirect } from "@tanstack/react-router"

/**
 * The observability screen is gone — its boards and connect guide are a
 * section of `/compute` now. The file stays so a URL somebody pastes lands on
 * where the section lives rather than a 404, and `routeTree.gen.ts` keeps its
 * shape without regeneration.
 *
 * `replace: true`, for the same reason `/verify` carries it: a redirect that
 * *pushes* leaves the dead path sitting in history, so the first press of Back
 * lands on `/observability` again and is bounced straight forward — the button
 * stops working rather than going back. Replacing takes the door out of the
 * history it was never a stop on.
 */
export const Route = createFileRoute("/observability")({
  beforeLoad: () => {
    throw redirect({ to: "/compute", replace: true })
  },
})
