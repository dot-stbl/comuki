/**
 * Proxy fetch adapter (issue Q7 / v1.1).
 *
 * The OpenAI / Anthropic-compatible proxy surface (<c>/v1/models</c>,
 * <c>/v1/chat/completions</c>) lives outside the kubb-emitted OpenAPI
 * document, and authenticates by a `VirtualKey` bearer rather than the
 * host cookie. The kubb-client transport (<c>credentials: 'include'</c>
 * only) cannot reach it. This file is the small hand-written fetch
 * adapter for that surface — one function, one URL, one bearer header.
 *
 * Future: when a `GET /api/v1/proxy/models` host endpoint is added that
 * reads the kubb config internally, this file goes away and the kubb
 * hook takes over.
 */

import { env } from "@/shared/config/env"

/** One row the proxy advertises on <c>GET /v1/models</c>. */
export interface ProxyModelRow {
  id: string
  object: string
  created: number
  owned_by: string
}

/** Wire envelope the proxy returns — the OpenAI list-models shape. */
export interface ProxyModelsResponse {
  object: string
  data: ProxyModelRow[]
}

/**
 * Calls <c>GET {apiBaseUrl}/v1/models</c> with the configured bearer.
 *
 * Returns <c>null</c> when the proxy key is unset: the caller falls back
 * to the seed, which is the right behaviour for a screen that has a
 * mock-mode story already wired up. Throws on non-2xx so a real-mode
 * caller can put the page into its error branch.
 */
export async function fetchProxyModelsAsync(
  cancellationToken?: AbortSignal,
): Promise<ProxyModelsResponse> {
  const apiBaseUrl = env.apiBaseUrl
  const proxyKey = env.proxyKey

  if (apiBaseUrl === "" || proxyKey === null) {
    throw new Error(
      "[proxy] apiBaseUrl or proxyKey is not set — set VITE_API_BASE_URL and VITE_PROXY_KEY to call /v1/models.",
    )
  }

  const response = await fetch(`${apiBaseUrl}/v1/models`, {
    method: "GET",
    headers: {
      Authorization: `Bearer ${proxyKey}`,
      Accept: "application/json",
    },
    signal: cancellationToken ?? null,
  })

  if (!response.ok) {
    throw new Error(`proxy /v1/models returned ${response.status}`)
  }

  return (await response.json()) as ProxyModelsResponse
}
