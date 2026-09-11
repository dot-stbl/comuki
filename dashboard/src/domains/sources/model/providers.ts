import type {
  AdmissionMode,
  NativeTicket,
  ProviderKey,
  SourceAuth,
  SourceConnection,
} from "@/domains/sources/model/types"
import type { BrandId } from "@/shared/ui"

/**
 * **The provider registry.** One row per tracker this build has learned, and
 * one honest answer for every tracker it has not.
 *
 * There used to be three of these. `sources` held five closed kinds and six
 * exhaustive `Record<SourceKind, …>` tables; `tasks` held the same five under
 * one different name (`manual` for `native`) and three more tables, with a
 * comment saying it mirrored the sources half *on purpose*; `inbox` had given
 * up and typed its ticket's provider as a bare `string` with the vocabulary in
 * a doc comment. Adding a sixth provider cost eleven files and two dozen edit
 * points, and forgetting one of them was not a compile error — it was an empty
 * cell on a table row, which is what actually shipped.
 *
 * So the catalogue is an array, and the array is the schema. A provider is one
 * `Provider` literal: every field is required, so a half-added provider fails
 * to compile *at the entry*, rather than at whichever of nine tables somebody
 * remembered. The three domains read this one array — the sources table and
 * its forms, the intake cards and the backlog badge, the inbox ticket's
 * source — and there is nothing left for them to drift against.
 *
 * ## The open edge is the shape, not a convention
 *
 * `ProviderKey` is `string` because the set of trackers is the *host's* fact
 * (see the type's own note). The only way into this registry is
 * `providerOf(key)`, which answers `Provider | null`; every reader below is a
 * function with a stated fallback rather than a table somebody could index
 * with a key it has no row for. An unknown provider therefore renders the
 * host's own word everywhere — the precedent is `BrandTag`, which has had an
 * honest "write it in words" branch since `yandex-tracker` needed one.
 *
 * `BrandId` stays closed and is reached through `brand: BrandId | null`. That
 * set is closed because the artwork is: a mark whose geometry does not exist
 * cannot be drawn, and guessing at one is inventing a trademark.
 *
 * ## Adding a provider
 *
 * One entry in `PROVIDERS`. Nothing else in `src/domains` — the marks are the
 * kit's (`brand-marks.ts`, and `null` is a real answer), and the mock host
 * keeps its own vocabulary on the other side of the wire on purpose.
 */

/**
 * One provider, and everything the three domains between them need to say
 * about it.
 *
 * Every field is required. That is the whole mechanism: the exhaustiveness
 * check that used to live on nine `Record` types now lives on one object
 * literal, where a reader can see all of it at once and a compiler can insist
 * on all of it at once.
 */
export interface Provider {
  /**
   * The kebab-case word the host, the webhook route segment and every screen
   * agree on. Not a display string — see `label`.
   */
  key: ProviderKey
  /**
   * What a surface calls it, in the product's lower-case voice. The mark's
   * accessible name, the filter option, the hover reading, and the whole of
   * the cell when there is no mark.
   */
  label: string
  /**
   * The mark it is drawn as, or `null` for one that is spelled instead.
   *
   * `null` is a first-class answer and not a gap. `yandex-tracker` takes it
   * because Yandex publishes no monochrome Tracker mark and the product glyph
   * is carried by its colour — drained to `currentColor` it is a shape nobody
   * can name rather than a quieter version of itself, and redrawing somebody's
   * trademark from memory is worse than spelling their name. An unknown
   * provider takes the same branch for the same reason.
   */
  brand: BrandId | null
  /**
   * The credentials this connector implements, in the order a form offers
   * them; the first is the default. A closed list per provider, because a form
   * must not be able to ask for a credential the connector cannot use.
   */
  auth: SourceAuth[]
  /**
   * Is there a remote system behind this provider?
   *
   * `false` for exactly one row — the product's own intake — and it is the fact
   * five separate `kind === "native"` comparisons used to spell for themselves.
   * Nothing to point a credential at (so the connect form does not offer it),
   * nothing to disconnect from, nothing to watch, and nowhere to write a status
   * back to.
   */
  remote: boolean
  /** Can an instance be self-hosted, and so carry a base url? */
  selfHostable: boolean
  /**
   * What the thing a connection points at is called, in this provider's own
   * word. A repository, a project key and a queue key are three different
   * objects, and a form that called all three "name" would be asking the
   * operator to translate on the way in.
   */
  target: string
  /** A shape for the target box, in this provider's own spelling. */
  targetPlaceholder: string
  /**
   * Field names this provider's tickets are known to carry.
   *
   * Emphatically **not a grammar**. A list of nouns the connectors have been
   * observed to accept somewhere, offered so an operator writing a filter does
   * not have to guess whether this tracker calls it `labels` or `tags`. There
   * is no operator here, no separator, no precedence and no quoting rule,
   * because none of those has been decided — see `filter-expression-field.tsx`.
   */
  filterFields: string[]
  /**
   * The one line an intake card says about picking this provider.
   *
   * Two facts, both load-bearing: how work normally arrives from this provider
   * (through a connection's watch — never through the intake form), and what
   * writing one down by hand does (stamps it as that provider's, which is all
   * the backlog reads).
   */
  intakeNote: string
}

