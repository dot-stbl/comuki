import { useMutation, useQueryClient } from "@tanstack/react-query"

import { sourcesQueryKey } from "@/domains/sources/api/queries"
import type {
  AdmissionMode,
  ProbeResult,
  SourceAuth,
  SourceConnection,
  SourcesSnapshot,
} from "@/domains/sources/model/types"
import { deleteApiV1SourcesSourceid } from "@/shared/api/_generated/clients/deleteApiV1SourcesSourceid"
import { postApiV1Tickets } from "@/shared/api/_generated/clients/postApiV1Tickets"
import type { CreateNativeTicketRequest } from "@/shared/api/_generated/types/CreateNativeTicketRequest"
import {
  connectSeedSource,
  createSeedNativeTicket,
  disconnectSeedSource,
  probeSeedConnection,
  probeSeedSourceDraft,
  updateSeedConnection,
  updateSeedWatch,
  type SeedSourceDraft,
  type SeedTicketDraft,
} from "@/shared/api/mock/sources.store"
import { env } from "@/shared/config/env"

/**
 * The acts this screen offers.
 *
 * ## Real-mode status (sources admin slice 6)
 *
 * Two of the seven mutations here are wired to the kubb client today:
 * `useDisconnectSource` (DELETE `/api/v1/sources/{id}`) and
 * `useCreateNativeTicket` (POST `/api/v1/tickets`). The other five remain
 * mock-first with explicit `requireMock(...)` throws on the real-mode path:
 *
 * - `useConnectSource` — wire `POST /api/v1/sources` exists but the request
 *   shape is `SecretReference`-style (`settingsJson` + `secretEnvRef` are
 *   env-var NAMES), and the form holds a plaintext credential + `SourceAuth`
 *   kind. The mismatch is tracked as issue #38 (connect-form redesign) and
 *   is the deferred half of the dashboard-pages-polish PR.
 * - `useUpdateConnection` — wire `PUT /api/v1/sources/{id}` exists with
 *   `{name, settingsJson, secretEnvRef, enabled}`, none of which overlaps
 *   the dashboard form's `{baseUrl, account, auth}`. Field-gap, issue #39.
 * - `useSaveWatch` — no watch / admission-rule endpoint surfaces in the
 *   dashboard model yet. The host's admission-rule API lives at
 *   `/api/v1/admission-rules` but is structured around a separate
 *   `AdmissionRuleView` (`{id, projectId, mode, filterJson, enabled}`),
 *   not the nested `SourceConnection.watch`. Issue #40.
 * - `useTestSourceDraft` / `useTestConnection` — no probe endpoint on the
 *   wire. Issues #41 (draft probe) and #42 (existing-connection probe).
 *
 * The mock store continues to be the single source of truth in mock mode, so
 * an optimistic write sticks across refetches for the same reason it always
 * did — and a real-mode caller of a mock-only mutation lands on the kubb
 * client's `VITE_USE_MOCK is not set` error rather than a phantom success.
 *
 * A **test connection** is the odd one out and deliberately so: a rejected
 * credential is a *result*, not a failed request, so it resolves with
 * `{ ok: false }` and the form shows the provider's sentence. Reserving the
 * rejected path for a genuinely broken call is what lets the form tell "the
 * token is wrong" apart from "the platform did not answer".
 */

const LATENCY = 220

function wait(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, LATENCY))
}

function requireMock(act: string): void {
  if (!env.useMock) {
    throw new Error(`${act} not implemented — set VITE_USE_MOCK=true`)
  }
}

export interface TestDraftInput {
  draft: SeedSourceDraft
  /**
   * The credential, held by the caller for exactly as long as the form is open.
   * It travels no further than `probeSeedSourceDraft`, which reads it and keeps
   * nothing.
   */
  secret: string
}

/**
 * Try the details in the form before anything is saved.
 *
 * Real-mode: mock-only. The probe needs to take a *draft* (not yet stored)
 * and a plaintext credential, so it cannot share the read endpoint of
 * `useTestConnection`; it would need either `POST /api/v1/sources/probe`
 * taking a draft body or a `?probe` query parameter on the create endpoint.
 * Neither exists. Issue #41.
 */
export function useTestSourceDraft() {
  return useMutation<ProbeResult, Error, TestDraftInput>({
    mutationFn: async ({ draft, secret }) => {
      requireMock("test connection")
      await wait()
      return probeSeedSourceDraft(draft, secret)
    },
  })
}

