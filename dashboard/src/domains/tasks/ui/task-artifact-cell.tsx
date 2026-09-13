import { useMemo, useState, type ReactNode } from "react"

import {
  latestPngForTicket,
  useVisualArtifactsQuery,
  visualArtifactContentUrl,
  type VisualArtifact,
} from "@/domains/artifacts"
import { EvidencePane, EvidenceThumbnail } from "@/shared/ui"
import { env } from "@/shared/config/env"

import type { Task } from "../model/types"

import styles from "./task-artifact-cell.module.css"

/**
 * The cell that lives next to the action button on a ticket row.
 *
 * Reads `<projectId, ticketId>` from the row, fetches the project's
 * artifacts filtered by `ticketId`, picks the latest png
 * (`latestPngForTicket`), and renders either the thumbnail or
 * nothing. A click tells the page which ticket to open in the pane.
 *
 * The cell deliberately does not own the modal — owning it here
 * would make every row render its own modal, and opening one ticket
 * would leave its modal visible after a sort or scroll. The page
 * composes `TaskArtifactViewer` once at the bottom of the body.
 */
export interface TaskArtifactCellProps {
  task: Task
  onActivate: (ticketId: string, projectId: string) => void
}

export function TaskArtifactCell({ task, onActivate }: TaskArtifactCellProps) {
  const query = useVisualArtifactsQuery(task.projectId, {
    ticketId: task.id,
  })
  const items = useMemo(() => query.data?.items ?? [], [query.data?.items])
  const latest = useMemo(
    () => latestPngForTicket(items, task.id),
    [items, task.id]
  )

  if (!latest) {
    return <span className={styles.empty} aria-hidden="true" />
  }

  return (
    <EvidenceThumbnail
      size="sm"
      data-test="task-artifact-thumb"
      src={visualArtifactContentUrl(env.apiBaseUrl, task.projectId, latest.id)}
      contentType={latest.contentType}
      filename={latest.filename}
      title={latest.title ?? latest.filename}
      onActivate={() => onActivate(task.id, task.projectId)}
    />
  )
}

/**
 * The page-level pane.
 *
 * The address the cell handed back is `taskId` + `projectId`; the pane
 * finds the latest png for that ticket (same query the cell ran) and
 * mounts `EvidencePane` with the resolved artifact. The base URL is
 * re-read here rather than remembered across renders, so a config
 * change picks up on the next open without a hot reload.
 */
export interface TaskArtifactViewerProps {
  open: { ticketId: string; projectId: string } | null
  onClose: () => void
}

export function TaskArtifactViewer({ open, onClose }: TaskArtifactViewerProps) {
  const projectId = open?.projectId ?? ""
  const ticketId = open?.ticketId ?? ""
  const query = useVisualArtifactsQuery(projectId, {
    ticketId: ticketId.length > 0 ? ticketId : undefined,
  })
  const items = useMemo(() => query.data?.items ?? [], [query.data?.items])
  const latest: VisualArtifact | null = useMemo(
    () => (ticketId ? latestPngForTicket(items, ticketId) : null),
    [items, ticketId]
  )

  if (!open || !latest) {
    return null
  }

  return (
    <EvidencePane
      open
      onClose={onClose}
      src={visualArtifactContentUrl(env.apiBaseUrl, projectId, latest.id)}
      contentType={latest.contentType}
      filename={latest.filename}
      title={latest.title ?? latest.filename}
      sizeBytes={latest.sizeBytes}
    />
  )
}

/**
 * The render-prop host that owns `openArtifact` state.
 *
 * Pages wrap their body in this once and pass `openArtifact` into the
 * cell's `onActivate`. The mount-and-close pair stays in one place.
 */
export interface TaskArtifactViewerHostProps {
  children: (helpers: {
    openArtifact: (ticketId: string, projectId: string) => void
  }) => ReactNode
}

export function TaskArtifactViewerHost({
  children,
}: TaskArtifactViewerHostProps) {
  const [open, setOpen] = useState<{
    ticketId: string
    projectId: string
  } | null>(null)
  return (
    <>
      {children({
        openArtifact: (ticketId, projectId) => setOpen({ ticketId, projectId }),
      })}
      <TaskArtifactViewer open={open} onClose={() => setOpen(null)} />
    </>
  )
}
