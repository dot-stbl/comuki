import { useMutation, useQueryClient } from "@tanstack/react-query"

import { sourcesQueryKey } from "@/domains/sources/api/queries"
import type {
  AdmissionMode,
  ProbeResult,
  SourceAuth,
  SourceConnection,
  SourcesSnapshot,
} from "@/domains/sources/model/types"
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
 * The orchestrator exposes no source endpoints yet, so in mock mode these write
 * to the shared store, which the sources query reads. That matters: an
 * optimistic update alone would be undone by the refetch that follows it, and
 * the decision would look like a 200 ms animation. Outside mock mode each of
 * these throws loudly rather than pretending to have succeeded.
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

/** Try the details in the form before anything is saved. */
export function useTestSourceDraft() {
  return useMutation<ProbeResult, Error, TestDraftInput>({
    mutationFn: async ({ draft, secret }) => {
      requireMock("test connection")
      await wait()
      return probeSeedSourceDraft(draft, secret)
    },
  })
}

/** Try a connection that already exists, with the credential it already has. */
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
 */
export function useDisconnectSource() {
  const client = useQueryClient()

  return useMutation<unknown, Error, string>({
    mutationFn: async (connectionId) => {
      requireMock("disconnect source")
      await wait()
      if (!disconnectSeedSource(connectionId)) {
        throw new Error(
          "native intake cannot be disconnected — it is the product's own, and a platform with no way to accept a ticket is not a state this product has."
        )
      }
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

/** File a ticket in the product's own intake. */
export function useCreateNativeTicket() {
  const client = useQueryClient()

  return useMutation<unknown, Error, SeedTicketDraft>({
    mutationFn: async (draft) => {
      requireMock("create ticket")
      await wait()
      return createSeedNativeTicket(draft)
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: sourcesQueryKey })
    },
  })
}
