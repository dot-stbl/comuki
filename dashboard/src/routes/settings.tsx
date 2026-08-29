import { createFileRoute } from "@tanstack/react-router"

import { SettingsPage } from "@/domains/settings"

export const Route = createFileRoute("/settings")({
  component: SettingsPage,
})
