/**
 * The seven sections this screen holds, as the address bar spells them.
 *
 * In their own module rather than beside the screen that renders them because
 * the route validates a search parameter against this list before any component
 * exists — and because a file that exports both a component and a constant
 * loses fast refresh, which is the same reason `identity/model/tabs.ts` is its
 * own file.
 */
export const SETTINGS_TABS = [
  "apps",
  "rules",
  "autonomy",
  "routing",
  "budgets",
  "keys",
  "tracker",
] as const

export type SettingsTab = (typeof SETTINGS_TABS)[number]

/** Whether an unknown value off the URL names one of the seven. */
export function isSettingsTab(value: unknown): value is SettingsTab {
  return (
    typeof value === "string" && SETTINGS_TABS.includes(value as SettingsTab)
  )
}
