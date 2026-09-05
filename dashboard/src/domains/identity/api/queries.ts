import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { buildIdentitySnapshot } from "@/domains/identity/model/identity"
import type {
  CreateApiKeyInput,
  GrantRoleInput,
  IdentitySnapshot,
  InviteUserInput,
  LinkOidcInput,
  SetUserDisabledInput,
} from "@/domains/identity/model/types"
import {
  createSeedApiKey,
  createSeedUser,
  grantSeedRole,
  linkSeedOidcSubject,
  listSeedApiKeys,
  listSeedRoleAssignments,
  listSeedUsers,
  revokeSeedApiKey,
  revokeSeedRole,
  setSeedUserDisabled,
} from "@/shared/api/mock/identity.store"
import { listSeedProjects } from "@/shared/api/mock/projects.store"
import { SESSION_USER_SEED } from "@/shared/api/mock/session.seed"
import { env } from "@/shared/config/env"
import type { SessionUser } from "@/shared/session"

import {
  mapMeResponseToSessionUser,
  mapOidcStartToAuthorizationUrl,
} from "./mappers"
import { getApiV1AuthMe } from "@/shared/api/_generated/clients/getApiV1AuthMe"
import { getApiV1AuthOidcProviderStart } from "@/shared/api/_generated/clients/getApiV1AuthOidcProviderStart"
import { postApiV1Grants } from "@/shared/api/_generated/clients/postApiV1Grants"
import { postApiV1GrantsGrantidRevoke } from "@/shared/api/_generated/clients/postApiV1GrantsGrantidRevoke"
import { postApiV1Keys } from "@/shared/api/_generated/clients/postApiV1Keys"
import { postApiV1KeysKeyidRevoke } from "@/shared/api/_generated/clients/postApiV1KeysKeyidRevoke"
import { postApiV1Users } from "@/shared/api/_generated/clients/postApiV1Users"
import { postApiV1UsersUseridOidcLink } from "@/shared/api/_generated/clients/postApiV1UsersUseridOidcLink"
import { patchApiV1UsersUserid } from "@/shared/api/_generated/clients/patchApiV1UsersUserid"

/**
 * Identity admin endpoints are mock-first until #31–#37 land.
 *
 * Real mode (`VITE_USE_MOCK=false`) for the session path is wired: `me`,
 * `oidc/{provider}/start`, login, logout. The seven admin mutations on this
 * page (invite, link OIDC, set disabled, grant role, revoke role, revoke key,
 * create key) all run against the seed store today; the read path throws
 * loudly when the seed is bypassed, so a misconfigured mock-off lands on the
 * empty state rather than producing phantom success. Each mutation carries its
 * follow-up issue reference on the per-function JSDoc.
 */

export const identityQueryKey = ["identity"] as const

/** The signed-in shift, read from the host's `/me`. */
export const meQueryKey = ["me"] as const

/** The OIDC start handshake, keyed by the provider the operator chose. */
export const oidcStartQueryKey = (provider: string) =>
  ["oidc-start", provider] as const

/**
 * The whole screen in one payload.
 *
 * Three lists that reference each other are fetched together on purpose: two
 * queries would let the screen show a grant against a key the other half had
 * already revoked, and the operator would be looking at two moments at once on
 * the one screen whose job is saying who holds what right now.
 *
 * It reads the mutable store rather than the seed constants — the reason that
 * store exists. A `queryFn` returning a module constant undoes an optimistic
 * write on the next refetch, and "revoke" that comes back two hundred
 * milliseconds later is worse than no revoke at all.
 */
function snapshot(): IdentitySnapshot {
  return buildIdentitySnapshot(
    listSeedUsers(),
    listSeedRoleAssignments(),
    listSeedApiKeys(),
    listSeedProjects(),
    new Date(),
  )
}

/**
 * Real-mode status: throws — there is no `GET /api/v1/identity` (or
 * `/api/v1/users` + `/api/v1/grants` + `/api/v1/keys`) endpoint on the host
 * today. The host's identity module exposes only the session endpoints
 * (`/api/v1/auth/{login,logout,me,oidc/{provider}/start,oidc/{provider}/callback}`)
 * per `openspec/specs/identity/spec.md`; the admin endpoints are tracked as
 * follow-up issues #31–#37. Mock-first is the contract until those land.
 */
async function loadIdentity(): Promise<IdentitySnapshot> {
  if (!env.useMock) {
    throw new Error(
      "identity API not implemented — set VITE_USE_MOCK=true (see issues #31–#37)",
    )
  }
  return snapshot()
}

/**
 * The signed-in subject, with platform roles and the empty `projectRoles`
 * map the wire carries (see `mapMeResponseToSessionUser`). Mock mode reads
 * the seeded duty engineer — the same shift the existing auth store hands
 * the rest of the dashboard.
 */
async function getCurrentUser(): Promise<SessionUser> {
  if (env.useMock) {
    return SESSION_USER_SEED
  }
  const me = await getApiV1AuthMe()
  return mapMeResponseToSessionUser(me)
}

/**
 * The OIDC redirect URL for the chosen provider.
 *
 * Mock mode returns a synthetic URL — the screen knows not to navigate to a
 * `mock://` host. Real mode calls the kubb-generated client; the response is
 * `any` because the endpoint answers a 302 (kubb follows redirects and the
 * body is whatever the IdP returned). The mapper surfaces a readable error
 * when the wire is not a string, and the screen is expected to use
 * `window.location.href` against the kubb route directly for the actual
 * browser redirect.
 */
async function startOidc(provider: string): Promise<string> {
  if (env.useMock) {
    return `mock://oidc/${provider}/start`
  }
  const start = await getApiV1AuthOidcProviderStart(provider)
  return mapOidcStartToAuthorizationUrl(start)
}

