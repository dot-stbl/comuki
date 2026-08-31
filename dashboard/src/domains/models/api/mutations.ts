import { useMutation, useQueryClient } from "@tanstack/react-query"

import { modelsQueryKey } from "@/domains/models/api/queries"
import type { ModelsSnapshot } from "@/domains/models/model/types"
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
 * No endpoint exists yet, so in mock mode these write to the shared store the
 * query reads. Outside mock mode they throw rather than pretending to have
 * succeeded — a revoke that silently no-ops is the worst failure on this screen.
 */

const LATENCY_MS = 220

async function postRevoke(keyId: string) {
  if (!env.useMock) {
    throw new Error(
      `revoking a virtual key not implemented — set VITE_USE_MOCK=true, or wire POST /api/v1/models/keys/${keyId}/revoke`
    )
  }
  await new Promise((resolve) => setTimeout(resolve, LATENCY_MS))
  revokeSeedModelKey(keyId)
  return { keyId }
}

async function postProxy(enabled: boolean) {
  if (!env.useMock) {
    throw new Error(
      "the proxy switch is not implemented — set VITE_USE_MOCK=true, or wire PUT /api/v1/models/proxy"
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
 * a row cannot answer "what happened to the key that was here", and that is the
 * question somebody asks the morning after.
 */
export function useRevokeKey() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: postRevoke,
    onMutate: async (keyId: string) => {
      await client.cancelQueries({ queryKey: modelsQueryKey })
      const previous = client.getQueryData<ModelsSnapshot>(modelsQueryKey)

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

      return { previous }
    },
    onError: (_error, _keyId, context) => {
      if (context?.previous) {
        client.setQueryData(modelsQueryKey, context.previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: modelsQueryKey })
    },
  })
}

/** Turn the thin proxy on or off. Optional in v1 — a developer may run without it. */
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
