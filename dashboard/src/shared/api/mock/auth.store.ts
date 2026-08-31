import type { SessionUser } from "@/shared/session"

import { SESSION_USER_SEED } from "./session.seed"

/**
 * Mutable mock session.
 *
 * Same shape as `runs.store.ts`, and for the same reason: a seed is a constant,
 * and a constant cannot record a decision. `Sign out` was a navigation with
 * nothing behind it — the rail landed on `/login` while the shell still held a
 * signed-in shift, so leaving and arriving looked identical. This holds the one
 * fact the guard reads, so signing out actually ends something.
 *
 * Session-scoped and in-memory by design: a reload is a fresh shift, and the
 * fresh shift starts *signed in*, the way a real cookie would still be there.
 * The alternative — starting signed out — would put a sign-in in front of every
 * reload of every screen while the board is still being built, and would tell
 * the operator their session expired when nothing had.
 */

export interface MockOidcProvider {
  id: string
  /** What the button says after "Continue with". */
  label: string
}

/**
 * How the last session ended, which is the whole difference between the second
 * and third landing on the sign-in screen. `null` means there has not been one
 * to end — a cold arrival.
 */
export type SessionEnd = "expired" | "signed-out"

export interface MockAuthState {
  /** The signed-in shift, or `null`. */
  user: SessionUser | null
  endedBy: SessionEnd | null
  /** `null` when no identity provider is configured — then no OIDC button. */
  oidc: MockOidcProvider | null
}

export type SignInResult =
  | { ok: true; user: SessionUser }
  | { ok: false; message: string }

export interface MockCredentials {
  /** Email or username — the field accepts either. */
  identity: string
  password: string
}

/**
 * The refusal trigger.
 *
 * Every other credential is accepted, so without this the error state could be
 * written but never seen, and an unreachable state is an unverified one. It is
 * named in the screen's own mock note rather than hidden, because the note is
 * already admitting the whole screen is invented.
 */
export const MOCK_REJECTED_PASSWORD = "fail"

export const MOCK_REJECTION_MESSAGE =
  "Those credentials were refused. Check the address and try again."

/** Enough latency for the busy state to be a real state rather than a branch. */
export const MOCK_SIGN_IN_LATENCY_MS = 150

const DEFAULT_OIDC: MockOidcProvider = { id: "comuki-oidc", label: "OIDC" }

/**
 * Nobody, with no roles anywhere.
 *
 * The shell needs *a* session object to render at all, and handing it the
 * seeded duty engineer while signed out would be a fabricated identity. This
 * grants nothing, so anything that slips past the guard shows a closed screen
 * rather than someone else's name.
 */
export const SIGNED_OUT_USER: SessionUser = {
  id: "",
  name: "",
  email: "",
  platformRoles: [],
  projectRoles: {},
}

function signedIn(): MockAuthState {
  return { user: SESSION_USER_SEED, endedBy: null, oidc: DEFAULT_OIDC }
}

let state: MockAuthState = signedIn()

const listeners = new Set<() => void>()

/** One object per change, so `useSyncExternalStore` can compare by reference. */
function commit(next: MockAuthState): void {
  state = next
  for (const listener of listeners) {
    listener()
  }
}

export function getMockAuth(): MockAuthState {
  return state
}

export function isMockSignedIn(): boolean {
  return state.user !== null
}

export function subscribeMockAuth(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms)
  })
}

/**
 * Any credentials are accepted, and they sign you in as the seeded duty
 * engineer rather than as whoever was typed: inventing a name and a role set
 * from an email box would be inventing an identity, and the seeds in this
 * folder are marked as invented precisely so nothing downstream mistakes them
 * for a person.
 */
export async function signInMock(
  credentials: MockCredentials
): Promise<SignInResult> {
  await delay(MOCK_SIGN_IN_LATENCY_MS)

  if (credentials.password === MOCK_REJECTED_PASSWORD) {
    return { ok: false, message: MOCK_REJECTION_MESSAGE }
  }

  commit(signedIn())
  return { ok: true, user: SESSION_USER_SEED }
}

/** The identity provider's round trip, minus the round trip. */
export async function signInWithOidcMock(): Promise<SignInResult> {
  await delay(MOCK_SIGN_IN_LATENCY_MS)
  commit(signedIn())
  return { ok: true, user: SESSION_USER_SEED }
}

/** Deliberate departure: the third landing, and the quiet one. */
export function signOutMock(): void {
  commit({ ...state, user: null, endedBy: "signed-out" })
}

/**
 * Thrown out mid-shift: the second landing.
 *
 * No UI raises this — the real one arrives as a 401 from a request nobody made
 * on purpose — so it exists for tests, stories and anyone who wants to see the
 * landing without waiting for a token to age out.
 */
export function expireMockSession(): void {
  commit({ ...state, user: null, endedBy: "expired" })
}

export function getMockOidcProvider(): MockOidcProvider | null {
  return state.oidc
}

/**
 * Both states of §16's "if configured" are real, and switchable: a screen that
 * can only ever be photographed with the OIDC button present has not actually
 * been built for the tenant that has no identity provider.
 */
export function setMockOidcProvider(provider: MockOidcProvider | null): void {
  commit({ ...state, oidc: provider })
}

/** Back to the seeded shift — used by tests and stories. */
export function resetMockAuth(): void {
  commit(signedIn())
}

/**
 * A browser that has never held a session: no user, and nothing to explain.
 *
 * Unreachable from the UI while this store boots signed in, which is exactly
 * why it is exported — the cold arrival is the first of §1.3's three and it
 * still has to be testable, and it becomes the ordinary state the day a real
 * cookie is what decides.
 */
export function clearMockAuth(): void {
  commit({ ...state, user: null, endedBy: null })
}
