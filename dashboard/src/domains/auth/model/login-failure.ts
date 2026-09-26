import { i18n } from "@/shared/i18n"

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
 * The product's own sentences live in the `auth` namespace and resolve
 * through the shared instance (D7); the host's own `detail`, when it carries
 * one, passes through verbatim (D11).
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
 * - 5xx — the host answered and the answer is that it is broken. Its own
 *   words here are "request failed 502", which tells the operator to check
 *   their password for something that has nothing to do with them.
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

const generic = () => i18n.t("auth:failure.generic")

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
      ? i18n.t("auth:failure.invalidCredentials")
      : generic()
  }
  if (status === 400) {
    return i18n.t("auth:failure.invalidEmail")
  }
  if (status === 429) {
    return i18n.t("auth:failure.tooMany")
  }
  if (typeof status === "number" && status >= 500) {
    return i18n.t("auth:failure.serviceDown")
  }
  if (error instanceof TypeError) {
    return i18n.t("auth:failure.unreachable")
  }
  if (error instanceof Error && error.message.length > 0) {
    return error.message
  }
  return generic()
}
