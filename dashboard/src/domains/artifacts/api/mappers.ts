import type { VisualArtifactListItem } from "@/shared/api/_generated/types/VisualArtifactListItem"
import type { VisualArtifactPage as VisualArtifactPageDto } from "@/shared/api/_generated/types/VisualArtifactPage"

import type { VisualArtifact, VisualArtifactPage } from "../model/types"

/**
 * Wire row → domain `VisualArtifact`.
 *
 * Two coercions, both because the wire's int64/int32 carrier pattern is
 * "number when small enough, string when big" (the JSON Schema `int64`
 * type kubb emits). The same trick `runs/api/mappers.ts` uses for `cost`
 * and `tokens`; landing here keeps the domain a plain `number`.
 *
 * Null fields stay `null` — the wire schema declares every optional link
 * as `[null, string]` (see `openapi.json`), so a value can arrive as `null`
 * to mean "unlinked" rather than absent.
 */
export function mapVisualArtifactListItemToArtifact(
  entry: VisualArtifactListItem,
  projectId: string
): VisualArtifact {
  return {
    id: entry.id,
    projectId,
    filename: entry.filename,
    contentType: entry.contentType,
    sizeBytes: toNumber(entry.sizeBytes),
    title: entry.title ?? null,
    createdAt: entry.createdAt,
    createdBy: entry.createdBy,
    runId: entry.runId ?? null,
    workItemId: entry.workItemId ?? null,
    sessionId: entry.sessionId ?? null,
    ticketId: entry.ticketId ?? null,
    version: toNumber(entry.version),
  }
}

/**
 * Wire page → domain page. The wire echoes `projectId` through the page,
 * which matches the project path the caller asked for — but the caller
 * already has it, so the mapper still threads it explicitly to avoid the
 * domain page silently carrying a different id from the URL.
 */
export function mapVisualArtifactPageToPage(
  page: VisualArtifactPageDto,
  projectId: string
): VisualArtifactPage {
  return {
    projectId,
    items: page.items.map((entry) =>
      mapVisualArtifactListItemToArtifact(entry, projectId)
    ),
  }
}

/**
 * kubb emits `int64` / `int32` numbers as `number | string` so big values
 * round-trip without loss. Coerce to a finite `number`; non-finite or
 * missing are treated as `0` (consistent with the runs mappers, where
 * `cost` / `tokens` fall back to `0` rather than throwing — a malformed
 * row degrades the cell, not the screen).
 */
function toNumber(value: number | string): number {
  if (typeof value === "number") {
    return Number.isFinite(value) ? value : 0
  }
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : 0
}
