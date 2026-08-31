import { createFileRoute } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { ConnectSourcePage } from "@/domains/sources"

export const Route = createFileRoute("/sources/new")({
  component: RouteComponent,
})

/* Gated on `sources.view`, not on `sources.edit`, and the difference is the
   scope rather than a lax door. `sources.edit` is a **project** permission, and
   this form has not picked a project yet — asking it without one would refuse
   an administrator of `atlas` on the way in and then have to let them through
   the moment they chose `atlas` in the second select. So the route opens for
   anyone who may read the section, and the act answers to the project the form
   picked: the submit carries `denied` with the sentence naming the role and the
   project that would open it, and the page asks the same question again before
   it mutates anything. */
function RouteComponent() {
  return (
    <RequirePermission
      permission="sources.view"
      title="Connect a source"
      crumbs={[
        { label: "configure" },
        { label: "sources", to: "/sources" },
        { label: "new" },
      ]}
    >
      <ConnectSourcePage />
    </RequirePermission>
  )
}
