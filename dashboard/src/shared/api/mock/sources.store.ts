import {
  SELF_HOSTED_KINDS,
  SOURCES_SEED,
  STATUS_MAPPINGS,
  type SeedAdmissionMode,
  type SeedNativeTicket,
  type SeedSourceAuth,
  type SeedSourceConnection,
  type SeedSourceKind,
  type SeedSourcesSnapshot,
  type SeedSourceWatch,
} from "./sources.seed"

/**
 * Mutable mock store for the source connections.
 *
 * The seed is a constant, and a query whose `queryFn` maps a constant can never
 * show the result of a decision: the refetch that follows a mutation restores
 * the constant and the optimistic write disappears about 200 ms later, which
 * looks exactly like a bug and is one. This holds the connections' live state
 * for the session so connecting a source, turning a watch off or filing a
 * native ticket actually sticks — the same thing the real endpoint will do,
 * minus the wire. Same pattern as `runs.store.ts` and `compute.store.ts`.
 *
 * Session-scoped and in-memory by design: a reload is a fresh shift.
 *
 * **No secret is ever held here.** `probeSeedSourceDraft` is the only function
 * that sees one, it takes it as an argument, it returns a sentence, and it
 * keeps nothing — which is what makes "shown once, never again" a fact about
 * the code rather than a promise in a tooltip.
 */

function cloneWatch(watch: SeedSourceWatch | null): SeedSourceWatch | null {
  if (!watch) {
    return null
  }
  return { ...watch, mapping: watch.mapping.map((entry) => ({ ...entry })) }
}

function cloneConnection(entry: SeedSourceConnection): SeedSourceConnection {
  return { ...entry, watch: cloneWatch(entry.watch) }
}

function clone(snapshot: SeedSourcesSnapshot): SeedSourcesSnapshot {
  return {
    connections: snapshot.connections.map(cloneConnection),
    tickets: snapshot.tickets.map((ticket) => ({
      ...ticket,
      labels: [...ticket.labels],
    })),
  }
}

let state: SeedSourcesSnapshot = clone(SOURCES_SEED)

export function readSeedSources(): SeedSourcesSnapshot {
  return state
}

/** What a connect form has collected, minus the secret. */
export interface SeedSourceDraft {
  projectId: string
  kind: SeedSourceKind
  name: string
  auth: SeedSourceAuth
  account: string
  /** Only meaningful for a self-hosted kind. */
  baseUrl: string
}

/** The answer a test-connection gives, either way. */
export interface SeedProbeResult {
  ok: boolean
  /** A sentence, never a bare code: "401" tells nobody what to do next. */
  message: string
}

function hostOf(baseUrl: string, fallback: string): string {
  try {
    return new URL(baseUrl).host
  } catch {
    return fallback
  }
}

/**
 * Test a connection that does not exist yet.
 *
 * Deterministic on its input, because the whole point of a test-connection is
 * that it answers a question about *these* details — a mock that failed at
 * random would teach the operator to press it twice. The three failures are the
 * three the connectors actually return first: an unnamed instance, a plaintext
 * endpoint, and a credential the provider refused.
 *
 * The secret is read here and nowhere else, and this function returns a
 * sentence rather than storing anything.
 */
export function probeSeedSourceDraft(
  draft: SeedSourceDraft,
  secret: string
): SeedProbeResult {
  const needsHost = SELF_HOSTED_KINDS.includes(draft.kind)
  const trimmed = draft.baseUrl.trim()

  if (needsHost && trimmed.length === 0) {
    return {
      ok: false,
      message:
        "no base url — a self-hosted instance has to be named before anything can reach it.",
    }
  }
  if (trimmed.length > 0 && !trimmed.startsWith("https://")) {
    return {
      ok: false,
      message:
        "plain http refused — the credential would cross the wire in the clear.",
    }
  }
  const host = trimmed.length > 0 ? hostOf(trimmed, trimmed) : cloudHost(draft.kind)
  if (secret.trim().length < 8) {
    return {
      ok: false,
      message: `401 from ${host} — the credential was rejected.`,
    }
  }
  return {
    ok: true,
    message: `reached ${host} and authenticated as ${draft.account || "the stored account"}.`,
  }
}

/** The endpoint a cloud provider answers on, for the sentence's sake. */
function cloudHost(kind: SeedSourceKind): string {
  switch (kind) {
    case "github":
      return "api.github.com"
    case "gitlab":
      return "gitlab.com"
    case "yandex-tracker":
      return "api.tracker.yandex.net"
    case "jira":
      return "atlassian.net"
    default:
      return "this instance"
  }
}

/**
 * Test a connection that already exists.
 *
 * It re-uses the stored credential, which the product has and this mock does
 * not — so the answer is derived from the state the connection is already in:
 * a connection in `error` fails again with the same sentence (the token is
 * still revoked; pressing test does not un-revoke it), and any other answers
 * and moves its last-sync forward. A success is a real write: it survives the
 * refetch, which is the difference between a probe and an animation.
 */
export function probeSeedConnection(connectionId: string): SeedProbeResult {
  const connection = state.connections.find((entry) => entry.id === connectionId)
  if (!connection) {
    return { ok: false, message: "this connection is gone." }
  }
  if (connection.kind === "native") {
    return {
      ok: true,
      message: "native intake is the product's own — there is nothing to reach.",
    }
  }
  if (connection.state === "error") {
    return { ok: false, message: connection.reason ?? "the provider refused." }
  }

  const host = connection.baseUrl
    ? hostOf(connection.baseUrl, connection.baseUrl)
    : cloudHost(connection.kind)

  state = {
    ...state,
    connections: state.connections.map((entry) =>
      entry.id === connectionId ? { ...entry, lastSyncAt: "just now" } : entry
    ),
  }

  return {
    ok: true,
    message: `reached ${host} and authenticated as ${connection.account}.`,
  }
}

