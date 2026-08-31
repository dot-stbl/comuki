import type { SourceKind } from "@/domains/sources/model/types"

/**
 * Field names each provider's tickets are known to carry.
 *
 * Emphatically **not a grammar**. This is a list of nouns the connectors have
 * been observed to accept somewhere, offered so an operator writing an
 * expression does not have to guess whether this tracker calls it `labels` or
 * `tags`. There is no operator here, no separator, no precedence and no
 * quoting rule, because none of those has been decided — see
 * `filter-expression-field.tsx`.
 *
 * A plain `.ts` module rather than a constant beside the component, because a
 * `.tsx` that exports a component may export nothing else.
 */
export const FILTER_FIELD_NAMES: Record<SourceKind, string[]> = {
  github: ["labels", "repo", "assignee", "milestone", "state"],
  gitlab: ["labels", "projects", "assignee", "milestone", "state"],
  jira: ["jql", "project", "labels", "status", "assignee"],
  "yandex-tracker": ["queue", "tags", "assignee", "status"],
  // Native has no watch at all, so it never reaches the field. Present so the
  // record is total and nothing has to guard the lookup.
  native: [],
}
