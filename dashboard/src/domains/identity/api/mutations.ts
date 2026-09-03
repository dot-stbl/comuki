import { useMutation, useQueryClient } from "@tanstack/react-query"

import { meQueryKey } from "@/domains/identity/api/queries"
import {
  mapLoginRequestFromInput,
  mapLoginResponseToSessionUser,
  mapMeResponseToSessionUser,
} from "@/domains/identity/api/mappers"
import { projectsQueryKey } from "@/domains/projects/api/queries"
import { postApiV1AuthLogin } from "@/shared/api/_generated/clients/postApiV1AuthLogin"
import { postApiV1AuthLogout } from "@/shared/api/_generated/clients/postApiV1AuthLogout"
import { getApiV1AuthMe } from "@/shared/api/_generated/clients/getApiV1AuthMe"
import {
  MOCK_REJECTION_MESSAGE,
  signInMock,
  signOutMock,
} from "@/shared/api/mock/auth.store"
import { env } from "@/shared/config/env"
import type { SessionUser } from "@/shared/session"

/**
 * Sign-in / sign-out against the host.
 *
 * Same mock-first pattern the runs mutations follow: `VITE_USE_MOCK=true`
 * keeps the operator's local dev / storybook flow through the hand-written
 * seed store; `VITE_USE_MOCK=false` routes through the kubb-generated
 * `postApiV1AuthLogin` / `postApiV1AuthLogout` clients, whose kubb-client
 * transport already sets `credentials: 'include'` so the host's cookie
 * session is written on the login response and cleared on logout.
 *
 * The session the rest of the dashboard reads is the `me` query — login and
 * logout both invalidate it (along with `projects`, whose permissions view
 * depends on the signed-in subject).
 */

async function login(email: string, password: string): Promise<SessionUser> {
  if (env.useMock) {
    const result = await signInMock({ identity: email, password })
    if (!result.ok) {
      throw new Error(result.message || MOCK_REJECTION_MESSAGE)
    }
    return result.user
  }

  const response = await postApiV1AuthLogin(
    mapLoginRequestFromInput(email, password),
  )

  // The login endpoint answers with the basic identity only (id, email,
  // displayName). The dashboard's `SessionUser` projection needs roles —
  // those come from the follow-up `/me` read.
  const welcome = mapLoginResponseToSessionUser(response)
  const me = await getApiV1AuthMe()
  const session = mapMeResponseToSessionUser(me)
  // Prefer the `/me` projection; fall back to the welcome payload if the
  // follow-up failed for any reason so the screen still has *something* to
  // render (the rail's avatar initial, the welcome banner).
  return session.id ? session : welcome
}

async function logout(): Promise<void> {
  if (env.useMock) {
    signOutMock()
    return
  }
  await postApiV1AuthLogout()
}

/**
 * The sign-in mutation.
 *
 * On success the `me` and `projects` queries are invalidated so the rail
 * reads the new roles on the next render and any cached project-permissions
 * view gets refetched against the fresh session. The mutation does not touch
 * the `me` cache directly — the host is the source of truth.
 */
export function useLoginMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ email, password }: { email: string; password: string }) =>
      login(email, password),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: meQueryKey }),
        queryClient.invalidateQueries({ queryKey: projectsQueryKey }),
      ])
    },
  })
}

/**
 * The sign-out mutation.
 *
 * Same invalidation as login — the `me` query must refetch (it will 401 and
 * the rail will read signed-out) and `projects` must re-resolve against a
 * guest subject.
 */
export function useLogoutMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: logout,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: meQueryKey }),
        queryClient.invalidateQueries({ queryKey: projectsQueryKey }),
      ])
    },
  })
}