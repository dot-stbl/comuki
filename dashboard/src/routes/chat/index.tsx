import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { ChatPage } from "@/domains/chat"

export const Route = createFileRoute("/chat/")({
  component: RouteComponent,
})

/* `chat.use` is the act, and it is a *project* permission asked without a
   project — the rail's question, "may this person do it somewhere?" A shift
   that can use the console on one project out of three gets the console; which
   project a given command lands in is decided per command, on the composer's
   scope chip and on the proposal it produces. */
function RouteComponent() {
  return (
    <RequirePermission
      permission="chat.use"
      title="Console"
      crumbs={[{ label: "console" }]}
    >
      <ChatPage />
    </RequirePermission>
  )
}
