import { useMutation, useQueryClient } from "@tanstack/react-query"

import {
  modelsQueryKey,
  proxyKeysQueryKey,
} from "@/domains/models/api/queries"
import type { ModelsSnapshot, VirtualKey } from "@/domains/models/model/types"
import { postApiV1ProxyKeysKeyidRevoke } from "@/shared/api/_generated/clients/postApiV1ProxyKeysKeyidRevoke"
import {
  revokeSeedModelKey,
  setSeedProxyEnabled,
} from "@/shared/api/mock/models.store"
import { env } from "@/shared/config/env"

/**
 * The two acts this registry offers. Both gate on `models.manage`, a *platform*
 * permission: it reads platform roles alone, so no `projectId` is ever passed
 * with it — not even for a key that is scoped to one project. Who may revoke a
 * key is a fact about the platform; what the key can reach is a fact about the
 * key.
 *
 * Real mode:
 *
 * - **Revoke** calls `POST /api/v1/proxy/keys/{keyId}/revoke` (the id is the
 *   token's SHA-256 fingerprint, never the token itself). The host's store is
 *   config-seeded — a restart resurrects a revoked key until its
 *   `Proxy:VirtualKeys` row is removed — and the confirm dialog says so; the
 *   screen must not promise a permanence the store does not have.
 * - **The proxy switch** has no endpoint: the config-seeded store is immutable
 *   at runtime and a PATCH would answer 501. The page hides the toggle rather
 *   than offering an act the host has already refused.
 */

const LATENCY_MS = 220

async function postRevoke(keyId: string) {
  if (env.useMock) {
    await new Promise((resolve) => setTimeout(resolve, LATENCY_MS))
    revokeSeedModelKey(keyId)
    return { keyId }
  }
  await postApiV1ProxyKeysKeyidRevoke(keyId)
  return { keyId }
}

async function postProxy(enabled: boolean) {
  if (!env.useMock) {
    throw new Error(
      "the proxy switch is not implemented on the host — virtual keys are seeded from configuration and immutable at runtime"
    )
  }
  await new Promise((resolve) => setTimeout(resolve, LATENCY_MS))
  setSeedProxyEnabled(enabled)
  return { enabled }
}

/**
 * Revoke a key.
 *
 * The row stays and changes state rather than vanishing: a registry that loses
 * a row cannot answer "what happened to the key that was here", and that is
 * the question somebody asks the morning after. In real mode the next
 * catalogue listing simply omits the key, and the optimistic revocation
 * bridges the gap until it lands.
 */
export function useRevokeKey() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: postRevoke,
    onMutate: async (keyId: string) => {
      await client.cancelQueries({ queryKey: modelsQueryKey })
      await client.cancelQueries({ queryKey: proxyKeysQueryKey })
      const previousModels =
        client.getQueryData<ModelsSnapshot>(modelsQueryKey)
      const previousKeys =
        client.getQueryData<{ keys: VirtualKey[] }>(proxyKeysQueryKey)

      client.setQueryData<ModelsSnapshot>(modelsQueryKey, (snapshot) =>
        snapshot
          ? {
              ...snapshot,
              keys: snapshot.keys.map((key) =>
                key.id === keyId ? { ...key, revoked: true } : key
              ),
            }
          : snapshot
      )
      client.setQueryData<{ keys: VirtualKey[] }>(proxyKeysQueryKey, (cache) =>
        cache
          ? {
              ...cache,
              keys: cache.keys.map((key) =>
                key.id === keyId ? { ...key, revoked: true } : key
              ),
            }
          : cache
      )

      return { previousModels, previousKeys }
    },
    onError: (_error, _keyId, context) => {
      if (context?.previousModels) {
        client.setQueryData(modelsQueryKey, context.previousModels)
      }
      if (context?.previousKeys) {
        client.setQueryData(proxyKeysQueryKey, context.previousKeys)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: modelsQueryKey })
      await client.invalidateQueries({ queryKey: proxyKeysQueryKey })
    },
  })
}

/** Turn the thin proxy on or off. Mock only — the host has no switch. */
export function useSetProxyEnabled() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: postProxy,
    onMutate: async (enabled: boolean) => {
      await client.cancelQueries({ queryKey: modelsQueryKey })
      const previous = client.getQueryData<ModelsSnapshot>(modelsQueryKey)

      client.setQueryData<ModelsSnapshot>(modelsQueryKey, (snapshot) =>
        snapshot
          ? {
              ...snapshot,
              proxy: { ...snapshot.proxy, enabled, changedAgoSec: 0 },
            }
          : snapshot
      )

      return { previous }
    },
    onError: (_error, _enabled, context) => {
      if (context?.previous) {
        client.setQueryData(modelsQueryKey, context.previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: modelsQueryKey })
    },
  })
}
