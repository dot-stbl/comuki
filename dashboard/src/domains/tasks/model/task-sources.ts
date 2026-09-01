import type { BrandId } from "@/shared/ui"

import type { TaskSource } from "@/domains/tasks/model/types"

/**
 * What a task's `source` is called, how it is drawn, and what picking it
 * means — the vocabulary the intake cards, the backlog's badge and the mock
 * store all read, so the three cannot drift.
 *
 * The provider half mirrors `domains/sources/model/providers.ts` on purpose:
 * a task stamped `github` and a connection of kind `github` are two views of
 * one provider, and a product that spelled the same vendor two ways would be
 * teaching the operator two vocabularies for one idea.
 */

/** Every source a task may carry, in the order the intake offers them. */
export const TASK_SOURCES: TaskSource[] = [
  "github",
  "gitlab",
  "yandex-tracker",
  "jira",
  "manual",
]

/** The default stamp: the product's own intake, typed by a person. */
export const DEFAULT_TASK_SOURCE: TaskSource = "manual"

/** The word each card and each denial sentence uses, in this voice. */
export const TASK_SOURCE_LABEL: Record<TaskSource, string> = {
  github: "github",
  gitlab: "gitlab",
  "yandex-tracker": "yandex tracker",
  jira: "jira",
  manual: "manual",
}

/**
 * The mark each provider is drawn as, or `null` for the one that is spelled.
 *
 * The same decisions `SOURCE_KIND_BRAND` records for connections, with
 * `manual` taking the product's own container rather than `native`'s word:
 * a ticket typed into this form *is* the product's own intake, and that is
 * the meaning the topbar and the sources table already give this mark.
 *
 * `yandex-tracker` stays `null` — Yandex publishes no monochrome Tracker
 * mark, and the colour glyph drained to `currentColor` is a shape nobody
 * could name. The card row draws a lucide glyph in that slot instead (see
 * `task-source-cards.tsx`) so the row keeps its rhythm; the honest spelling
 * travels in the name, which a card always carries beside its mark.
 */
export const TASK_SOURCE_BRAND: Record<TaskSource, BrandId | null> = {
  github: "github",
  gitlab: "gitlab",
  "yandex-tracker": null,
  jira: "jira",
  manual: "comuki",
}

/**
 * The one line that says what picking this card means.
 *
 * Two facts, both load-bearing: how work normally arrives from this provider
 * (through a connection's watch — never through this form), and what writing
 * one down here does (stamps it as that provider's, which is all the backlog
 * reads — the badge in the identity column says where a ticket came from).
 */
export const TASK_SOURCE_NOTE: Record<TaskSource, string> = {
  github:
    "issues land from a watched repository. one written here is stamped as the repo's.",
  gitlab:
    "issues land from a watched repository. one written here is stamped as the repo's.",
  "yandex-tracker":
    "tickets land from a watched tracker queue. one written here is stamped as the queue's.",
  jira: "tickets land from a watched board. one written here is stamped as the board's.",
  manual:
    "written here, by a person — the product's own intake, with no tracker behind it.",
}