/** The product's own intake, by key. The one row with no remote end. */
export const NATIVE_PROVIDER: ProviderKey = "native"

/**
 * Every provider this build has learned, in the order the requirements list
 * them — and the order the intake cards and the provider filter offer them.
 */
export const PROVIDERS: readonly Provider[] = [
  {
    key: "github",
    label: "github",
    brand: "github",
    auth: ["pat", "app-install"],
    remote: true,
    selfHostable: false,
    target: "repository",
    targetPlaceholder: "owner/repo",
    filterFields: ["labels", "repo", "assignee", "milestone", "state"],
    intakeNote:
      "issues land from a watched repository. one written here is stamped as the repo's.",
  },
  {
    key: "gitlab",
    label: "gitlab",
    brand: "gitlab",
    auth: ["pat", "oauth"],
    remote: true,
    selfHostable: true,
    target: "repository",
    targetPlaceholder: "group/project",
    filterFields: ["labels", "projects", "assignee", "milestone", "state"],
    intakeNote:
      "issues land from a watched repository. one written here is stamped as the repo's.",
  },
  {
    key: "yandex-tracker",
    label: "yandex tracker",
    /* Spelled, not drawn. See `brand` on `Provider`. */
    brand: null,
    auth: ["oauth"],
    remote: true,
    selfHostable: false,
    target: "queue key",
    targetPlaceholder: "comuki",
    filterFields: ["queue", "tags", "assignee", "status"],
    intakeNote:
      "tickets land from a watched tracker queue. one written here is stamped as the queue's.",
  },
  {
    key: "jira",
    label: "jira",
    brand: "jira",
    /* An api token, in the PAT slot. */
    auth: ["pat"],
    remote: true,
    selfHostable: true,
    target: "project key",
    targetPlaceholder: "atlas",
    filterFields: ["jql", "project", "labels", "status", "assignee"],
    intakeNote:
      "tickets land from a watched board. one written here is stamped as the board's.",
  },
  {
    key: NATIVE_PROVIDER,
    label: "native",
    /* Not a third party, so no third-party mark: it takes the product's own
       container, meaning the same thing it means in the topbar. Borrowing an
       unrelated vendor's glyph would be a lie and a generic inbox icon would
       say less than the mark this product already owns. */
    brand: "comuki",
    auth: ["none"],
    remote: false,
    selfHostable: false,
    target: "name",
    targetPlaceholder: "",
    /* No watch, so the field never renders. Empty because there is nothing to
       filter, not because nobody filled it in. */
    filterFields: [],
    intakeNote:
      "written here, by a person — the product's own intake, with no tracker behind it.",
  },
]

const BY_KEY: ReadonlyMap<ProviderKey, Provider> = new Map(
  PROVIDERS.map((provider) => [provider.key, provider])
)

/**
 * The providers a connect form may offer.
 *
 * Derived from `remote` rather than from a name: native is missing because
 * there is nothing to point a credential at, which is the reason, and every
 * project already has one.
 */
export const CONNECTABLE_PROVIDERS: readonly Provider[] = PROVIDERS.filter(
  (provider) => provider.remote
)

