/**
 * Mock-first seed for visual artifacts.
 *
 * Lives in `api/` next to `queries.ts` because the kubb-generated tree
 * under `_generated/*` is wiped on every `bun run generate-api`, and the
 * mock store mirrors the kubb-shaped client — the query reads the same
 * shape on both paths (see `queries.ts.listVisualArtifacts`).
 *
 * Empty today. The slice 1 backend ships the controller with a
 * placeholder `FetchAsync` (`VisualArtifactsControllerQueriesHelpers` →
 * always returns `[]`); slice 3 wires the dashboard to render "no
 * evidence yet" for that empty list, which is exactly what this seed
 * produces. When the EF query lands, the seed keeps the mock path
 * honest — `useMock=true` continues to render real-looking rows without
 * the host running.
 *
 * Once a worker publishes through `POST /workers/{workItemId}/artifacts`
 * and the run page refetches, the row materialises for both mock and
 * real mode.
 */
export interface SeedVisualArtifact {
  id: string
  projectId: string
  filename: string
  contentType: string
  sizeBytes: number
  title?: string
  createdAt: string
  createdBy: "brain" | "worker"
  runId?: string
  workItemId?: string
  sessionId?: string
  ticketId?: string
  version: number
}

export const ARTIFACTS_VISUAL_SEED: SeedVisualArtifact[] = []
