import { useQuery } from "@tanstack/react-query"

import {
  knowledgeDocumentsToSnapshot,
  knowledgeHitWireToHit,
  toKnowledgeSnapshot,
  type KnowledgeSearchResponseWire,
} from "@/domains/knowledge/api/mappers"
import type {
  KnowledgeHit,
  KnowledgeSnapshot,
} from "@/domains/knowledge/model/types"
import { getApiV1KnowledgeDocuments } from "@/shared/api/_generated/clients/getApiV1KnowledgeDocuments"
import { getApiV1KnowledgeSearch } from "@/shared/api/_generated/clients/getApiV1KnowledgeSearch"
import { KNOWLEDGE_SEED } from "@/shared/api/mock/knowledge.seed"
import { env } from "@/shared/config/env"

/**
 * The knowledge page, against the wire.
 *
 * Two read paths landed with the host's knowledge surface:
 *
 * - `GET /api/v1/knowledge/documents` — the paged library of ingested
 *   documents. Real mode maps it onto the entry rows the library already
 *   draws; the revision readings and the golden-task harness are
 *   control-plane concepts the surface does not carry, so the snapshot
 *   answers their nulls and the page hides those sections.
 * - `GET /api/v1/knowledge/search?q&topK&minSimilarity` — pgvector cosine
 *   hits over the same chunks the MCP `search_knowledge` tool reads. The
 *   search box in the header drives it in real mode; mock mode keeps the
 *   client-side filter over the seed, which is a different (lexical) reading
 *   and the one the seed can honestly make.
 *
 * The search endpoint reads its parameters off the raw query string, so the
 * generated client (which the spec gave no query params) passes them through
 * its `config` — the transport serialises `params` exactly as it would for a
 * declared parameter.
 */

export const knowledgeQueryKey = ["knowledge"] as const

/** One page of documents is the whole library this screen has ever listed. */
const DOCUMENTS_PAGE_SIZE = 100

async function getKnowledge(): Promise<KnowledgeSnapshot> {
  if (env.useMock) {
    return toKnowledgeSnapshot(KNOWLEDGE_SEED)
  }
  const page = await getApiV1KnowledgeDocuments({
    page: 1,
    pageSize: DOCUMENTS_PAGE_SIZE,
  })
  return knowledgeDocumentsToSnapshot(page)
}

export function useKnowledgeQuery() {
  return useQuery({
    queryKey: knowledgeQueryKey,
    queryFn: getKnowledge,
  })
}

/* ------------------------------------------------------------------ *
 * Semantic search — real mode only; mock mode narrows the seed locally.
 * ------------------------------------------------------------------ */

export const knowledgeSearchQueryKey = (q: string) =>
  ["knowledge", "search", q] as const

/** The host's defaults when the caller sends neither knob. */
const SEARCH_TOP_K = 8
const SEARCH_MIN_SIMILARITY = 0.2

export function useKnowledgeSearchQuery(q: string) {
  const trimmed = q.trim()

  return useQuery<KnowledgeHit[]>({
    queryKey: knowledgeSearchQueryKey(trimmed),
    enabled: !env.useMock && trimmed.length > 0,
    queryFn: async () => {
      const response = await getApiV1KnowledgeSearch({
        params: {
          q: trimmed,
          topK: SEARCH_TOP_K,
          minSimilarity: SEARCH_MIN_SIMILARITY,
        },
      })
      const wire = response as KnowledgeSearchResponseWire
      return wire.items.map(knowledgeHitWireToHit)
    },
  })
}
