import { useQuery } from "@tanstack/react-query"

import { toKnowledgeSnapshot } from "@/domains/knowledge/api/mappers"
import type { KnowledgeSnapshot } from "@/domains/knowledge/model/types"
import { KNOWLEDGE_SEED } from "@/shared/api/mock/knowledge.seed"
import { env } from "@/shared/config/env"

export const knowledgeQueryKey = ["knowledge"] as const

async function getKnowledge(): Promise<KnowledgeSnapshot> {
  if (!env.useMock) {
    throw new Error("knowledge API not implemented — set VITE_USE_MOCK=true")
  }
  return toKnowledgeSnapshot(KNOWLEDGE_SEED)
}

export function useKnowledgeQuery() {
  return useQuery({
    queryKey: knowledgeQueryKey,
    queryFn: getKnowledge,
  })
}
