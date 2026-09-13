import { useQuery } from "@tanstack/react-query"

import { fetchProxyModelsAsync } from "@/shared/api/models-proxy"
import {
  proxyKeysWireToMapping,
  type ProxyKeysMapping,
  type ProxyKeysResponseWire,
} from "@/domains/models/api/proxy-keys"
import type { ModelsSnapshot } from "@/domains/models/model/types"
import { getApiV1ProxyKeys } from "@/shared/api/_generated/clients/getApiV1ProxyKeys"
import { readSeedModels } from "@/shared/api/mock/models.store"
import { env } from "@/shared/config/env"

export const modelsQueryKey = ["models"] as const

/** The proxy's virtual-key catalogue — real mode only, never polled. */
export const proxyKeysQueryKey = ["proxy-keys"] as const

/**
 * Mock-mode: the registry, read from the mutable store rather than from
 * the seed — so a revoke and a proxy switch survive the refetch that
 * follows them. See <c>shared/api/mock/models.store.ts</c>.
 *
 * Real-mode (issue Q7 / v1.1): the only authoritative model list the
 * proxy exposes today is <c>GET /v1/models</c>, returning the OpenAI
 * <c>{ object, data: [{ id, object, created, owned_by }] }</c> envelope.
 * The dashboard's <c>ModelsSnapshot</c> shape carries endpoints/keys/
 * routes alongside the model list; the proxy list is a strict subset,
 * so the screen renders with the proxy's ids under a placeholder proxy
 * block and empty arrays for the rest. A v2 host endpoint that
 * aggregates the full snapshot drops in here without UI surgery.
 */
async function getModels(): Promise<ModelsSnapshot> {
  if (env.useMock) {
    return readSeedModels()
  }

  const envelope = await fetchProxyModelsAsync()
  const ids = envelope.data.map((row) => row.id)

  return {
    proxy: {
      enabled: true,
      changedAgoSec: 0,
      windowLabel: "live",
      runs: 0,
      spendUsd: 0,
      costPerRunUsd: 0,
      burnHourlyUsd: [],
    },
    endpoints: [
      {
        id: "proxy",
        name: "comuki proxy",
        wire: "openai",
        baseUrl: `${env.apiBaseUrl}/v1`,
        state: "ok",
        models: ids,
        note: "model list from /v1/models",
      },
    ],
    keys: [],
    routes: [],
  }
}

export function useModelsQuery() {
  return useQuery({
    queryKey: modelsQueryKey,
    queryFn: getModels,
  })
}

/**
 * The spend-key catalogue, `GET /api/v1/proxy/keys` — real mode only. Not
 * polled: keys change by configuration and restart, and the one act this
 * screen offers (revoke) invalidates the cache itself.
 */
export function useProxyKeysQuery() {
  return useQuery({
    queryKey: proxyKeysQueryKey,
    enabled: !env.useMock,
    queryFn: async (): Promise<ProxyKeysMapping> => {
      const response = await getApiV1ProxyKeys()
      return proxyKeysWireToMapping(response as ProxyKeysResponseWire)
    },
  })
}
