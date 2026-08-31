import type { SessionEnd } from "@/shared/api/mock/auth.store"

/**
 * Three ways to arrive at one screen.
 *
 * §1.3 and §16 name three arrivals — no session, a session that expired, and a
 * departure the operator chose — and they carry three different messages. They
 * are not three screens: the form, the mark and the provider button are
 * identical in all three, and only the sentence above them moves. So the
 * arrival is a search param on `/login` and the screen reads it, which also
 * means every landing is a URL somebody can paste into a ticket.
 */
export type LoginReason = SessionEnd

const REASONS: readonly LoginReason[] = ["expired", "signed-out"]

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

const COLD: LandingCopy = {
  kind: "cold",
  notice: null,
  lead: "Sign in to reach the dispatcher board.",
}

const LANDINGS: Record<LoginReason, LandingCopy> = {
  expired: {
    kind: "expired",
    notice: "Your session expired",
    lead: "Sign in again to pick up where you left off.",
  },
  "signed-out": {
    kind: "signed-out",
    notice: "You're signed out",
    lead: "Sign in again whenever you're ready.",
  },
}

export function landingFor(reason?: LoginReason): LandingCopy {
  return reason ? LANDINGS[reason] : COLD
}

/** Where a successful sign-in lands: back where they were, or the board. */
export function signInTarget(redirect?: string): string {
  return safeRedirect(redirect) ?? "/"
}