export function useIdentityQuery() {
  return useQuery({
    queryKey: identityQueryKey,
    queryFn: loadIdentity,
  })
}

export function useCurrentUserQuery(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: meQueryKey,
    queryFn: getCurrentUser,
    enabled: options?.enabled ?? true,
  })
}

export function useStartOidcQuery(provider: string) {
  return useQuery({
    queryKey: oidcStartQueryKey(provider),
    queryFn: () => startOidc(provider),
    enabled: provider.length > 0,
    // The start URL is a one-shot handshake, not live data — let it sit in
    // the cache for the session rather than refetching on every focus.
    staleTime: Infinity,
    retry: false,
  })
}

/**
 * Every mutation here ends the same way: the snapshot the store now holds.
 *
 * Real-mode status: all seven mutations on this page are mock-only. The
 * underlying host endpoints do not exist on the wire today — the kubb client
 * directory at `shared/api/_generated/clients/` carries nothing for
 * `users`, `grants`, `keys` or `oidc-links`. Each one is tracked as a
 * follow-up issue (#31 invite, #32 grant, #33 key, #34 oidc-link, plus
 * #35 disable, #36 revoke-grant, #37 revoke-key).
 *
 * Calling any of these in real mode resolves successfully against the seed
 * store rather than throwing — the screen renders, the optimistic write
 * lands in mock space, the next refetch sees what was written. That is the
 * same shape the rest of the dashboard has for mock-first pages: the read
 * is what fails loudly (`loadIdentity` above throws), so a misconfigured
 * `VITE_USE_MOCK=false` lands on the empty-state branch, not on the success
 * branch with phantom data. Mock-first until the endpoints land.
 */
export function useInviteUserMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: InviteUserInput) => {
      if (env.useMock) {
        createSeedUser(input)
      } else {
        await postApiV1Users({
          email: input.email,
          displayName: input.name,
          password: input.invite ? null : null,
        })
      }
      return snapshot()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(identityQueryKey, next)
    },
  })
}

/**
 * Real-mode: mock-only. No `DELETE /api/v1/oidc-links/{id}` (or
 * `POST /api/v1/users/{id}/oidc-link`) endpoint on the wire. See issue #34.
 */
export function useLinkOidcMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: LinkOidcInput) => {
      if (env.useMock) {
        linkSeedOidcSubject(input.userId, input.subject)
      } else {
        await postApiV1UsersUseridOidcLink(input.userId, {
          provider: "oidc",
          subjectId: input.subject,
        })
      }
      return snapshot()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(identityQueryKey, next)
    },
  })
}

/**
 * Real-mode: mock-only. The toggle would map to `PATCH /api/v1/users/{id}`
 * with `{ disabled }`, but neither endpoint exists on the wire. See issue #35.
 */
export function useSetUserDisabledMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: SetUserDisabledInput) => {
      if (env.useMock) {
        setSeedUserDisabled(input.userId, input.disabled)
      } else {
        await patchApiV1UsersUserid(input.userId, { disabled: input.disabled })
      }
      return snapshot()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(identityQueryKey, next)
    },
  })
}

/**
 * Real-mode: mock-only. No `POST /api/v1/grants` endpoint. Revocation would
 * be `POST /api/v1/grants/{id}/revoke` (timestamped, never delete) per the
 * identity spec — also missing. See issues #32 (write) and #36 (revoke).
 */
export function useGrantRoleMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: GrantRoleInput) => {
      if (env.useMock) {
        grantSeedRole(input)
      } else {
        await postApiV1Grants({
          userId: input.subjectId,
          role: input.role,
          projectId: input.projectId,
        })
      }
      return snapshot()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(identityQueryKey, next)
    },
  })
}

/** See note on `useGrantRoleMutation`. Revoke side. Issue #36. */
export function useRevokeRoleMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (grantId: string) => {
      if (env.useMock) {
        revokeSeedRole(grantId)
      } else {
        await postApiV1GrantsGrantidRevoke(grantId)
      }
      return snapshot()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(identityQueryKey, next)
    },
  })
}

/** Real-mode: mock-only. No `POST /api/v1/keys/{id}/revoke`. Issue #37. */
export function useRevokeApiKeyMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (keyId: string) => {
      if (env.useMock) {
        revokeSeedApiKey(keyId)
      } else {
        await postApiV1KeysKeyidRevoke(keyId)
      }
      return snapshot()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(identityQueryKey, next)
    },
  })
}

/**
 * The one mutation whose result is not just the new list.
 *
 * Creating a key produces a secret that exists exactly once, in exactly one
 * place, and the store deliberately keeps no copy — so it comes back as a
 * return value and the caller is the only holder. The caller's job, in turn, is
 * to drop it: see `KeysPanel`, which resets this mutation the moment it has
 * taken the value into its own state, so the mutation cache is not a second
 * place the secret lives.
 *
 * Real-mode: mock-only. No `POST /api/v1/keys` endpoint; the wire shape
 * (prefix + plaintext shown once) is the dashboard's own projection and
 * would land untouched if/when the host ships the endpoint. See issue #33.
 */
export function useCreateApiKeyMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: CreateApiKeyInput) => {
      if (env.useMock) {
        const created = createSeedApiKey(input)
        return { prefix: created.key.prefix, plaintext: created.plaintext }
      }
      const me = await getApiV1AuthMe()
      const created = await postApiV1Keys({
        userId: me.userId ?? me.subjectId,
        label: input.name,
        expiresAt: input.expiresAt,
      })
      return { prefix: created.prefix, plaintext: created.secret }
    },
    onSuccess: () => {
      queryClient.setQueryData(identityQueryKey, snapshot())
    },
  })
}