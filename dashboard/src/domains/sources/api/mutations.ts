import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { sourcesQueryKey } from "@/domains/sources/api/queries"
import type {
  AdmissionMode,
  ProbeResult,
  SourceAuth,
  SourceConnection,
  SourcesSnapshot,
} from "@/domains/sources/model/types"
import { settingsToJson, sourceConnectionViewToConnection } from "@/domains/sources/api/mappers"
import { getApiV1AdmissionRules } from "@/shared/api/_generated/clients/getApiV1AdmissionRules"
import { postApiV1AdmissionRules } from "@/shared/api/_generated/clients/postApiV1AdmissionRules"
import { postApiV1Sources } from "@/shared/api/_generated/clients/postApiV1Sources"
import { postApiV1SourcesProbe } from "@/shared/api/_generated/clients/postApiV1SourcesProbe"
import { postApiV1SourcesSourceidProbe } from "@/shared/api/_generated/clients/postApiV1SourcesSourceidProbe"
import { postApiV1Tickets } from "@/shared/api/_generated/clients/postApiV1Tickets"
import { putApiV1AdmissionRulesRuleid } from "@/shared/api/_generated/clients/putApiV1AdmissionRulesRuleid"
import { putApiV1SourcesSourceid } from "@/shared/api/_generated/clients/putApiV1SourcesSourceid"
import { deleteApiV1SourcesSourceid } from "@/shared/api/_generated/clients/deleteApiV1SourcesSourceid"
import type { AdmissionRuleView } from "@/shared/api/_generated/types/AdmissionRuleView"
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
 * ## Real-mode status (sources admin slice 7 — issues #38, #39, #40)
 *
 * Six of the seven mutations are wired to the kubb client today.
 * `useCreateNativeTicket` was already real-mode; `useDisconnectSource`,
 * `useTestConnection`, `useTestSourceDraft`, `useUpdateConnection` followed
 * it on the admin slice. The slice-7 round trips the dashboard form
 * through `SecretReference`-style wiring (`settingsJson` + `secretEnvRef`
 * carry env-var NAMES, not values), so:
 *
 * - `useConnectSource` and `useUpdateConnection` fold `auth` / `account`
 *   / `baseUrl` into `settingsJson` via `settingsToJson`. There is no
 *   field anywhere that could hold a credential — the operator never sees
 *   a plaintext secret at any point of the round trip.
 * - `useSaveWatch` reads admission rules from
 *   `GET /api/v1/admission-rules?projectId=...` and writes them through
 *   `PUT /api/v1/admission-rules/{id}` (issue #40). The dashboard's three
 *   admission modes (`watch` | `inbox-only` | `both`) collapse onto the
 *   host's two (`watch` | `inbox`); `both` round-trips as `watch` because
 *   the host's `watch` is what the dashboard labels "both" — start a run
 *   and the ticket still lands in the catalog.
 *
 * The mock store continues to be the single source of truth in mock mode.
 * A real-mode caller of a mock-only mutation would land on the kubb
 * client's `VITE_USE_MOCK is not set` error.
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

export interface TestDraftInput {
  draft: SecretReferenceDraft
  /**
   * The env-var NAME holding the credential. The dashboard never holds the
   * secret itself; the host resolves the value at probe time. The mock path
   * reads a literal token for its probe, then keeps nothing.
   */
  secretEnvRef: string
  /** Mock-only literal credential; never sent on the wire. */
  mockSecret: string
}

/**
 * The shape the connect form holds. Auth / account / baseUrl fold into
 * `settingsJson` on the wire; `secretEnvRef` carries the env-var NAME.
 *
 * The legacy mock shape (`SeedSourceDraft`) is kept for the mock store's
 * own tests; new mutations speak this narrower type instead so there is
 * no field here that could hold a plaintext secret.
 */
export interface SecretReferenceDraft {
  projectId: string
  kind: string
  name: string
  auth: SourceAuth
  account: string
  baseUrl: string
  secretEnvRef: string
}

/**
 * Try the details in the form before anything is saved.
 *
 * The probe resolves the credential at call time — the wire sends the
 * env-var NAME, the host looks up the value. The mock path takes a literal
 * secret because there is no env-var layer in mock mode.
 */
export function useTestSourceDraft() {
  return useMutation<ProbeResult, Error, TestDraftInput>({
    mutationFn: async ({draft, secretEnvRef, mockSecret}) => {
      if (env.useMock) {
        await wait()
        return probeSeedSourceDraft(
          {
            projectId: draft.projectId,
            kind: draft.kind as never,
            name: draft.name,
            auth: draft.auth,
            account: draft.account,
            baseUrl: draft.baseUrl,
          },
          mockSecret
        )
      }
      const result = await postApiV1SourcesProbe({
        provider: draft.kind,
        settingsJson: settingsToJson({
          auth: draft.auth,
          account: draft.account,
          baseUrl: draft.baseUrl,
        }),
        secretEnvRef,
      })
      return {ok: result.reachable, message: result.message}
    },
  })
}

/**
 * Try a connection that already exists, with the credential it already has.
 *
 * The host looks up `secretEnvRef` from the stored row; the dashboard
 * never sees the resolved value.
 */
export function useTestConnection() {
  const client = useQueryClient()

  return useMutation<ProbeResult, Error, string>({
    mutationFn: async (connectionId) => {
      if (env.useMock) {
        await wait()
        return probeSeedConnection(connectionId)
      }
      const result = await postApiV1SourcesSourceidProbe(connectionId)
      return {ok: result.reachable, message: result.message}
    },
    onSettled: async () => {
      await client.invalidateQueries({queryKey: sourcesQueryKey})
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
 * Resolves with the wire's `SourceConnectionView`. The mapper rebuilds the
 * domain `SourceConnection` so the form that called it has somewhere to land
 * — `/sources/$sourceId` — with the id only this call knows.
 */
export function useConnectSource() {
  const client = useQueryClient()

  return useMutation<SourceConnection, Error, SecretReferenceDraft>({
    mutationFn: async (draft) => {
      if (env.useMock) {
        await wait()
        const seedDraft: SeedSourceDraft = {
          projectId: draft.projectId,
          kind: draft.kind as never,
          name: draft.name,
          auth: draft.auth,
          account: draft.account,
          baseUrl: draft.baseUrl,
        }
        return connectSeedSource(seedDraft) as SourceConnection
      }
      const created = await postApiV1Sources({
        projectId: draft.projectId,
        provider: draft.kind,
        name: draft.name,
        settingsJson: settingsToJson({
          auth: draft.auth,
          account: draft.account,
          baseUrl: draft.baseUrl,
        }),
        secretEnvRef: draft.secretEnvRef,
      })
      return sourceConnectionViewToConnection(created)
    },
    onSettled: async () => {
      await client.invalidateQueries({queryKey: sourcesQueryKey})
    },
  })
}

export interface UpdateConnectionInput {
  connectionId: string
  auth: SourceAuth
  account: string
  baseUrl: string
  /** The new env-var name; the dashboard does not see the value. */
  secretEnvRef: string
}

/**
 * Change the details of a connection that already exists.
 *
 * No field here can carry a secret: the credential is written once by the
 * form that took it, replacing one is reconnecting rather than editing, and
 * what this mutation rewrites is `settingsJson` (env-var NAMES only) plus
 * `secretEnvRef` (also a NAME).
 *
 * No optimistic write. What comes back is the host's recomputation; inventing
 * that answer here would put a row on the screen that the refetch contradicts.
 */
export function useUpdateConnection() {
  const client = useQueryClient()

  return useMutation<unknown, Error, UpdateConnectionInput>({
    mutationFn: async ({connectionId, auth, account, baseUrl, secretEnvRef}) => {
      if (env.useMock) {
        await wait()
        updateSeedConnection(connectionId, {baseUrl, account, auth})
        return connectionId
      }
      await putApiV1SourcesSourceid(connectionId, {
        settingsJson: settingsToJson({auth, account, baseUrl}),
        secretEnvRef,
      })
      return connectionId
    },
    onSettled: async () => {
      await client.invalidateQueries({queryKey: sourcesQueryKey})
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
 * mutation throws with the native explanation.
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
      await client.cancelQueries({queryKey: sourcesQueryKey})
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

      return {previous}
    },
    onError: (_error, _connectionId, context) => {
      const previous = (context as {previous?: SourcesSnapshot} | undefined)
        ?.previous
      if (previous) {
        client.setQueryData(sourcesQueryKey, previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({queryKey: sourcesQueryKey})
    },
  })
}

export interface SaveWatchInput {
  /** The connection whose admission rule the screen is editing. */
  connectionId: string
  /** The project the rule belongs to; the host keys rules by project. */
  projectId: string
  /** The existing rule id when the connection already has one; null on create. */
  ruleId: string | null
  enabled: boolean
  /** Stored verbatim. Nothing between the textarea and the host touches it. */
  filter: string
  mode: AdmissionMode
}

/**
 * Persist a connection's watch.
 *
 * The dashboard keeps its 3-mode vocabulary (`watch` | `inbox-only` | `both`)
 * because that is what the radio group shows the operator. The host
 * speaks 2 modes (`watch` | `inbox`); the mapping collapses "both" onto
 * "watch" because the host's `watch` is what the dashboard labels "both" —
 * a matching ticket starts a run and stays in the catalog. Round-tripping
 * "both" as "watch" is lossy by design: the next read shows the operator
 * the same word the host kept.
 */
function dashboardModeToHost(mode: AdmissionMode): "watch" | "inbox" {
  return mode === "inbox-only" ? "inbox" : "watch"
}

export function useSaveWatch() {
  const client = useQueryClient()

  return useMutation<unknown, Error, SaveWatchInput>({
    mutationFn: async ({
      connectionId,
      projectId,
      ruleId,
      enabled,
      filter,
      mode,
    }) => {
      if (env.useMock) {
        await wait()
        updateSeedWatch(connectionId, {enabled, filter, mode})
        return connectionId
      }
      const hostMode = dashboardModeToHost(mode)
      const filterJson = filter.length > 0 ? filter : "{}"

      if (ruleId) {
        await putApiV1AdmissionRulesRuleid(ruleId, {
          mode: hostMode,
          filterJson,
          enabled,
        })
      } else {
        await postApiV1AdmissionRules({
          projectId,
          mode: hostMode,
          filterJson,
        })
      }
      return connectionId
    },
    onSettled: async () => {
      await client.invalidateQueries({queryKey: sourcesQueryKey})
      await client.invalidateQueries({queryKey: ["admission-rules"]})
    },
  })
}

/**
 * List admission rules for one project. Used by the source detail page to
 * discover the existing rule (if any) the watch form should patch.
 */
export const admissionRulesQueryKey = (projectId: string) =>
  ["admission-rules", projectId] as const

export function useAdmissionRules(projectId: string | undefined) {
  return useQuery<AdmissionRuleView[]>({
    queryKey: projectId ? admissionRulesQueryKey(projectId) : ["admission-rules"],
    enabled: projectId !== undefined,
    queryFn: async () => {
      if (env.useMock) {
        return []
      }
      if (!projectId) {
        return []
      }
      return getApiV1AdmissionRules({projectId})
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
      await client.invalidateQueries({queryKey: sourcesQueryKey})
    },
  })
}