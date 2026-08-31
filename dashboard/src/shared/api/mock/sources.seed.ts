import type { SeedStatus } from "./runs.seed"

/**
 * Where work comes from — the connections the swarm pulls tickets out of.
 *
 * Fictional, like every other seed in this folder: no host, account, token
 * fingerprint, match count or timestamp below came from a real installation.
 * The set is chosen to make the *awkward* states reachable without a backend
 * rather than to look like an average afternoon — a credential that was
 * revoked, a self-hosted instance, a watch whose filter admits nothing, and a
 * provider somebody turned off and left.
 *
 * The shape follows the FE requirements (§6 Sources):
 *
 *   - Five provider kinds, and **native is always present**: it is the
 *     product's own intake, so it cannot be connected (there is nothing to
 *     connect to) and it cannot be disconnected either.
 *   - A connection carries **auth appropriate to its kind** — a PAT, an OAuth
 *     grant, an app install — and a **base URL** when the instance is
 *     self-hosted. Cloud providers have nowhere to put one.
 *   - A watch carries an **admission mode**: `watch` puts matching tickets
 *     straight into the swarm, `inbox-only` puts them in the catalog for a
 *     human to claim, `both` does each.
 *   - The filter expression is **stored verbatim and never parsed**. See
 *     `SeedSourceWatch.filter`.
 *
 * A connection belongs to a project, by id, exactly as a run does: the duty
 * engineer watches the whole platform at once, so every list mixes projects and
 * every gated act on a row answers to *that row's* project rather than to the
 * session. The ids are the ones `session.seed.ts` hands the shift.
 */

/** The provider kinds v1 admits work from. `native` is the product's own. */
export const SOURCE_KINDS = [
  "github",
  "gitlab",
  "yandex-tracker",
  "jira",
  "native",
] as const

export type SeedSourceKind = (typeof SOURCE_KINDS)[number]

/** How a connection stands. Three words, from the requirements, verbatim. */
export const SOURCE_STATES = ["connected", "error", "disabled"] as const

export type SeedSourceState = (typeof SOURCE_STATES)[number]

/**
 * The credential behind a connection.
 *
 * `none` is native's, and it is not a gap: there is no remote system, so there
 * is nothing to authenticate against.
 */
export type SeedSourceAuth = "pat" | "oauth" | "app-install" | "none"

/** What a watch does with a ticket it admitted. */
export const ADMISSION_MODES = ["watch", "inbox-only", "both"] as const

export type SeedAdmissionMode = (typeof ADMISSION_MODES)[number]

/** One line of the write-back preview: a run status, and what the tracker gets. */
export interface SeedStatusMap {
  from: SeedStatus
  /** What the connector writes on the ticket. Prose — the provider's words. */
  to: string
}

export interface SeedSourceWatch {
  enabled: boolean
  /**
   * The filter expression, **stored verbatim and never parsed**.
   *
   * The requirements say "DSL TBD", and that is load-bearing rather than an
   * omission: nothing in the product has decided whether this is a jql-like
   * grammar, a label set, or something a connector compiles per provider. So
   * the mock stores a string, the UI shows a string, and nothing here or in the
   * domain tokenises, validates or normalises it. The day the language exists,
   * a parser goes *beside* this field and the stored strings are migrated —
   * which is a smaller job than unpicking a grammar that was invented here.
   *
   * An empty expression is a real value and means "admit everything the
   * connection can see". It is not an incomplete form.
   */
  filter: string
  mode: SeedAdmissionMode
  /**
   * Tickets this filter admitted in the last day. Fictional, like every figure
   * in this folder — and `0` on one connection on purpose: a watch that is on,
   * healthy and admitting nothing is the state that looks like a broken screen
   * and is not one.
   */
  matched: number
  /** Written back to the tracker, per this connection's provider. */
  mapping: SeedStatusMap[]
}

export interface SeedSourceConnection {
  id: string
  /** The project this connection feeds. An attribute of the row, not a mode. */
  projectId: string
  kind: SeedSourceKind
  /** What it points at, in the provider's own words — a repo, a queue, a key. */
  name: string
  state: SeedSourceState
  /**
   * Why it is in `error`, in the provider's own words. Present only in `error`,
   * and never a code on its own: "401" tells an operator nothing they can act
   * on, and the sentence is the whole reason the state is worth showing.
   */
  reason?: string
  auth: SeedSourceAuth
  /**
   * The instance, for a self-hosted provider. Absent for cloud and for native.
   * Kept beside `selfHosted` rather than derived from it because a cloud Jira
   * site also has a URL and it is not the same fact.
   */
  baseUrl?: string
  selfHosted: boolean
  /** The account or app the credential belongs to. Never the credential. */
  account: string
  /**
   * When the secret was stored, and the only thing the product ever says about
   * it again. **The secret itself is not in this file, is not in the store, and
   * is never rendered after the form that took it** — see `sources.store.ts`.
   */
  secretStoredAt?: string
  /**
   * Native refuses this. It is not a permission and not a missing feature: the
   * product's own intake has no remote end to disconnect from, and a platform
   * with no way to accept a ticket is not a state the product has.
   */
  removable: boolean
  /**
   * `null` on native, which is the honest shape rather than a disabled form:
   * there is no remote system to watch, so there is no filter, no admission
   * mode and nothing to map a status back onto. Native tickets arrive because
   * a person wrote one here.
   */
  watch: SeedSourceWatch | null
  /** Last successful poll. Absent when it has never completed one. */
  lastSyncAt?: string
}

