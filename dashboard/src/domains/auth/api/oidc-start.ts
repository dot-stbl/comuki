/**
 * Browser-driven OIDC start: hand the navigation to the browser.
 *
 * The host answers `GET /api/v1/auth/oidc/{provider}/start` with a 302
 * to the configured IdP's authorize endpoint. The IdP then redirects
 * the browser to the host's unified callback
 * (`/api/v1/auth/oidc/callback`), which exchanges the code, signs the
 * cookie, and answers a 302 to either the in-app `returnTo` path or
 * `/`. The full handshake is server-side: the SPA never sees the
 * callback directly.
 *
 * Two paths were on the table:
 *
 * 1. **`useStartOidcQuery` through kubb-client** — kubb follows the 302
 *    with `fetch()`, which lands on the IdP's HTML page and is then
 *    `await response.json()`-ed. The IdP page is HTML, not JSON; kubb
 *    throws. Wrong path.
 * 2. **`fetch(..., { redirect: 'manual' })`** — read the 302's `Location`
 *    header and then `window.location.assign` it. Two round-trips where
 *    one would do.
 *
 * The path below is the third one: let the browser do the whole dance.
 * `window.location.assign` issues the navigation; the browser follows the
 * 302 to the IdP, follows the IdP's 302 back to the host's callback, and
 * lands on `returnTo` with a cookie set. One helper call, no `fetch`,
 * no parsing of redirect headers.
 *
 * Tests mock this helper — the production code never calls it directly.
 */

const OIDC_PROVIDER_START_PATH = (provider: string): string =>
  `/api/v1/auth/oidc/${provider}/start`

/**
 * Hands the browser to the host's OIDC start endpoint for `provider`.
 *
 * Returns `void` because it does not return: the page unloads before any
 * JavaScript could observe the result. The host's cookie middleware sets
 * the session; the SPA picks it up on the next render via `/api/v1/auth/me`.
 */
export function startOidcFlow(
  baseUrl: string,
  provider: string,
  returnTo?: string,
): void {
  const path = OIDC_PROVIDER_START_PATH(provider)
  const target =
    returnTo && returnTo.startsWith("/") && !returnTo.startsWith("//") && !returnTo.startsWith("/\\")
      ? `${baseUrl}${path}?returnTo=${encodeURIComponent(returnTo)}`
      : `${baseUrl}${path}`

  window.location.assign(target)
}