/**
 * The two sections this screen holds, as the address bar spells them.
 *
 * In their own module rather than beside the screen that renders them because
 * the route validates a search parameter against this list before any component
 * exists — and because a file that exports both a component and a constant
 * loses fast refresh, which is the same reason `settings/model/tabs.ts` is its
 * own file.
 *
 * `gate` names the section by the word the product already says — "the gate",
 * the one the panel's own switch spells "run the gate on every run" — rather
 * than by the screen it used to be. `/verify` redirects here, so the word in
 * the address bar and the word on the tab are the same word.
 */
export const KNOWLEDGE_TABS = ["library", "gate"] as const

export type KnowledgeTab = (typeof KNOWLEDGE_TABS)[number]

/** Whether an unknown value off the URL names one of the two. */
export function isKnowledgeTab(value: unknown): value is KnowledgeTab {
  return (
    typeof value === "string" &&
    KNOWLEDGE_TABS.includes(value as KnowledgeTab)
  )
}