/**
 * Add a connection.
 *
 * It lands `connected` with a fresh watch that is **off**, admitting nothing:
 * a source that starts pulling tickets into the swarm the instant it is saved
 * would be a connect form with a hidden second act in it. Turning the watch on
 * is a separate, visible decision on the row.
 */
export function connectSeedSource(draft: SeedSourceDraft): SeedSourceConnection {
  const selfHosted =
    SELF_HOSTED_KINDS.includes(draft.kind) && draft.baseUrl.trim().length > 0
  const connection: SeedSourceConnection = {
    id: `src_${draft.kind.replace(/-/g, "")}_${Date.now().toString(36)}`,
    projectId: draft.projectId,
    kind: draft.kind,
    name: draft.name,
    state: "connected",
    auth: draft.auth,
    baseUrl: draft.baseUrl.trim() || undefined,
    selfHosted,
    account: draft.account,
    // The date the secret was taken, and the last thing the product will ever
    // say about it. The secret itself never reached this function.
    secretStoredAt: "just now",
    removable: true,
    lastSyncAt: "just now",
    watch: {
      enabled: false,
      filter: "",
      mode: "inbox-only",
      matched: 0,
      mapping: STATUS_MAPPINGS[draft.kind],
    },
  }

  state = { ...state, connections: [...state.connections, connection] }
  return connection
}

/** What the connection form on a source's own page may change. */
export interface SeedConnectionPatch {
  /** Only meaningful for a self-hosted kind; the empty string clears it. */
  baseUrl: string
  account: string
  auth: SeedSourceAuth
}

/**
 * Edit a connection that already exists.
 *
 * Three fields and no fourth, and the fourth is the point: **there is no
 * `secret` on this patch**, in the same way there is no `secret` on a
 * connection. A credential is written once, by the form that took it, and
 * changing one is reconnecting rather than editing — so the shape of this
 * function is what makes "shown once, never again" structural instead of
 * polite. The same argument as `connectSeedSource`, one act later.
 *
 * `selfHosted` is recomputed rather than patched: it is a derivation of the
 * kind and whether an instance was named, and letting a caller set it directly
 * is how a cloud connection ends up flagged self-hosted with nowhere to point.
 * `name` is not here either — what a connection points at is what it *is*, and
 * repointing it at another repository is a different connection with the same
 * row id.
 */
export function updateSeedConnection(
  connectionId: string,
  patch: SeedConnectionPatch
): void {
  const trimmed = patch.baseUrl.trim()
  state = {
    ...state,
    connections: state.connections.map((entry) => {
      if (entry.id !== connectionId) {
        return entry
      }
      const wantsHost = SELF_HOSTED_KINDS.includes(entry.kind)
      const baseUrl = wantsHost ? trimmed : ""
      return {
        ...entry,
        baseUrl: baseUrl.length > 0 ? baseUrl : undefined,
        selfHosted: wantsHost && baseUrl.length > 0,
        account: patch.account,
        auth: patch.auth,
      }
    }),
  }
}

/**
 * Remove a connection — and refuse when it is native.
 *
 * The refusal lives here as well as in the UI on purpose. It is not a
 * permission and not an unfinished screen: the product's own intake has no
 * remote end to disconnect from, and a platform that cannot accept a ticket is
 * not a state the product has. A rule that only existed in a disabled button
 * would be one refactor away from being gone.
 */
export function disconnectSeedSource(connectionId: string): boolean {
  const connection = state.connections.find((entry) => entry.id === connectionId)
  if (!connection || !connection.removable) {
    return false
  }
  state = {
    ...state,
    connections: state.connections.filter((entry) => entry.id !== connectionId),
  }
  return true
}

export interface SeedWatchPatch {
  enabled: boolean
  filter: string
  mode: SeedAdmissionMode
}

/**
 * Save a watch.
 *
 * `filter` goes in exactly as it was typed. Nothing here trims a clause,
 * normalises whitespace or rejects a token — see `SeedSourceWatch.filter` for
 * why the language is still undecided and why that is deliberate.
 */
export function updateSeedWatch(
  connectionId: string,
  patch: SeedWatchPatch
): void {
  state = {
    ...state,
    connections: state.connections.map((entry) => {
      if (entry.id !== connectionId || !entry.watch) {
        return entry
      }
      return {
        ...entry,
        // A watch that was turned off admits nothing from now on, and saying so
        // beats leaving yesterday's count under a switch that is off.
        state: entry.state === "error" ? "error" : "connected",
        watch: {
          ...entry.watch,
          enabled: patch.enabled,
          filter: patch.filter,
          mode: patch.mode,
          matched: patch.enabled ? entry.watch.matched : 0,
        },
      }
    }),
  }
}

export interface SeedTicketDraft {
  projectId: string
  title: string
  body: string
  /** Already split by the form — the store does not parse a labels string. */
  labels: string[]
  straightToWork: boolean
}

/** File a ticket in the product's own intake. */
export function createSeedNativeTicket(
  draft: SeedTicketDraft
): SeedNativeTicket {
  const ticket: SeedNativeTicket = {
    id: `nt_${Date.now().toString(36)}`,
    projectId: draft.projectId,
    title: draft.title,
    body: draft.body,
    labels: draft.labels,
    createdAt: "just now",
    straightToWork: draft.straightToWork,
  }
  state = { ...state, tickets: [ticket, ...state.tickets] }
  return ticket
}

/** Back to the seeded connections — used by tests and stories. */
export function resetSeedSources(): void {
  state = clone(SOURCES_SEED)
}
