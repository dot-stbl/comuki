import type {
  AdmissionMode,
  NativeTicket,
  SourceAuth,
  SourceConnection,
  SourceKind,
} from "@/domains/sources/model/types"
import type { BrandId } from "@/shared/ui"

/**
 * What a provider is called, what it can be asked for, and what it means.
 *
 * The vocabulary lives here rather than in the components because three
 * surfaces read it — the table's filter options, the connect form's selects and
 * the watch form's prose — and three copies of a word list is how a word list
 * drifts.
 */

/** Every kind, in the order the requirements list them. */
export const SOURCE_KINDS: SourceKind[] = [
  "github",
  "gitlab",
  "yandex-tracker",
  "jira",
  "native",
]

/**
 * The kinds a connect form may offer. Native is missing on purpose: it is the
 * product's own intake, it is already present on every project, and there is
 * nothing to point a credential at.
 */
export const CONNECTABLE_KINDS: SourceKind[] = SOURCE_KINDS.filter(
  (kind) => kind !== "native"
)

export const SOURCE_KIND_LABEL: Record<SourceKind, string> = {
  github: "github",
  gitlab: "gitlab",
  "yandex-tracker": "yandex tracker",
  jira: "jira",
  native: "native",
}

/**
 * The mark each provider is shown as, or `null` for the one that is spelled.
 *
 * A provider that publishes a mark is drawn as that mark: an operator scanning
 * a column of sources recognises the octocat before they read the word beside
 * it, and the word was costing a column of width to say something the glyph
 * says instantly. The name never leaves — it is the mark's accessible name and
 * its hover reading — but it stops being the thing on screen.
 *
 * Two of the five are not marks:
 *
 * - **`yandex-tracker`** is `null` on purpose. Yandex publishes no monochrome
 *   Tracker mark, and the product glyph is carried by its colour; drained to
 *   `currentColor` it is a shape nobody can name rather than a quieter version
 *   of itself. The honest options were to spell the provider or to redraw
 *   somebody's trademark from memory, and only one of those is honest. It
 *   renders its name, which is why `BrandTag` takes a label in both branches.
 * - **`native`** is not a third party and has no third-party mark. It takes the
 *   product's own container — the same mark as the topbar, meaning the same
 *   thing it means there: a Comuki worker, a Comuki intake. Borrowing an
 *   unrelated vendor's glyph would be a lie and a generic inbox icon would say
 *   less than the mark this product already owns.
 */
export const SOURCE_KIND_BRAND: Record<SourceKind, BrandId | null> = {
  github: "github",
  gitlab: "gitlab",
  "yandex-tracker": null,
  jira: "jira",
  native: "comuki",
}

/**
 * Why native intake refuses to be disconnected, in one sentence and one
 * spelling.
 *
 * Not a permission and not an unbuilt control: the product's own intake has no
 * remote end to disconnect from, and a platform that cannot accept a ticket is
 * not a state this product has. Two surfaces say it now — the row's button and
 * the source's own page — and the refusal is also enforced in
 * `sources.store.ts`, which is the half an operator never reads. Two copies of
 * a sentence is how a sentence drifts.
 */
export const NATIVE_DISCONNECT_REFUSAL =
  "native intake cannot be disconnected — it is the product's own way of accepting a ticket"

export const AUTH_LABEL: Record<SourceAuth, string> = {
  pat: "personal access token",
  oauth: "oauth grant",
  "app-install": "app install",
  none: "none",
}

/**
 * The auth each kind actually offers.
 *
 * A closed list per provider, because the form must not be able to ask for a
 * credential the connector cannot use.
 */
export const AUTH_BY_KIND: Record<SourceKind, SourceAuth[]> = {
  github: ["pat", "app-install"],
  gitlab: ["pat", "oauth"],
  "yandex-tracker": ["oauth"],
  jira: ["pat"],
  native: ["none"],
}

/**
 * The credential kind a form is *actually* holding, given the provider chosen.
 *
 * Derived rather than synced, and the difference matters: changing the provider
 * must not be able to leave a form holding a credential that provider's
 * connector cannot use, and an effect that corrected it afterwards would fire
 * in whatever order React felt like — which is a form that is briefly wrong and
 * a save that is occasionally wrong. Asked at render, it cannot be either.
 *
 * Two forms ask it now — the connect form on `/sources/new` and the connection
 * region on a source's own page — which is exactly why it stopped being a line
 * inside one of them.
 */
export function effectiveAuth(kind: SourceKind, auth: SourceAuth): SourceAuth {
  const allowed = AUTH_BY_KIND[kind]
  return allowed.includes(auth) ? auth : allowed[0]
}

/** The kinds that can be self-hosted, and so may carry a base URL. */
export const SELF_HOSTED_KINDS: SourceKind[] = ["gitlab", "jira"]

export function needsBaseUrl(kind: SourceKind): boolean {
  return SELF_HOSTED_KINDS.includes(kind)
}

