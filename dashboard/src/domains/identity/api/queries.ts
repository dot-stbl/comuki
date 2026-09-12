import type { QueryClient } from "@tanstack/react-query"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import * as projectsDomain from "@/domains/projects/api/mappers"
import {
  buildIdentitySnapshot,
} from "@/domains/identity/model/identity"
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
  mapApiKeysPageToApiKeyRows,
  mapGrantsPageToGrantRows,
  mapIdentityUsersPageToUserRows,
  mapMeResponseToSessionUser,
  mapOidcStartToAuthorizationUrl,
} from "./mappers"
import { getApiV1AuthMe } from "@/shared/api/_generated/clients/getApiV1AuthMe"
import { getApiV1AuthOidcProviderStart } from "@/shared/api/_generated/clients/getApiV1AuthOidcProviderStart"
import { getApiV1Grants } from "@/shared/api/_generated/clients/getApiV1Grants"
import { getApiV1Keys } from "@/shared/api/_generated/clients/getApiV1Keys"
import { getApiV1Projects } from "@/shared/api/_generated/clients/getApiV1Projects"
import { getApiV1Users } from "@/shared/api/_generated/clients/getApiV1Users"
import { postApiV1Grants } from "@/shared/api/_generated/clients/postApiV1Grants"
import { postApiV1GrantsGrantidRevoke } from "@/shared/api/_generated/clients/postApiV1GrantsGrantidRevoke"
import { postApiV1Keys } from "@/shared/api/_generated/clients/postApiV1Keys"
import { postApiV1KeysKeyidRevoke } from "@/shared/api/_generated/clients/postApiV1KeysKeyidRevoke"
import { postApiV1Users } from "@/shared/api/_generated/clients/postApiV1Users"
import { postApiV1UsersUseridOidcLink } from "@/shared/api/_generated/clients/postApiV1UsersUseridOidcLink"
import { patchApiV1UsersUserid } from "@/shared/api/_generated/clients/patchApiV1UsersUserid"

/**
 * Identity admin endpoints.
 *
 * Real mode (`VITE_USE_MOCK=false`) for the session path is wired: `me`,
 * `oidc/{provider}/start`, login, logout. The seven admin mutations on this
 * page (invite, link OIDC, set disabled, grant role, revoke role, revoke key,
 * create key) are also wired — each calls the kubb-generated client for the
 * host endpoint that landed under issues #31–#37 (`POST /api/v1/users`,
 * `POST /api/v1/users/{id}/oidc-link`, `PATCH /api/v1/users/{id}`,
 * `POST /api/v1/grants`, `POST /api/v1/grants/{id}/revoke`,
 * `POST /api/v1/keys`, `POST /api/v1/keys/{id}/revoke`) and then
 * **invalidates** — the mock seed snapshot never crosses into the real
 * cache. Mock mode keeps the synchronous `setQueryData(snapshot())`
 * behaviour it always had (see `settleIdentityCache`).
 *
 * The read path now also lands on the host under issue #45 / F13:
 * `loadIdentityReal` fans three list kubb clients out in parallel
 * (`GET /api/v1/users`, `GET /api/v1/grants`, `GET /api/v1/keys`, plus
 * `GET /api/v1/projects` for the registry), passes the wire through the
 * seed-shape mappers in `./mappers` and rejoins in
 * `buildIdentitySnapshot` so the screen receives the same
 * `IdentitySnapshot` it always did. Mock mode still runs the mutable seed
 * store — storybook / dev:mock remain a real-mode-fidelity v1.
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
 * The real-mode read path (issue #45 / F13). Three kubb list endpoints
 * are dispatched in parallel — the screen joins them in `buildIdentitySnapshot`,
 * the same helper the mock path uses, so the screen receives an identical
 * `IdentitySnapshot` and stops branching on `env.useMock` for the result
 * type. Projects come from the existing kubb `/api/v1/projects` client;
 * when that round-trips empty in a misconfigured dev environment the
 * grants column falls back to the raw project id (the same behaviour the
 * mock path applies to a missing registry entry).
 *
 * The wire is intentionally narrower than the seed — no OIDC subject,
 * no `lastSeenAt`, no `invited` user state, no key `expiresAt`. The
 * mappers document each gap and fill it with the honest default so the
 * screen renders a real-mode account row rather than a placeholder.
 */
