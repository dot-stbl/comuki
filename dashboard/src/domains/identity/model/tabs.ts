/**
 * The three lists this section holds, as the address bar spells them.
 *
 * In their own module rather than beside the screen that renders them because
 * the route validates a search parameter against this list before any component
 * exists — and because a file that exports both a component and a constant
 * loses fast refresh, which is the same reason `shared/ui/form/ids.ts` is its
 * own file.
 */
export const IDENTITY_TABS = ["users", "grants", "keys"] as const

export type IdentityTab = (typeof IDENTITY_TABS)[number]

/** Whether an unknown value off the URL names one of the three. */
export function isIdentityTab(value: unknown): value is IdentityTab {
  return (
    typeof value === "string" && IDENTITY_TABS.includes(value as IdentityTab)
  )
}