/**
 * Try a connection that already exists, with the credential it already has.
 *
 * Real-mode: mock-only. The probe would need either a dedicated
 * `POST /api/v1/sources/{id}/probe` endpoint or a query parameter on the
 * GET endpoint to ask the upstream "is the stored credential still valid?".
 * Neither exists today. Issue #42.
 */
export function useTestConnection() {
  const client = useQueryClient()

  return useMutation<ProbeResult, Error, string>({
    mutationFn: async (connectionId) => {
      requireMock("test connection")
      await wait()
      return probeSeedConnection(connectionId)
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: sourcesQueryKey })
    },
  })
}

/**
 * Save a new connection.
 *
 * No optimistic write: the id and the stored-at stamp are the server's to
 * mint, and inventing them here would put a row on the board that the refetch
 * then replaces with a differently-identified one.
 *
 * It resolves with the connection it made, because the form that called it has
 * somewhere to land now: `/sources/$sourceId` is a real screen, and the id it
 * needs is the one thing only this call knows.
 *
 * Real-mode: mock-only. The wire `POST /api/v1/sources` body is
 * `CreateSourceConnectionRequest` — `{projectId, provider, name, settingsJson,
 * secretEnvRef}` — and `settingsJson` + `secretEnvRef` are env-var NAMES,
 * not values. The dashboard's `SeedSourceDraft` carries the credential
 * itself plus an auth kind, which would have to fold into `settingsJson`
 * (env-var NAMES, not values) via a `SecretReference` resolver. Issue #38.
 */
export function useConnectSource() {
  const client = useQueryClient()

  return useMutation<SourceConnection, Error, SeedSourceDraft>({
    mutationFn: async (draft) => {
      requireMock("connect source")
      await wait()
      return connectSeedSource(draft)
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: sourcesQueryKey })
    },
  })
}

export interface UpdateConnectionInput {
  connectionId: string
  /** Only meaningful for a self-hosted kind; the empty string clears it. */
  baseUrl: string
  account: string
  auth: SourceAuth
}

/**
 * Change the details of a connection that already exists.
 *
 * The sibling of `useConnectSource`, and it carries the same absence: **no
 * secret**. The credential was written once by the form that took it, and
 * replacing one is reconnecting rather than editing — so there is no field here
 * that could hold a token, no argument that could smuggle one in, and nothing
 * downstream that would have to promise not to keep it.
 *
 * No optimistic write. What comes back is the store's own recomputation —
 * `selfHosted` follows the kind and the instance — and inventing that answer
 * here would put a row on the screen that the refetch then contradicts.
 *
 * Real-mode: mock-only. The wire `PUT /api/v1/sources/{id}` body is
 * `UpdateSourceConnectionRequest` — `{name, settingsJson, secretEnvRef,
 * enabled}` — and the dashboard form's `{baseUrl, account, auth}` carries
 * none of those fields. The mapping would need a `SecretReference` resolver
 * for the credential plus a flatten step into `settingsJson`. Issue #39.
 */
export function useUpdateConnection() {
  const client = useQueryClient()

  return useMutation<unknown, Error, UpdateConnectionInput>({
    mutationFn: async ({ connectionId, baseUrl, account, auth }) => {
      requireMock("update connection")
      await wait()
      updateSeedConnection(connectionId, { baseUrl, account, auth })
      return connectionId
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: sourcesQueryKey })
    },
  })
}

/**
 * Remove a connection.
 *
 * Throws when the connection refuses — which today means native. The rule lives
 * in the store as well as in the UI, and this is the path that carries it back
 * to the operator if a control ever gets it wrong.
 *
 * Real mode calls `DELETE /api/v1/sources/{id}` via the kubb-generated client.
 * The host returns 204 on success and 404 on the row already gone — same shape
 * as mock, where the seed store returns `false` for the same case and the
 * mutation throws with the native explanation. The host's "this is native"
 * check is presumed to live in its own controller; the dashboard renders the
 * 409 ProblemDetails body as a generic error when the host does refuse, and
 * the native row in mock mode is filtered out at the form level (no connect
 * for `native`).
 */