/**
 * The credentials offered for a provider nobody has written a row for.
 *
 * Not a guess at what it accepts — an admission that we do not know. The form
 * offers every credential the dashboard can render rather than pretending to a
 * closed list, and `pat` leads because it is what the mapper already assumes
 * for a provider whose `settingsJson` did not say.
 */
const UNKNOWN_PROVIDER_AUTH: SourceAuth[] = ["pat", "oauth", "app-install"]

/**
 * The registry row for a key, or `null` when this build has never met it.
 *
 * `null` and not a thrown error, not a cast, and not a synthesised row: the
 * host naming a provider the dashboard has not learned is an ordinary Tuesday,
 * and a fabricated row would have to invent an auth list and an intake
 * sentence nobody wrote.
 */
export function providerOf(key: ProviderKey): Provider | null {
  return BY_KEY.get(key) ?? null
}

/** Has this build learned this provider? */
export function isKnownProvider(key: ProviderKey): boolean {
  return BY_KEY.has(key)
}

/**
 * The provider's name as a surface says it: the product's word for one it
 * knows, and the host's own word for one it does not. Never an empty cell.
 */
export function providerLabel(key: ProviderKey): string {
  return providerOf(key)?.label ?? key
}

/**
 * The mark for a provider, or `null` for one this kit has no honest mark for —
 * which `BrandTag` reads as "write it in words". An unknown provider takes that
 * branch too: drawing somebody's trademark from memory and inventing one for a
 * provider we have never seen are the same mistake.
 */
export function providerBrand(key: ProviderKey): BrandId | null {
  return providerOf(key)?.brand ?? null
}

/**
 * Is this the product's own intake rather than somebody else's tracker?
 *
 * An unknown provider answers `false`: there is a remote end, we just do not
 * know whose. Degrading the other way would make an unknown connection
 * unremovable and hide its credential fields.
 */
export function isNativeIntake(key: ProviderKey): boolean {
  return providerOf(key)?.remote === false
}

/** The credentials a form may offer for this provider. */
export function providerAuth(key: ProviderKey): SourceAuth[] {
  return providerOf(key)?.auth ?? UNKNOWN_PROVIDER_AUTH
}

/**
 * The credential kind a form is *actually* holding, given the provider chosen.
 *
 * Derived rather than synced, and the difference matters: changing the provider
 * must not be able to leave a form holding a credential that provider's
 * connector cannot use, and an effect that corrected it afterwards would fire
 * in whatever order React felt like — which is a form that is briefly wrong and
 * a save that is occasionally wrong. Asked at render, it cannot be either.
 */
export function effectiveAuth(
  key: ProviderKey,
  auth: SourceAuth
): SourceAuth {
  const allowed = providerAuth(key)
  return allowed.includes(auth) ? auth : allowed[0]
}

/**
 * May this provider carry a base url?
 *
 * `false` for an unknown provider, which is the form's answer and not the
 * row's: a connection that already has a `baseUrl` says so on itself, and
 * `ConnectionForm` asks the connection before it asks the registry.
 */
export function needsBaseUrl(key: ProviderKey): boolean {
  return providerOf(key)?.selfHostable ?? false
}

/** What the thing a connection points at is called. */
export function targetLabel(key: ProviderKey): string {
  return providerOf(key)?.target ?? "name"
}

/** A shape for the target box, in the provider's own spelling. */
export function targetPlaceholder(key: ProviderKey): string {
  return providerOf(key)?.targetPlaceholder ?? ""
}

/**
 * Field names to offer beside a filter expression.
 *
 * Empty for a provider the dashboard has not learned: the list is a set of
 * nouns *this* connector has been observed to accept, and offering github's to
 * an unknown tracker would be a guess dressed as a fact.
 */
export function filterFields(key: ProviderKey): string[] {
  return providerOf(key)?.filterFields ?? []
}

/**
 * The line an intake card says about picking this provider.
 *
 * The generic sentence for an unknown one says the same two things the written
 * ones do, minus the noun nobody can supply.
 */
export function intakeNote(key: ProviderKey): string {
  return (
    providerOf(key)?.intakeNote ??
    "tickets land from a watched connection. one written here is stamped as that provider's."
  )
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
  if (isNativeIntake(connection.kind)) {
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
  if (isNativeIntake(connection.kind)) {
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
