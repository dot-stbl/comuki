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
 *  - `settingsJson` (the runtime config — kind/host/auth/account)
 *  - `secretEnvRef` (where the credential lives; never the secret itself)
 *  - `webhookPath` (the public URL the provider posts to)
 *  - `enabled` (the connection's own on/off; not the watch's)
 *
 * The domain needs that and more — `state`, `auth`, `account`, `selfHosted`,
 * `baseUrl`, `lastSyncAt`, `removable`, `watch`. The mapper fills the
 * unmodelled fields with the **honest defaults**: a connection that says
 * nothing about its watch has `watch: null` (the screen already knows how
 * to render that — the native branch's empty state), and a connection
 * that says nothing about `account` / `baseUrl` / `auth` gets the empty
 * string and `"none"`. The screen already treats empty as a valid answer
 * in those positions; what it cannot recover is **runtime state** the host
 * hasn't sent, so `lastSyncAt` stays `undefined` (the screen renders "never")
 * and `reason` stays `undefined` (the screen says "the provider refused,
 * and said nothing useful.").
 *
 * When a future host endpoint carries the runtime state — last sync, watch
 * config, the provider's own account — the mapper grows a few lines. The
 * contract here is "what the screen renders against the view the host
 * hands us today"; the contract is not "the host is dumb".
 */
export function sourceConnectionViewToConnection(
  view: SourceConnectionView
): SourceConnection {
  const kind = providerToKind(view.provider)
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
    // The wire carries `secretEnvRef` (where the credential lives) but not
    // the kind. Native is the only kind the host answers "none" for — every
    // provider worth a connection is a token of some flavour — so the
    // domain default is the right read until the host starts sending it.
    auth: kind === "native" ? ("none" as SourceAuth) : ("pat" as SourceAuth),
    // `settingsJson` is the runtime config; today the dashboard does not
    // read it, so `baseUrl` is left undefined (the host renders "cloud").
    baseUrl: undefined,
    // Self-hosted is the form for `baseUrl` present; without it we cannot
    // tell from the wire whether the connection is cloud or self-hosted, so
    // the screen defaults to cloud via `connectionHost`.
    selfHosted: false,
    account: "",
    secretStoredAt: undefined,
    // Native refuses disconnection at the store; the host mirrors that.
    removable: kind !== "native",
    watch: null,
    lastSyncAt: undefined,
  }
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