export function useDisconnectSource() {
  const client = useQueryClient()

  return useMutation<unknown, Error, string>({
    mutationFn: async (connectionId) => {
      if (env.useMock) {
        await wait()
        if (!disconnectSeedSource(connectionId)) {
          throw new Error(
            "native intake cannot be disconnected — it is the product's own, and a platform with no way to accept a ticket is not a state this product has."
          )
        }
        return connectionId
      }
      await deleteApiV1SourcesSourceid(connectionId)
      return connectionId
    },
    onMutate: async (connectionId) => {
      await client.cancelQueries({ queryKey: sourcesQueryKey })
      const previous = client.getQueryData<SourcesSnapshot>(sourcesQueryKey)

      client.setQueryData<SourcesSnapshot>(sourcesQueryKey, (snapshot) =>
        snapshot
          ? {
              ...snapshot,
              connections: snapshot.connections.filter(
                (entry) => entry.id !== connectionId
              ),
            }
          : snapshot
      )

      return { previous }
    },
    onError: (_error, _connectionId, context) => {
      const previous = (context as { previous?: SourcesSnapshot } | undefined)
        ?.previous
      if (previous) {
        client.setQueryData(sourcesQueryKey, previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: sourcesQueryKey })
    },
  })
}

export interface SaveWatchInput {
  connectionId: string
  enabled: boolean
  /** Stored verbatim. Nothing between the textarea and the store touches it. */
  filter: string
  mode: AdmissionMode
}

/**
 * Real-mode: mock-only. The dashboard models `watch` as a nested field on
 * `SourceConnection`, but the host's admission-rule API at
 * `/api/v1/admission-rules` is structured as a separate
 * `AdmissionRuleView` ({id, projectId, mode, filterJson, enabled}). Wiring
 * this needs the dashboard to read and write admission rules as a sibling
 * collection rather than a nested attribute on the connection. Issue #40.
 */
export function useSaveWatch() {
  const client = useQueryClient()

  return useMutation<unknown, Error, SaveWatchInput>({
    mutationFn: async ({ connectionId, enabled, filter, mode }) => {
      requireMock("save watch")
      await wait()
      updateSeedWatch(connectionId, { enabled, filter, mode })
      return connectionId
    },
    onMutate: async (input) => {
      await client.cancelQueries({ queryKey: sourcesQueryKey })
      const previous = client.getQueryData<SourcesSnapshot>(sourcesQueryKey)

      client.setQueryData<SourcesSnapshot>(sourcesQueryKey, (snapshot) =>
        snapshot
          ? {
              ...snapshot,
              connections: snapshot.connections.map((entry) =>
                entry.id === input.connectionId && entry.watch
                  ? {
                      ...entry,
                      watch: {
                        ...entry.watch,
                        enabled: input.enabled,
                        filter: input.filter,
                        mode: input.mode,
                        matched: input.enabled ? entry.watch.matched : 0,
                      },
                    }
                  : entry
              ),
            }
          : snapshot
      )

      return { previous }
    },
    onError: (_error, _input, context) => {
      const previous = (context as { previous?: SourcesSnapshot } | undefined)
        ?.previous
      if (previous) {
        client.setQueryData(sourcesQueryKey, previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: sourcesQueryKey })
    },
  })
}

/**
 * File a ticket in the product's own intake.
 *
 * Mock mode writes to the shared sources store. Real mode calls
 * `POST /api/v1/tickets`. The wire `CreateNativeTicketRequest` is the host's
 * intake shape — `projectId`, `title`, `body`, `externalId`, `author` — and
 * the dashboard's `SeedTicketDraft` carries two fields the wire does not:
 *
 * - `labels` — the dashboard's intake screen lets an operator tag a ticket;
 *   the host's request today has no labels parameter, so the labels are
 *   dropped on the wire (mock mode preserves them on the seed store).
 * - `straightToWork` — the screen's switch decides whether the orchestrator
 *   dispatches the ticket the moment the run is filed. The wire has no
 *   `dispatchOnCreate` parameter, so the screen says "filed" and the host's
 *   default routing applies (mock mode honours the switch).
 *
 * The two fields are silently dropped, not flattened into `body`, because the
 * page renders a different badge for "filed straight to work" — keeping the
 * difference local to the form keeps the host's contract clean.
 */
export function useCreateNativeTicket() {
  const client = useQueryClient()

  return useMutation<unknown, Error, SeedTicketDraft>({
    mutationFn: async (draft) => {
      if (env.useMock) {
        await wait()
        return createSeedNativeTicket(draft)
      }
      const request: CreateNativeTicketRequest = {
        projectId: draft.projectId,
        title: draft.title,
        body: draft.body,
      }
      await postApiV1Tickets(request)
      return draft
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: sourcesQueryKey })
    },
  })
}
