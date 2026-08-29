import type { KnowledgeEntry } from "@/domains/knowledge/model/types"

export function filterKnowledgeEntries(
  entries: KnowledgeEntry[],
  query: string
): KnowledgeEntry[] {
  const normalized = query.trim().toLowerCase()
  if (normalized.length === 0) {
    return entries
  }

  return entries.filter((entry) => {
    const haystack = [
      entry.id,
      entry.title,
      entry.scope,
      entry.summary,
      entry.kind,
      entry.ruleKind ?? "",
      entry.revision,
    ]
      .join(" ")
      .toLowerCase()
    return haystack.includes(normalized)
  })
}
