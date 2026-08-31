import { createFileRoute, useNavigate } from "@tanstack/react-router"

import { RequirePermission } from "@/app/layout/require-permission"
import { SettingsPage, isSettingsTab, type SettingsTab } from "@/domains/settings"

export interface SettingsSearch {
  /**
   * Which of the seven sections is showing. In the URL rather than in component
   * state because a control-plane section is a thing one operator sends
   * another — "your budget cap is here" is a link, not a set of directions —
   * and because a save that refetches must not drop the operator back on the
   * first tab.
   *
   * Optional, and that is load-bearing rather than lazy: `/settings` is a rail
   * destination, and making the parameter required would force every one of
   * those call sites to name a section in order to link to the screen at all.
   * Absent means the first section.
   */
  tab?: SettingsTab
}

export const Route = createFileRoute("/settings")({
  validateSearch: (search: Record<string, unknown>): SettingsSearch =>
    isSettingsTab(search.tab) ? { tab: search.tab } : {},
  component: RouteComponent,
})

function RouteComponent() {
  const { tab = "apps" } = Route.useSearch()
  const navigate = useNavigate()

  return (
    <RequirePermission
      permission="settings.live"
      title="Settings"
      crumbs={[{ label: "settings" }]}
    >
      <SettingsPage
        tab={tab}
        onTabChange={(next) => {
          // `replace` because moving between sections is not a step worth
          // pressing back through — the back button should leave the screen.
          void navigate({
            to: "/settings",
            search: { tab: next },
            replace: true,
          })
        }}
      />
    </RequirePermission>
  )
}
