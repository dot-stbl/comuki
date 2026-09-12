import { useQuery } from "@tanstack/react-query"

import { toKnowledgeSnapshot } from "@/domains/knowledge/api/mappers"
import type { KnowledgeSnapshot } from "@/domains/knowledge/model/types"
import { KNOWLEDGE_SEED } from "@/shared/api/mock/knowledge.seed"
import { env } from "@/shared/config/env"

export const knowledgeQueryKey = ["knowledge"] as const

/**
 * The knowledge page, against the wire.
 *
 * TODO(knowledge.read): the host exposes only `POST /api/v1/knowledge/ingest`
 * (issue S10 #9) — read paths land through the MCP `knowledge.search` tool
 * and a follow-up `GET /api/v1/knowledge/search` is planned for a later slice.
 * Until the search endpoint ships, the page stays on the seed store and the
 * kubb-client transport is never called from this domain. The day the BE
 * adds `/api/v1/knowledge/search`, the implementation here drops in
 * alongside `mappers.ts` the same way `chat/queries.ts` does — the `queryFn`
 * becomes a kubb client call returning a paged result, and `mappers.ts`
 * translates the host's `KnowledgeChunkView` to the dashboard's
 * `KnowledgeEntry`.
 *
 * Tracking: this domain's only real-mode branch is a typed `throw` that
 * surfaces the missing endpoint through React Query, so the screen still
 * renders its error state rather than silently serving stale seeds.
 */
async function getKnowledge(): Promise<KnowledgeSnapshot> {
  if (!env.useMock) {
    throw new Error(
      "knowledge read API not implemented — GET /api/v1/knowledge/search is a planned follow-up slice; set VITE_USE_MOCK=true until it ships",
    )
  }
  return toKnowledgeSnapshot(KNOWLEDGE_SEED)
}

export function useKnowledgeQuery() {
  return useQuery({
    queryKey: knowledgeQueryKey,
    queryFn: getKnowledge,
  })
}
