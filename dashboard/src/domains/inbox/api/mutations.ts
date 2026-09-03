import { useMutation, useQueryClient } from "@tanstack/react-query"

import {
  mapClaimTicketInputToClaimRequest,
  mapIntakeTicketViewToTicket,
  mapNativeTicketInputToCreateRequest,
} from "@/domains/inbox/api/mappers"
import { inboxQueryKey } from "@/domains/inbox/api/queries"
import type {
  ClaimTicketInput,
  CreateNativeTicketInput,
  Ticket,
} from "@/domains/inbox/model/types"
import { postApiV1InboxClaim } from "@/shared/api/_generated/clients/postApiV1InboxClaim"
import { postApiV1Tickets } from "@/shared/api/_generated/clients/postApiV1Tickets"
import { postApiHooksProviderKey } from "@/shared/api/_generated/clients/postApiHooksProviderKey"
import type { WebhookAcceptedResponse } from "@/shared/api/_generated/types/WebhookAcceptedResponse"
import { createSeedNativeTicket } from "@/shared/api/mock/sources.store"
import type { SeedTicketDraft } from "@/shared/api/mock/sources.store"
import { env } from "@/shared/config/env"

/* ---------------------------------------------------------------------------
 * Inbox mutations — three writes, mock-first.
 *
 * `VITE_USE_MOCK=true` writes to the shared sources store, which the inbox
 * query reads; that round-trip is what keeps the storybook/dev:mock flow
 * honest. `VITE_USE_MOCK=false` switches to kubb-generated clients.
 *
 * Each mutation invalidates `inboxQueryKey` (and its sub-keys) on settle.
 * Optimistic writes are deliberately conservative: the inbox carries
 * per-row decisions with exact-once semantics on the host, and a
 * misplaced optimistic write would look like a successful claim followed
 * by a 409 on the refetch. We invalidate instead — the row disappears
 * (or flips) when the operator's decision has actually landed.
 * ------------------------------------------------------------------------- */

/**
 * Mock latency — kept identical to the sources mutations so the dashboard
 * behaves the same in mock mode whether the operator goes through the
 * sources form or the inbox mutation.
 */
const LATENCY = 220

function wait(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, LATENCY))
}

function requireMock(act: string): void {
  if (!env.useMock) {
    throw new Error(
      `${act} not implemented — set VITE_USE_MOCK=true, or wire the kubb-generated client.`
    )
  }
}

/**
 * Claim a pending ticket into a run.
 *
 * The host treats this as exactly-once; a repeat claim answers 409. The
 * mutation does **not** optimistically move the row out of the list
 * (unlike approve/cancel in runs) because the wire response carries the
 * new run id the screen needs to surface on success, and inventing it
 * here would let a 409 surface a row that never opened a run.
 */
export function useClaimTicketMutation() {
  const client = useQueryClient()

  return useMutation<Ticket, Error, ClaimTicketInput>({
    mutationFn: async (input) => {
      if (env.useMock) {
        requireMock("claim ticket")
        await wait()
        const seed = createSeedNativeTicket({
          projectId: "",
          title: "",
          body: "",
          labels: [],
          straightToWork: true,
        } satisfies SeedTicketDraft)
        return mapIntakeTicketViewToTicket({
          id: `00000000-0000-0000-0000-${Date.now().toString(16).padStart(12, "0").slice(0, 12)}`,
          projectId: seed.projectId,
          source: "native",
          externalId: seed.id,
          title: seed.title,
          url: `https://comuki.local/inbox/${seed.id}`,
          status: "claimed",
          runId: `00000000-0000-0000-0000-${Date.now().toString(16).padStart(12, "0").slice(0, 12)}`,
          createdAt: new Date().toISOString(),
        })
      }
      const view = await postApiV1InboxClaim(mapClaimTicketInputToClaimRequest(input))
      return mapIntakeTicketViewToTicket(view)
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: inboxQueryKey })
    },
  })
}

/**
 * File a new native ticket — `POST /api/v1/tickets`.
 *
 * The host creates the ticket AND launches its run in one motion when the
 * caller asks for `straightToWork: true`. The native form already routes
 * its submit through this mutation; the sources-domain `useCreateNativeTicket`
 * is the parallel mock-only path that bypasses the kubb client.
 *
 * Mock mode reuses the sources store's `createSeedNativeTicket` so both
 * surfaces share one source of truth.
 */
export function useCreateNativeTicketMutation() {
  const client = useQueryClient()

  return useMutation<Ticket, Error, CreateNativeTicketInput>({
    mutationFn: async (input) => {
      if (env.useMock) {
        requireMock("create native ticket")
        await wait()
        const seed = createSeedNativeTicket({
          projectId: input.projectId,
          title: input.title,
          body: input.body ?? "",
          labels: [],
          straightToWork: false,
        } satisfies SeedTicketDraft)
        return mapIntakeTicketViewToTicket({
          id: `00000000-0000-0000-0000-${seed.id.padStart(12, "0").slice(0, 12)}`,
          projectId: seed.projectId,
          source: "native",
          externalId: seed.id,
          title: seed.title,
          url: `https://comuki.local/inbox/${seed.id}`,
          status: "pending",
          runId: null,
          createdAt: new Date().toISOString(),
        })
      }
      const view = await postApiV1Tickets(mapNativeTicketInputToCreateRequest(input))
      return mapIntakeTicketViewToTicket(view)
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: inboxQueryKey })
    },
  })
}

/**
 * Post a synthetic webhook delivery to a connection.
 *
 * Anonymous endpoint (`POST /api/hooks/{provider}/{key}`); the FE never
 * invokes this from the SPA. The storybook / dev:mock may use the
 * generated hook to render the "send a test webhook" form.
 *
 * The body of the request is the raw tracker payload. The kubb-generated
 * client does not type it (kubb treats it as `unknown`) because every
 * provider has its own shape; callers wrap their payload in `JSON.stringify`
 * before this hook. The kubb wrapper threads the body through `config.data`
 * (its own convention for anonymous payloads) — see
 * `usePostApiHooksProviderKey` in the generated tree.
 */
export function usePostWebhookMutation() {
  const client = useQueryClient()

  return useMutation<WebhookAcceptedResponse, Error, WebhookInput>({
    mutationFn: async ({ provider, key, payload }) => {
      if (env.useMock) {
        requireMock("post webhook")
        await wait()
        return { outcome: "admitted", detail: null }
      }
      return postApiHooksProviderKey(provider, key, { data: payload })
    },
    onSettled: async () => {
      // The webhook surfaces an inbox ticket only when the connection's
      // admission rule admits the delivery; refetching the inbox on settle
      // covers the admitted case without forcing a wider invalidation.
      await client.invalidateQueries({ queryKey: inboxQueryKey })
    },
  })
}

/** Inputs to the synthetic-webhook mutation. */
export interface WebhookInput {
  /** Kebab-case provider key (`github | gitlab | yandex-tracker | jira`). */
  provider: string
  /** Per-connection webhook routing key (the connection's identifier). */
  key: string
  /** Raw tracker payload — provider-specific, passed through verbatim. */
  payload: unknown
}
