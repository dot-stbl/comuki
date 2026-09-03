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

async function loadIdentity(): Promise<IdentitySnapshot> {
  if (!env.useMock) {
    throw new Error("identity API not implemented — set VITE_USE_MOCK=true")
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

/** Every mutation here ends the same way: the snapshot the store now holds. */
function useIdentityMutation<TInput>(
  apply: (input: TInput) => void,
) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: TInput) => {
      apply(input)
      return snapshot()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(identityQueryKey, next)
    },
  })
}

export function useInviteUserMutation() {
  return useIdentityMutation<InviteUserInput>((input) => {
    createSeedUser(input)
  })
}

export function useLinkOidcMutation() {
  return useIdentityMutation<LinkOidcInput>((input) => {
    linkSeedOidcSubject(input.userId, input.subject)
  })
}

export function useSetUserDisabledMutation() {
  return useIdentityMutation<SetUserDisabledInput>((input) => {
    setSeedUserDisabled(input.userId, input.disabled)
  })
}

export function useGrantRoleMutation() {
  return useIdentityMutation<GrantRoleInput>((input) => {
    grantSeedRole(input)
  })
}

export function useRevokeRoleMutation() {
  return useIdentityMutation<string>((grantId) => {
    revokeSeedRole(grantId)
  })
}

export function useRevokeApiKeyMutation() {
  return useIdentityMutation<string>((keyId) => {
    revokeSeedApiKey(keyId)
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
 */
export function useCreateApiKeyMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: CreateApiKeyInput) => {
      const created = createSeedApiKey(input)
      return { prefix: created.key.prefix, plaintext: created.plaintext }
    },
    onSuccess: () => {
      queryClient.setQueryData(identityQueryKey, snapshot())
    },
  })
}