import type { SourceConnectionView } from "@/shared/api/_generated/types/SourceConnectionView"
import type {
  SourceAuth,
  SourceConnection,
  SourceKind,
} from "@/domains/sources/model/types"

/**
 * The seam between the host's `SourceConnectionView` and the domain's richer
 * `SourceConnection`. Mock mode needs none of this — the seed store and the
 * domain type agree on shape — but the host's view is intentionally smaller:
 * it carries the connection's identity, the project's it belongs to and a
 * flat `settingsJson` blob the client parses only when it has a use for it.
 *
 * The wire `SourceConnectionView` carries:
 *  - `id`, `projectId`, `provider`, `name`
 *  - `settingsJson` (the runtime config — kind/host/auth/account, all
 *    provider-specific)
 *  - `secretEnvRef` (where the credential lives; never the secret itself)
 *  - `webhookPath` (the public URL the provider posts to)
 *  - `enabled` (the connection's own on/off; not the watch's)
 *
 * The domain needs that and more — `state`, `auth`, `account`, `selfHosted`,
 * `baseUrl`, `lastSyncAt`, `removable`, `watch`. `settingsJson` carries
 * `{auth, account, baseUrl}` for the v1 connectors; the mapper parses them
 * so the screen can show the same fields it showed in mock mode without a
 * second source of truth. The runtime-only fields the host has not sent
 * stay at the screen's honest defaults — `watch: null`, `reason: undefined`,
 * `lastSyncAt: undefined` (rendered as "the provider refused, and said
 * nothing useful." and "never").
 *
 * When a future host endpoint carries the runtime state — last sync, watch
 * config, the provider's own account — the mapper grows a few lines. The
 * contract here is "what the screen renders against the view the host
 * hands us today"; the contract is not "the host is dumb".
 */

/**
 * The v1 `settingsJson` shape the dashboard reads. Per-provider fields
 * live outside this contract — a self-hosted GitLab carries its
 * `baseUrl` here; a cloud GitHub does not — but the four the dashboard
 * cares about are stable.
 */
interface ParsedSettings {
  auth?: SourceAuth
  account?: string
  baseUrl?: string
}

function parseSettings(settingsJson: string): ParsedSettings {
  const trimmed = settingsJson.trim()
  if (trimmed.length === 0) {
    return {}
  }
  try {
    const parsed: unknown = JSON.parse(trimmed)
    if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
      return {}
    }
    const result: ParsedSettings = {}
    const auth = (parsed as Record<string, unknown>).auth
    if (auth === "pat" || auth === "oauth" || auth === "app-install") {
      result.auth = auth
    }
    const account = (parsed as Record<string, unknown>).account
    if (typeof account === "string") {
      result.account = account
    }
    const baseUrl = (parsed as Record<string, unknown>).baseUrl
    if (typeof baseUrl === "string" && baseUrl.length > 0) {
      result.baseUrl = baseUrl
    }
    return result
  } catch {
    return {}
  }
}

export function sourceConnectionViewToConnection(
  view: SourceConnectionView
): SourceConnection {
  const kind = providerToKind(view.provider)
  const settings = parseSettings(view.settingsJson)

  return {
    id: view.id,
    projectId: view.projectId,
    kind,
    name: view.name,
    state: view.enabled ? "connected" : "disabled",
    // A wire connection has no error message — the screen renders its own
    // fallback when this is absent, and the screen does the same in mock mode
    // when the seed forgets one. A future `/sources/{id}/status` endpoint
    // would carry the real reason; the mapper only widens.
    reason: undefined,
    // Native has no credential kind on the wire — the host returns the empty
    // string for `secretEnvRef`. Every other kind reads its `auth` out of
    // settingsJson and falls back to `"pat"`, the connector's default token
    // shape for the v1 providers the dashboard knows about.
    auth: kind === "native"
      ? ("none" as SourceAuth)
      : (settings.auth ?? ("pat" as SourceAuth)),
    // Self-hosted instances carry their `baseUrl` in settings; cloud and
    // native do not. The mapper leaves `baseUrl` undefined when absent so
    // `connectionHost` renders "cloud" / "in-platform" honestly.
    baseUrl: settings.baseUrl,
    selfHosted: settings.baseUrl !== undefined && settings.baseUrl.length > 0,
    account: settings.account ?? "",
    // The wire carries the env-var name; native has none. Empty on native
    // is the wire's own contract — the host returns "" rather than omitting
    // the field — and `undefined` keeps the rest of the screens honest.
    secretEnvRef:
      view.secretEnvRef.length > 0 && kind !== "native"
        ? view.secretEnvRef
        : undefined,
    secretStoredAt: undefined,
    // Native refuses disconnection at the store; the host mirrors that.
    removable: kind !== "native",
    watch: null,
    lastSyncAt: undefined,
  }
}

/**
 * The reverse direction — domain write payload → wire `settingsJson`.
 *
 * The dashboard collects `auth`, `account`, `baseUrl` as separate fields on
 * the connect / update screens and folds them into one JSON object here.
 * Provider-specific extras (e.g. GitHub's `includePullRequests`) stay in
 * their own textarea on the screen and ride in via a second JSON shape that
 * the connector later merges; that path is not in scope for the v1.
 */
export function settingsToJson(settings: {
  auth?: SourceAuth
  account?: string
  baseUrl?: string
}): string {
  const object: Record<string, string> = {}
  if (settings.auth !== undefined) {
    object.auth = settings.auth
  }
  if (settings.account !== undefined && settings.account.length > 0) {
    object.account = settings.account
  }
  if (settings.baseUrl !== undefined && settings.baseUrl.length > 0) {
    object.baseUrl = settings.baseUrl
  }
  return JSON.stringify(object)
}

function providerToKind(provider: string): SourceKind {
  // The host's vocabulary and the dashboard's agree today; the cast is the
  // form. A future provider the dashboard does not yet know about would
  // arrive as `unknown` here — caught by `SOURCE_KIND_BRAND` / `SOURCE_KIND_LABEL`
  // rendering as `undefined`. Until then, the trust is local.
  switch (provider) {
    case "github":
    case "gitlab":
    case "jira":
    case "yandex-tracker":
    case "native":
      return provider
    default:
      // Unknown provider — leave the row on the screen with the host's own
      // word rather than failing closed; the brand tag and the label map
      // both degrade to a spelled fallback in `providers.ts`.
      return provider as SourceKind
  }
}

/**
 * The full snapshot the screens read. In real mode tickets are empty —
 * `GET /api/v1/tickets` is not on the wire — and the screen already knows
 * "no tickets yet" is a valid answer for the rows that show a count.
 */
export function sourceConnectionViewsToSnapshot(
  views: SourceConnectionView[]
): { connections: SourceConnection[]; tickets: [] } {
  return {
    connections: views.map(sourceConnectionViewToConnection),
    tickets: [],
  }
}