async function loadIdentityReal(): Promise<IdentitySnapshot> {
  const [users, grants, keys, projectsPage] = await Promise.all([
    getApiV1Users({ Page: 1, PageSize: 100 }),
    getApiV1Grants({ Page: 1, PageSize: 200 }),
    getApiV1Keys({ Page: 1, PageSize: 100 }),
    getApiV1Projects({ includeArchived: true }),
  ]);

  const seedUsers = mapIdentityUsersPageToUserRows(users);
  const seedGrants = mapGrantsPageToGrantRows(grants);
  const seedKeys = mapApiKeysPageToApiKeyRows(keys);
  // `getApiV1Projects` returns `any` — the projects endpoint has no
  // explicit response schema. The hand-written `mapProjectViewToDetail`
  // path already drives the registry from the same client.
  const projectRows = projectsDomain.mapProjectsPageToSummaries(
    projectsPage as unknown as Parameters<
      typeof projectsDomain.mapProjectsPageToSummaries
    >[0],
  );

  return buildIdentitySnapshot(
    seedUsers,
    seedGrants,
    seedKeys,
    projectRows.map((row) => ({
      id: row.id,
      slug: row.slug,
      name: row.name,
      gitProfileRepo: row.gitProfileRepo,
      createdAt: row.createdAt,
    })),
    new Date(),
  );
}

/**
 * The whole screen in one payload.
 *
 * Mock mode reads the mutable seed store (the reason that store
 * exists — a `queryFn` returning a module constant undoes an optimistic
 * write on the next refetch).
 *
 * Real mode (`VITE_USE_MOCK=false`) calls the three list endpoints that
 * landed under issue #45 + the projects registry. See
 * `loadIdentityReal`'s note for the wire-shape gaps that the mappers
 * paper over.
 */
async function loadIdentity(): Promise<IdentitySnapshot> {
  return env.useMock ? snapshot() : loadIdentityReal();
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

/**
 * The session query as one options object — the hook below and the route
 * guard (`guardSession` → `ensureQueryData`) share it, so there is exactly
 * one place that knows how `/me` is keyed and fetched.
 *
 * `retry: false` because a 401 here is the answer, not a hiccup: the
 * cookie is gone, and a retry re-asks a question that was just refused —
 * holding the cold visitor's navigation open for a round trip nobody
 * wanted. The client-wide default (`retry: 1`) stays for everything else.
 */
export function meQueryOptions() {
  return {
    queryKey: meQueryKey,
    queryFn: getCurrentUser,
    retry: false,
  }
}

export function useCurrentUserQuery(options?: { enabled?: boolean }) {
  return useQuery({
    ...meQueryOptions(),
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
 * The one way every mutation on this file settles the identity cache.
 *
 * Mock mode wrote to the seed store, so the store's next snapshot goes
 * straight into the cache (`setQueryData`) — the synchronous read the
 * local flow has always had.
 *
 * Real mode must NOT snapshot: `snapshot()` reads the seed store, and
 * pasting it over the wire-fed cache would swap the operator's real
 * users, grants and keys for the mock roster mid-session. The real
 * branch invalidates instead, and TanStack refetches the host lists
 * through `loadIdentityReal` — the same discipline the sources domain
 * applies after its writes.
 *
 * `extraQueryKeys` covers mutations whose write reaches further than
 * this screen's own list (disabling a user also settles what `/me`
 * reports about the session).
 */
async function settleIdentityCache(
  queryClient: QueryClient,
  extraQueryKeys: readonly (readonly unknown[])[] = [],
): Promise<void> {
  if (env.useMock) {
    queryClient.setQueryData(identityQueryKey, snapshot())
    return
  }
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: identityQueryKey }),
    ...extraQueryKeys.map((queryKey) =>
      queryClient.invalidateQueries({ queryKey })
    ),
  ])
}