/** What the secret box is called for this credential, in the provider's words. */
export function secretLabel(auth: SourceAuth): string {
  switch (auth) {
    case "pat":
      return "access token"
    case "oauth":
      return "oauth client secret"
    case "app-install":
      return "app private key"
    default:
      return "secret"
  }
}

/**
 * What the thing a connection points at is called, in the provider's own word.
 *
 * A repository, a project key and a queue key are three different objects, and
 * a form that called all three "name" would be asking the operator to translate
 * on the way in.
 */
export function targetLabel(kind: SourceKind): string {
  switch (kind) {
    case "github":
    case "gitlab":
      return "repository"
    case "jira":
      return "project key"
    case "yandex-tracker":
      return "queue key"
    default:
      return "name"
  }
}

/** A shape for the box, in the provider's own spelling. */
export function targetPlaceholder(kind: SourceKind): string {
  switch (kind) {
    case "github":
      return "owner/repo"
    case "gitlab":
      return "group/project"
    case "jira":
      return "atlas"
    case "yandex-tracker":
      return "comuki"
    default:
      return ""
  }
}

/**
 * The three admission modes, each with the sentence that tells them apart.
 *
 * They are genuinely three different products for the same ticket, and the
 * difference is who moves next: `watch` hands it to the swarm, `inbox-only`
 * hands it to a person, `both` does each. A control that showed three words and
 * no sentences would be asking the operator to guess which.
 */
export const ADMISSION_MODES: {
  value: AdmissionMode
  label: string
  description: string
}[] = [
  {
    value: "watch",
    label: "watch",
    description: "a matching ticket starts a run. nobody has to claim it.",
  },
  {
    value: "inbox-only",
    label: "inbox-only",
    description:
      "a matching ticket lands in the catalog and waits for a person to take it.",
  },
  {
    value: "both",
    label: "both",
    description:
      "the ticket starts a run and stays in the catalog, so the run can be found by the ticket that caused it.",
  },
]

export const ADMISSION_LABEL: Record<AdmissionMode, string> = {
  watch: "watch",
  "inbox-only": "inbox-only",
  both: "both",
}

/**
 * The instance a connection talks to, as the operator would name it.
 *
 * A self-hosted instance is its host; a cloud provider has no instance to name
 * and says so; native has no remote end at all. The three are different facts
 * and the column shows three different words rather than one blank.
 */
export function connectionHost(connection: SourceConnection): string {
  if (connection.kind === "native") {
    return "in-platform"
  }
  if (!connection.baseUrl) {
    return "cloud"
  }
  try {
    return new URL(connection.baseUrl).host
  } catch {
    return connection.baseUrl
  }
}

/**
 * How this connection describes its admission, for the table's one-line cell.
 * Native has no watch, and the honest word for that is not "off".
 */
export function admissionLabel(connection: SourceConnection): string {
  if (!connection.watch) {
    return "native intake"
  }
  if (!connection.watch.enabled) {
    return "watch off"
  }
  return ADMISSION_LABEL[connection.watch.mode]
}

/**
 * How many tickets this connection put in front of somebody.
 *
 * A watch counts what its filter admitted; native counts the tickets people
 * filed in it. Different arithmetic, same question, so it is one column.
 */
export function admittedCount(
  connection: SourceConnection,
  tickets: NativeTicket[]
): number {
  if (connection.watch) {
    return connection.watch.enabled ? connection.watch.matched : 0
  }
  return tickets.filter((ticket) => ticket.projectId === connection.projectId)
    .length
}

/**
 * The one line that says what is actually going on with this row.
 *
 * Ordered by what an operator needs first: a failure states its reason, a watch
 * that admits nothing says so (that is the state that looks like a broken
 * screen and is not one), a disabled connection says when it stopped, and a
 * healthy one says when it last synced. Never a blank cell — a blank reads as a
 * render that failed rather than as a row with nothing to report.
 */
export function connectionNote(
  connection: SourceConnection,
  tickets: NativeTicket[]
): string {
  if (connection.state === "error") {
    return connection.reason ?? "the provider refused, and said nothing useful."
  }
  if (connection.kind === "native") {
    const count = admittedCount(connection, tickets)
    return count === 1 ? "1 ticket filed here" : `${count} tickets filed here`
  }
  if (connection.state === "disabled") {
    return `turned off — last synced ${connection.lastSyncAt ?? "never"}`
  }
  if (connection.watch && !connection.watch.enabled) {
    return "watch off — nothing is being admitted from here"
  }
  if (connection.watch && connection.watch.matched === 0) {
    return "the filter matched nothing in the last day"
  }
  return `last synced ${connection.lastSyncAt ?? "never"}`
}

/**
 * Labels for a native ticket, split off the one line the form collects.
 *
 * This is a comma-separated list and nothing more — it is emphatically *not*
 * the filter language. Splitting on a comma is a fact about a text box; the
 * filter expression next door is left whole precisely because nobody has
 * decided what its separators mean yet.
 */
export function parseTicketLabels(input: string): string[] {
  return input
    .split(",")
    .map((label) => label.trim())
    .filter((label) => label.length > 0)
}
