import type { Status } from "@/shared/ui"

/**
 * Sources as the screen sees them: where work comes from.
 *
 * One record, and it carries two things that look like one — the **connection**
 * (a credential pointed at a remote system) and its **watch** (what that
 * connection is allowed to admit). They are separate because they fail
 * separately: a revoked token breaks the connection and leaves the watch
 * perfectly well specified, and a filter that matches nothing leaves the
 * connection healthy. A screen that folded them into one status would have to
 * pick which of those two an operator was looking at.
 */

/** The provider kinds v1 admits work from. `native` is the product's own. */
export type SourceKind =
  "github" | "gitlab" | "yandex-tracker" | "jira" | "native"

/**
 * How a connection stands. The requirements' three words, verbatim.
 *
 * Deliberately not a run status and deliberately not spelled like one: a
 * connection is never queued or escalated, and borrowing that vocabulary would
 * make the badge lie about what kind of thing it is describing.
 */
export type SourceState = "connected" | "error" | "disabled"

/** The credential behind a connection. `none` is native's, and is not a gap. */
export type SourceAuth = "pat" | "oauth" | "app-install" | "none"

/** What a watch does with a ticket it admitted. */
export type AdmissionMode = "watch" | "inbox-only" | "both"

/** One line of the write-back preview: a run status, and what the tracker gets. */
export interface StatusMap {
  from: Status
  to: string
}

export interface SourceWatch {
  enabled: boolean
  /**
   * The filter expression, **held as a string and never parsed**.
   *
   * The requirements say "DSL TBD". Nothing in this domain tokenises,
   * validates, completes or normalises this value — see
   * `sources/ui/filter-expression-field.tsx` for the field that edits it and
   * for why inventing a grammar here would be the expensive mistake.
   */
  filter: string
  mode: AdmissionMode
  /** Tickets this filter admitted in the last day. `0` is a real answer. */
  matched: number
  mapping: StatusMap[]
}

export interface SourceConnection {
  id: string
  /** The project this connection feeds. An attribute of the row, not a mode. */
  projectId: string
  kind: SourceKind
  name: string
  state: SourceState
  /** Why it is in `error`, in the provider's own words. `error` only. */
  reason?: string
  auth: SourceAuth
  /** The instance, for a self-hosted provider. Absent for cloud and native. */
  baseUrl?: string
  selfHosted: boolean
  account: string
  /**
   * The env-var NAME that holds the credential. The dashboard never sees the
   * value; the host resolves it at probe and webhook time. Absent on native
   * (no remote end to authenticate against) and on legacy rows that were
   * written before the SecretReference rewrite.
   */
  secretEnvRef?: string
  /**
   * When the secret was stored — and the only thing the product ever says about
   * it again. There is no field here for the secret itself, anywhere in the
   * domain, which is what makes "shown once" structural rather than polite.
   * Carried by the mock seed only; the wire (post-#38) names the env var
   * instead of marking a date.
   */
  secretStoredAt?: string
  /** Native refuses to be disconnected. See `sources.store.ts`. */
  removable: boolean
  /** `null` on native: there is no remote system to watch. */
  watch: SourceWatch | null
  lastSyncAt?: string
}

export interface NativeTicket {
  id: string
  projectId: string
  title: string
  body: string
  labels: string[]
  createdAt: string
  straightToWork: boolean
}

export interface SourcesSnapshot {
  connections: SourceConnection[]
  tickets: NativeTicket[]
}

/** The answer a test-connection gives, either way. */
export interface ProbeResult {
  ok: boolean
  message: string
}