export interface SeedNativeTicket {
  id: string
  projectId: string
  title: string
  body: string
  labels: string[]
  createdAt: string
  /** Created with "straight to work": a run was opened at the same moment. */
  straightToWork: boolean
}

export interface SeedSourcesSnapshot {
  connections: SeedSourceConnection[]
  tickets: SeedNativeTicket[]
}

/* ---------------------------------------------------------------------------
 * Write-back mappings, per provider.
 *
 * The preview the watch form shows is this table, read out loud. It is per
 * *kind* rather than per connection because the connector is what writes, and
 * a connector speaks one provider's vocabulary — a Jira transition is not a
 * GitHub label, and neither is a Yandex Tracker status key.
 * ------------------------------------------------------------------------- */

const GITHUB_MAPPING: SeedStatusMap[] = [
  { from: "queued", to: "label comuki:queued" },
  { from: "running", to: "label comuki:running" },
  { from: "waiting", to: "label comuki:needs-a-human" },
  { from: "escalated", to: "label comuki:escalated, assign the reporter" },
  { from: "success", to: "close the issue" },
  { from: "failed", to: "label comuki:failed, comment with the run link" },
]

const GITLAB_MAPPING: SeedStatusMap[] = [
  { from: "queued", to: "label comuki::queued" },
  { from: "running", to: "label comuki::running" },
  { from: "waiting", to: "label comuki::needs-a-human" },
  { from: "escalated", to: "label comuki::escalated" },
  { from: "success", to: "close the issue" },
  { from: "failed", to: "label comuki::failed, note with the run link" },
]

const JIRA_MAPPING: SeedStatusMap[] = [
  { from: "queued", to: "transition to to do" },
  { from: "running", to: "transition to in progress" },
  { from: "waiting", to: "transition to in review" },
  { from: "escalated", to: "transition to blocked, flag the issue" },
  { from: "success", to: "transition to done" },
  { from: "failed", to: "stay in progress, comment with the run link" },
]

const TRACKER_MAPPING: SeedStatusMap[] = [
  { from: "queued", to: "status open" },
  { from: "running", to: "status inProgress" },
  { from: "waiting", to: "status needInfo" },
  { from: "escalated", to: "status blocked" },
  { from: "success", to: "status closed, resolution fixed" },
  { from: "failed", to: "status inProgress, comment with the run link" },
]

/**
 * Every mapping the product knows, by kind. `native` is deliberately empty:
 * the ticket lives here, so its status *is* the run's status and there is
 * nowhere to write it back to. The form says that in words rather than showing
 * an empty table, which would read as a mapping that failed to load.
 */
export const STATUS_MAPPINGS: Record<SeedSourceKind, SeedStatusMap[]> = {
  github: GITHUB_MAPPING,
  gitlab: GITLAB_MAPPING,
  jira: JIRA_MAPPING,
  "yandex-tracker": TRACKER_MAPPING,
  native: [],
}

/**
 * The auth a kind actually offers.
 *
 * A closed list per provider, because the form must not be able to ask for a
 * credential the connector cannot use: GitHub takes a PAT or an app install,
 * a self-hosted GitLab takes a PAT, Yandex Tracker takes an OAuth grant, Jira
 * takes an api token in the PAT slot. Native takes nothing at all, and is not
 * offered by the connect form for that reason.
 */
export const AUTH_BY_KIND: Record<SeedSourceKind, SeedSourceAuth[]> = {
  github: ["pat", "app-install"],
  gitlab: ["pat", "oauth"],
  "yandex-tracker": ["oauth"],
  jira: ["pat"],
  native: ["none"],
}

/** The kinds that can be self-hosted, and so may carry a base URL. */
export const SELF_HOSTED_KINDS: SeedSourceKind[] = ["gitlab", "jira"]

/* ---------------------------------------------------------------------------
 * The connections themselves. Five provider kinds across three projects, plus
 * one native intake per project — native is per project because intake is, and
 * because it makes "the row that refuses to be disconnected" a fact about every
 * project rather than a single special case sitting at the bottom of one list.
 * ------------------------------------------------------------------------- */

