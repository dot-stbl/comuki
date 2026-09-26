import type { SessionEnd } from "@/shared/api/mock/auth.store"
import { i18n } from "@/shared/i18n"

/**
 * The ways to arrive at one screen.
 *
 * §1.3 and §16 name three arrivals — no session, a session that expired, and
 * a departure the operator chose — and they carry three different messages.
 * The OIDC callback adds a fourth: the provider round trip itself failed, and
 * the host redirects to `/login?reason=oidc-failed&error=<code>` so the SPA
 * can say what happened without exposing internals. They are not four
 * screens: the form, the mark and the provider button are identical in all,
 * and only the sentence above them moves. So the arrival is a search param on
 * `/login` and the screen reads it, which also means every landing is a URL
 * somebody can paste into a ticket.
 */
export type LoginReason = SessionEnd | "oidc-failed"

const REASONS: readonly LoginReason[] = ["expired", "signed-out", "oidc-failed"]

export interface LoginSearch {
  /** Absent is the cold arrival — there is no `reason=cold`. */
  reason?: LoginReason
  /** Where the operator was headed, to be resumed after signing in. */
  redirect?: string
}

/**
 * A path this application will actually navigate to.
 *
 * The value arrives from the address bar, so it is attacker-controlled by
 * definition. Only an in-app absolute path survives: `//host` and `https://…`
 * are rejected because the router would happily send someone off-site with a
 * URL that still looked like ours, and a backslash is rejected because some
 * browsers normalise `/\evil.test` into a protocol-relative URL.
 */
export function safeRedirect(value: unknown): string | undefined {
  if (typeof value !== "string" || value.length === 0) {
    return undefined
  }
  if (!value.startsWith("/")) {
    return undefined
  }
  if (value.startsWith("//") || value.startsWith("/\\")) {
    return undefined
  }
  return value
}

/** The route's `validateSearch`: anything unrecognised is a cold arrival. */
export function parseLoginSearch(raw: Record<string, unknown>): LoginSearch {
  const reason = REASONS.find((entry) => entry === raw.reason)
  const redirect = safeRedirect(raw.redirect)

  return {
    ...(reason ? { reason } : {}),
    ...(redirect ? { redirect } : {}),
  }
}

export interface LandingCopy {
  kind: "cold" | LoginReason
  /**
   * The headline for the arrival, or `null` when there is nothing to announce.
   * A cold visitor is not told anything happened, because nothing did.
   */
  notice: string | null
  /** The line under it — always present, because the ask is always the same. */
  lead: string
}

/* The words resolve through the shared instance at call time (D7): the login
   screen asks on every render, so a locale switch re-words the landing. */
function landing(
  kind: LandingCopy["kind"],
  noticeKey: string | null,
  leadKey: string
): LandingCopy {
  return {
    kind,
    notice: noticeKey ? i18n.t(`auth:${noticeKey}`) : null,
    lead: i18n.t(`auth:${leadKey}`),
  }
}

export function landingFor(reason?: LoginReason): LandingCopy {
  if (reason === "expired") {
    return landing("expired", "landing.expiredNotice", "landing.expiredLead")
  }
  if (reason === "signed-out") {
    return landing(
      "signed-out",
      "landing.signedOutNotice",
      "landing.signedOutLead"
    )
  }
  if (reason === "oidc-failed") {
    return landing(
      "oidc-failed",
      "landing.oidcFailedNotice",
      "landing.oidcFailedLead"
    )
  }
  return landing("cold", null, "landing.coldLead")
}

/** Where a successful sign-in lands: back where they were, or the board. */
export function signInTarget(redirect?: string): string {
  return safeRedirect(redirect) ?? "/"
}
