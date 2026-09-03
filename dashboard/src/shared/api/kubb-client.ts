/**
 * Custom kubb transport adapter (issue #29).
 *
 * kubb's pluginClient / pluginReactQuery `importPath: '@/shared/api/kubb-client'`
 * resolves every generated hook call (and the matching client wrapper) to this
 * file instead of kubb's built-in `@kubb/plugin-client/clients/fetch`. Two
 * reasons we override:
 *
 * 1. **Mock-first by default.** When `VITE_API_BASE_URL` is empty (fresh
 *    clone, `dev:mock` workflow, Storybook), a generated hook must NOT ping a
 *    random host. We surface the error early — when a screen is wired
 *    against real backend data, the operator must set
 *    `VITE_API_BASE_URL=http://localhost:17173` and restart the dev server.
 * 2. **Cookie auth.** Comuki's orchestrator authn is cookie-based
 *    (cookie session + `ck_` API key passthrough). Fetch defaults to
 *    `credentials: 'same-origin'` which LOSES the cookie on the cross-origin
 *    call to the host. We force `credentials: 'include'` so the dashboard
 *    reads/writes the host session even when the SPA is served from a
 *    different origin (deploy pattern: Vite :17173 → host :NNNN).
 *
 * Replaces the kubb default client. Generated code under
 * `src/shared/api/_generated/*` is unchanged — the `importPath` directive
 * on pluginClient.pluginReactQuery forces every hook to resolve through
 * this file. The adapter itself lives at the parent level so the
 * `output.clean: true` cycle only wipes `_generated/*`, not the adapter.
 *
 * If the existing `src/shared/api/mock/*` ever needs to serve generated
 * endpoints (mock data for OpenAPI operations), extend the `isMockMode`
 * branch here to dispatch into a mock handler keyed by `${method} ${url}`.
 * For v1 the operator uses the separate hand-written `mock/*` seeds; this
 * adapter only runs when the operator explicitly opts into real backend.
 */

import type {
  Client,
  RequestConfig,
  ResponseConfig,
} from "@kubb/plugin-client/clients/fetch";

// Re-export the kubb plugin-client types so generated hooks/clients can pull
// the full surface from a single import (`@/shared/api/kubb-client`). The
// transport itself is the default export; the types below match kubb's own
// `@kubb/plugin-client/clients/fetch` shape so generated code that imports
// types resolves identically regardless of which file it pulls from.
export type { Client, RequestConfig, ResponseConfig };
export type { ResponseErrorConfig } from "@kubb/plugin-client/clients/fetch";

/** Empty / unset → "operator hasn't pointed me at a backend yet". */
const rawBaseUrl =
  typeof import.meta.env.VITE_API_BASE_URL === "string"
    ? import.meta.env.VITE_API_BASE_URL.trim()
    : "";

/** Trailing slash would double-emit at request time (`//api/v1/...`). */
const baseURL = rawBaseUrl.replace(/\/+$/, "");

/**
 * Surface a clear error when the operator calls a generated hook without
 * setting `VITE_API_BASE_URL`. Better than the kubb default, which silently
 * fetches relative to the SPA origin and gets a Vite-served 404.
 */
const isMockMode = baseURL === "";

function requireBackendBaseUrl(): string {
  if (isMockMode) {
    throw new Error(
      [
        "[kubb-client] VITE_API_BASE_URL is not set.",
        "  Generated hooks call the real backend; the dashboard mock layer",
        "  (src/shared/api/mock/*) is hand-written and not visible to kubb.",
        "  Set VITE_API_BASE_URL=http://localhost:17173 (or your host port)",
        "  in .env.local and restart vite.",
      ].join("\n"),
    );
  }
  return baseURL;
}

/**
 * Reject when the backend sends an auth boundary. React Query treats the
 * rejected promise as an error and the screen falls back to its unauthed
 * branch; we do NOT redirect here — that belongs to a route guard watching
 * `/api/v1/auth/me`, not to the transport.
 *
 * 401 — not authenticated (cookie missing/expired). Surface as a query error.
 * 403 — authenticated but not permitted. Same path; the screen decides.
 */
function isAuthBoundary(status: number): boolean {
  return status === 401 || status === 403;
}

const kubbClient: Client = async <TResponseData, TError = unknown, TRequestData = unknown>(
  paramsConfig: RequestConfig<TRequestData>,
): Promise<ResponseConfig<TResponseData>> => {
  const resolvedBaseUrl = requireBackendBaseUrl();

  const headers = new Headers();
  if (paramsConfig.headers) {
    if (Array.isArray(paramsConfig.headers)) {
      for (const [key, value] of paramsConfig.headers) {
        headers.set(key, value);
      }
    } else {
      for (const [key, value] of Object.entries(paramsConfig.headers)) {
        if (typeof value === "string") {
          headers.set(key, value);
        }
      }
    }
  }

  const queryString =
    paramsConfig.params !== undefined && paramsConfig.params !== null
      ? new URLSearchParams(
          Object.entries(
            paramsConfig.params as Record<string, unknown>,
          ).reduce<Record<string, string>>((acc, [key, value]) => {
            if (value === undefined || value === null) {
              return acc;
            }
            acc[key] = String(value);
            return acc;
          }, {}),
        ).toString()
      : "";

  const url =
    resolvedBaseUrl +
    (paramsConfig.url ?? "") +
    (queryString.length > 0 ? `?${queryString}` : "");

  let body: BodyInit | undefined;
  if (paramsConfig.data !== undefined && paramsConfig.data !== null) {
    if (paramsConfig.data instanceof FormData) {
      body = paramsConfig.data;
    } else {
      headers.set("Content-Type", "application/json");
      body = JSON.stringify(paramsConfig.data);
    }
  }

  const response = await fetch(url, {
    credentials: "include",
    method: paramsConfig.method ?? "GET",
    headers,
    body,
    signal: paramsConfig.signal ?? null,
  });

  const data = ([204, 205, 304].includes(response.status) || !response.body
    ? ({} as TResponseData)
    : ((await response.json()) as TResponseData));

  if (isAuthBoundary(response.status)) {
    const errorPayload = (data ?? { status: response.status }) as unknown;
    const boundaryError = Object.assign(
      new Error(`auth boundary ${response.status}`),
      {
        status: response.status,
        response,
        data: errorPayload,
      },
    );
    throw boundaryError as unknown as TError;
  }

  return {
    data,
    status: response.status,
    statusText: response.statusText,
    headers: response.headers,
  };
};

export default kubbClient;
