/**
 * Public surface of the inbox domain.
 *
 * This slice ships the API and model layers only — no pages yet. The
 * existing `tasks` domain owns the inbox UI today; this domain is the
 * bridge to the backend's `/api/v1/inbox` surface so pages can switch
 * over without a fork in the mock path.
 *
 * Re-exporting the public surface here (rather than importing from
 * `@/domains/inbox/api/...` directly) keeps pages consistent with the
 * other domains and gives us a single place to re-shape exports as the
 * inbox grows (webhook hooks, ticket detail, etc.).
 */

export type {
  CatalogProjection,
  ClaimTicketInput,
  CreateNativeTicketInput,
  InboxFilters,
  Ticket,
  TicketKind,
  TicketStatus,
} from "./model/types"
export {
  inboxCatalogQueryKey,
  inboxQueryKey,
  useInboxCatalogQuery,
  useInboxQuery,
  useInboxTicketQuery,
} from "./api/queries"
export {
  useClaimTicketMutation,
  useCreateNativeTicketMutation,
  usePostWebhookMutation,
  type WebhookInput,
} from "./api/mutations"
export {
  mapClaimTicketInputToClaimRequest,
  mapInboxCatalogToConnections,
  mapInboxToTickets,
  mapIntakeTicketViewToTicket,
  mapNativeTicketInputToCreateRequest,
  mapSeedTicketToTicket,
  normalizeTicketStatus,
  type WebhookOutcome,
} from "./api/mappers"