/**
 * Real mode (`VITE_USE_MOCK=false`) calls the kubb-generated client for
 * `POST /api/v1/users`. Mock mode writes to the seed store; either way
 * the cache settles through `settleIdentityCache` — the store snapshot
 * in mock, an invalidation-driven refetch against the host in real
 * mode. See issue #31.
 */
export function useInviteUserMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: InviteUserInput) => {
      if (env.useMock) {
        createSeedUser(input)
        return
      }
      await postApiV1Users({
        email: input.email,
        displayName: input.name,
        password: input.invite ? null : null,
      })
    },
    onSuccess: async () => {
      await settleIdentityCache(queryClient)
    },
  })
}

/**
 * Real mode calls `POST /api/v1/users/{id}/oidc-link` via the kubb
 * client. The host does not expose a delete — a subject is written once,
 * and changing it is a platform operation, not a screen. See issue #34.
 */
export function useLinkOidcMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: LinkOidcInput) => {
      if (env.useMock) {
        linkSeedOidcSubject(input.userId, input.subject)
        return
      }
      await postApiV1UsersUseridOidcLink(input.userId, {
        provider: "oidc",
        subjectId: input.subject,
      })
    },
    onSuccess: async () => {
      await settleIdentityCache(queryClient)
    },
  })
}

/**
 * Real mode calls `PATCH /api/v1/users/{id}` with `{ disabled }` via the
 * kubb client. The write also settles what `/me` reports when the row
 * being disabled is the session's own, so the mutation invalidates the
 * `me` key alongside the identity list. See issue #35.
 */
export function useSetUserDisabledMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: SetUserDisabledInput) => {
      if (env.useMock) {
        setSeedUserDisabled(input.userId, input.disabled)
        return
      }
      await patchApiV1UsersUserid(input.userId, { disabled: input.disabled })
    },
    onSuccess: async () => {
      await settleIdentityCache(queryClient, [meQueryKey])
    },
  })
}

/**
 * Real mode calls `POST /api/v1/grants` via the kubb client. The revoke
 * side lives on `useRevokeRoleMutation`. See issue #32.
 */
export function useGrantRoleMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: GrantRoleInput) => {
      if (env.useMock) {
        grantSeedRole(input)
        return
      }
      await postApiV1Grants({
        userId: input.subjectId,
        role: input.role,
        projectId: input.projectId,
      })
    },
    onSuccess: async () => {
      await settleIdentityCache(queryClient)
    },
  })
}

/**
 * Real mode calls `POST /api/v1/grants/{id}/revoke` via the kubb client.
 * The host timestamps the revocation rather than deleting the row, so the
 * grant survives with a `revokedAt`. See issue #36.
 */
export function useRevokeRoleMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (grantId: string) => {
      if (env.useMock) {
        revokeSeedRole(grantId)
        return
      }
      await postApiV1GrantsGrantidRevoke(grantId)
    },
    onSuccess: async () => {
      await settleIdentityCache(queryClient)
    },
  })
}

/**
 * Real mode calls `POST /api/v1/keys/{id}/revoke` via the kubb client.
 * See issue #37.
 */
export function useRevokeApiKeyMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (keyId: string) => {
      if (env.useMock) {
        revokeSeedApiKey(keyId)
        return
      }
      await postApiV1KeysKeyidRevoke(keyId)
    },
    onSuccess: async () => {
      await settleIdentityCache(queryClient)
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
 * Real mode calls `POST /api/v1/keys` via the kubb client and reads the
 * current user from `GET /api/v1/auth/me` to fill `userId`. The wire shape
 * (`prefix` + `plaintext` shown once) is the dashboard's own projection;
 * the host's response is unpacked into that shape by the mapper.
 * The key list settles through `settleIdentityCache` — mock snapshot in
 * mock mode, invalidation against the host otherwise.
 * See issue #33.
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
        tenantProjectId: input.tenantProjectId,
      })
      return { prefix: created.prefix, plaintext: created.secret }
    },
    onSuccess: async () => {
      await settleIdentityCache(queryClient)
    },
  })
}