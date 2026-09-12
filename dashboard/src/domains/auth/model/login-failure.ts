/**
 * Turning a rejected sign-in into a sentence a person can act on.
 *
 * The kubb transport throws an `Error` carrying `status` and `data` (the
 * host's ProblemDetails — `code`, `detail`); before this existed, the screen
 * showed the raw `message` ("auth boundary 401", "Failed to fetch"), which
 * is a log line, not an answer. The mock branch still throws plain `Error`
 * with copy that is already human — it falls through to the message
 * unchanged.
 *
 * The mappings follow what the host actually answers on
 * `POST /api/v1/auth/login`:
 *
 * - 401 + `auth.invalid_credentials` — unknown user and wrong password read
 *   identically on purpose (no account enumeration); the screen must too.
 * - 400 — `LoginValidator` refusing a non-address ("not an email" is the
 *   only validation a correct form can still hit, e.g. a paste with a
 *   trailing space).
 * - 429 — the login rate limiter.
 * - `TypeError` — fetch's network failure ("Failed to fetch"): the server
 *   was never reached, which is a different problem from a refused login.
 */

/** What the kubb transport attaches to a non-2xx rejection. */
interface TransportFailure {
  status?: number
  data?: {
    code?: string
    detail?: string
  }
}

const INVALID_CREDENTIALS = "auth.invalid_credentials"

const GENERIC = "Sign-in failed. Check the address and try again."

export function loginFailureMessage(error: unknown): string {
  const failure = error as TransportFailure | null
  const status = failure?.status
  const code = failure?.data?.code

  if (status === 401) {
    // The host answers refused credentials with this code; a code-less 401
    // (proxy, gateway) is still a refusal to look at. Any other code is a
    // 401 this screen has no sentence for yet — the generic fallback, not
    // a raw "auth boundary 401".
    return code === INVALID_CREDENTIALS || code === undefined
      ? "Incorrect email or password"
      : GENERIC
  }
  if (status === 400) {
    return "Enter a valid email address"
  }
  if (status === 429) {
    return "Too many attempts — try again in a minute"
  }
  if (error instanceof TypeError) {
    return "Cannot reach the server — check your connection"
  }
  if (error instanceof Error && error.message.length > 0) {
    return error.message
  }
  return GENERIC
}