const CONNECTIONS: SeedSourceConnection[] = [
  {
    id: "src_gh_comuki",
    projectId: "p_comuki",
    kind: "github",
    name: "comuki/web-app",
    state: "connected",
    auth: "app-install",
    selfHosted: false,
    account: "comuki-swarm (app install 41822)",
    secretStoredAt: "2026-07-02",
    removable: true,
    lastSyncAt: "4 min ago",
    watch: {
      enabled: true,
      filter: "labels: swarm, area/dashboard\nprojects: web-app",
      mode: "both",
      matched: 14,
      mapping: GITHUB_MAPPING,
    },
  },
  {
    id: "src_yt_comuki",
    projectId: "p_comuki",
    kind: "yandex-tracker",
    // The queue key is what an operator reads back in the tracker, so it is
    // the connection's name rather than the prose title of the queue.
    name: "comuki",
    state: "disabled",
    auth: "oauth",
    selfHosted: false,
    account: "swarm@comuki.local",
    secretStoredAt: "2026-05-18",
    removable: true,
    lastSyncAt: "12 aug",
    watch: {
      enabled: false,
      filter: "queue: comuki\nassignee: empty()",
      mode: "inbox-only",
      matched: 0,
      mapping: TRACKER_MAPPING,
    },
  },
  {
    id: "src_gl_plexor",
    projectId: "p_plexor",
    kind: "gitlab",
    name: "plexor/identity-svc",
    state: "connected",
    auth: "pat",
    baseUrl: "https://git.plexor.internal",
    selfHosted: true,
    account: "svc-comuki",
    secretStoredAt: "2026-08-11",
    removable: true,
    lastSyncAt: "9 min ago",
    watch: {
      // On, healthy, and admitting nothing. The label was renamed in the
      // tracker three weeks ago and nobody changed the expression here.
      enabled: true,
      filter: "labels: agent-ready\nprojects: identity-svc, notify-svc",
      mode: "watch",
      matched: 0,
      mapping: GITLAB_MAPPING,
    },
  },
  {
    id: "src_jira_atlas",
    projectId: "p_atlas",
    kind: "jira",
    name: "atlas",
    state: "error",
    reason:
      "401 from atlas.atlassian.net — the api token was revoked on 24 aug. reconnect with a new one.",
    auth: "pat",
    baseUrl: "https://atlas.atlassian.net",
    selfHosted: false,
    account: "comuki-intake@atlas.example",
    secretStoredAt: "2026-06-30",
    removable: true,
    lastSyncAt: "24 aug",
    watch: {
      enabled: true,
      filter: 'jql: project = atlas and labels = swarm and status != "done"',
      mode: "inbox-only",
      matched: 0,
      mapping: JIRA_MAPPING,
    },
  },
  {
    id: "src_gh_atlas",
    projectId: "p_atlas",
    kind: "github",
    name: "atlas/checkout-web",
    state: "connected",
    auth: "pat",
    selfHosted: false,
    account: "atlas-release-bot",
    secretStoredAt: "2026-08-19",
    removable: true,
    lastSyncAt: "1 min ago",
    watch: {
      enabled: true,
      filter: "labels: swarm\nprojects: checkout-web, ledger-core",
      mode: "inbox-only",
      matched: 31,
      mapping: GITHUB_MAPPING,
    },
  },
  {
    id: "src_native_comuki",
    projectId: "p_comuki",
    kind: "native",
    name: "native intake",
    state: "connected",
    auth: "none",
    selfHosted: false,
    account: "comuki",
    removable: false,
    watch: null,
  },
  {
    id: "src_native_plexor",
    projectId: "p_plexor",
    kind: "native",
    name: "native intake",
    state: "connected",
    auth: "none",
    selfHosted: false,
    account: "comuki",
    removable: false,
    watch: null,
  },
  {
    id: "src_native_atlas",
    projectId: "p_atlas",
    kind: "native",
    name: "native intake",
    state: "connected",
    auth: "none",
    selfHosted: false,
    account: "comuki",
    removable: false,
    watch: null,
  },
]

/**
 * Tickets somebody typed here rather than in a tracker. The app names are the
 * ones the run seed already uses, so a native ticket and a run can name the
 * same application without the two seeds disagreeing about whose project it is.
 */
const TICKETS: SeedNativeTicket[] = [
  {
    id: "nt_4120",
    projectId: "p_comuki",
    title: "search-idx drops the last shard on a cold start",
    body: "Reproducible on a fresh volume: the tenth shard never registers and the query planner reads nine. Suspect the readiness probe fires before the last mmap lands.",
    labels: ["search-idx", "bug"],
    createdAt: "2026-08-28",
    straightToWork: true,
  },
  {
    id: "nt_4131",
    projectId: "p_atlas",
    title: "ledger-core: money columns print with a float tail",
    body: "The export writes 12.340000000000001 for a value stored as a decimal. Formatting bug, not a storage one — the row in the database is right.",
    labels: ["ledger-core", "reporting"],
    createdAt: "2026-08-29",
    straightToWork: false,
  },
  {
    id: "nt_4142",
    projectId: "p_plexor",
    title: "notify-svc retries a delivered webhook after a 502 on the ack",
    body: "The receiver acknowledged and then the ack itself 502'd at the edge. We retry, they get it twice. Needs an idempotency key on the delivery, not a longer backoff.",
    labels: ["notify-svc", "reliability"],
    createdAt: "2026-08-30",
    straightToWork: false,
  },
]

export const SOURCES_SEED: SeedSourcesSnapshot = {
  connections: CONNECTIONS,
  tickets: TICKETS,
}
