import { Loader2, Target, Trash2 } from "lucide-react"

import { staleReason, versionLabel } from "@/domains/compute/model/capacity"
import type { WorkerVersion } from "@/domains/compute/model/types"
import { formatDuration } from "@/domains/runs/model/format"
import { can, needsLabel, type Session } from "@/shared/session"
import { Button, Tooltip, numericSort, type DataColumn } from "@/shared/ui"

import styles from "./compute-table.module.css"

export interface VersionColumnsOptions {
  /** The label new starts use — what every other row is stale against. */
  target: WorkerVersion | undefined
  /** The label a teardown is in flight for, as `digest · ref`. */
  retiringLabel: string | null
  onRetire: (version: WorkerVersion) => void
  /** The shift itself — see the note on `ProviderColumnsOptions.session`. */
  session: Session
}

/** Row identity. A label is both halves, so the id is both halves. */
export const getVersionId = (version: WorkerVersion) =>
  `${version.digest}|${version.profilesRef}`

/**
 * The rollout table, and the one reading it exists to deliver.
 *
 * A worker is labelled by image digest **plus** profiles git-ref. Changing
 * either only affects a new `Start` — an idle worker on a different label is
 * never matched to an item, no matter how long that item waits. So a pool can
 * be full of idle containers while the queue grows, and every other view of the
 * system shows a healthy idle pool, because a stranded worker looks exactly
 * like an available one.
 *
 * The columns are arranged around that: the two halves of the label live in one
 * cell so they cannot be read apart, the stale rows say *which half* moved, and
 * the idle count on a stale row is the number that is actually wrong — so it is
 * the one column allowed to take a hue, with the header word beside it saying
 * what it counts.
 */
export function createVersionColumns({
  target,
  retiringLabel,
  onRetire,
  session,
}: VersionColumnsOptions): DataColumn<WorkerVersion>[] {
  return [
    {
      id: "label",
      accessorFn: versionLabel,
      header: "worker label",
      cell: ({ row }) => (
        <span className={styles.label} title={versionLabel(row.original)}>
          <span className={styles.digest}>{row.original.digest}</span>
          <span className={styles.ref}>{row.original.profilesRef}</span>
        </span>
      ),
      meta: {
        width: 260,
        pinned: true,
        label: "worker label",
        filter: {
          kind: "text",
          placeholder: "filter digest, profiles ref…",
          match: (version, needle) =>
            versionLabel(version).toLowerCase().includes(needle.toLowerCase()),
        },
      },
    },
    {
      id: "target",
      accessorFn: (version) => (version.target ? "target" : "stale"),
      header: "matching",
      cell: ({ row }) =>
        row.original.target ? (
          <span className={styles.target}>
            <Target className={styles.targetIcon} aria-hidden="true" />
            new starts
          </span>
        ) : (
          <span className={styles.faint}>never matched</span>
        ),
      meta: {
        width: 132,
        label: "matching",
        filter: {
          kind: "select",
          placeholder: "all labels",
          options: [
            { value: "target", label: "new starts" },
            { value: "stale", label: "never matched" },
          ],
        },
      },
    },
    {
      accessorKey: "workers",
      header: "workers",
      cell: ({ row }) => (
        <span className={styles.value}>{row.original.workers}</span>
      ),
      meta: { width: 88, numeric: true },
    },
    {
      accessorKey: "idle",
      header: "idle",
      sortFn: numericSort,
      // The number that is wrong. On the target label an idle worker is the
      // pool doing its job; on any other label it is a container that will sit
      // there until somebody tears it down, and the two must not look alike.
      cell: ({ row }) => {
        const version = row.original
        const stranded = !version.target && version.idle > 0
        return (
          <span
            className={stranded ? styles.stranded : styles.value}
            data-test="version-idle"
            data-stranded={stranded ? "" : undefined}
            title={
              stranded
                ? `${version.idle} idle on a label nothing is matched to — they will not claim an item`
                : undefined
            }
          >
            {version.idle}
          </span>
        )
      },
      meta: { width: 80, numeric: true },
    },
    {
      accessorKey: "oldestUpSec",
      header: "oldest up",
      cell: ({ row }) => (
        <span className={styles.value}>
          {formatDuration(row.original.oldestUpSec)}
        </span>
      ),
      meta: { width: 96, numeric: true, label: "oldest up" },
    },
    {
      id: "reason",
      accessorFn: (version) => staleReason(version, target) ?? "",
      header: "why",
      // The half that moved. Without it two stale rows look like one problem,
      // and the second one — the right image on a moved profiles ref — is the
      // one nobody expects.
      cell: ({ row }) => {
        const reason = staleReason(row.original, target)
        return reason ? (
          <span className={styles.note} title={reason}>
            {reason}
          </span>
        ) : (
          <span className={styles.faint}>—</span>
        )
      },
      meta: { label: "why" },
    },
    {
      id: "actions",
      header: "actions",
      enableSorting: false,
      cell: ({ row }) => {
        const version = row.original
        // Nothing to retire: the target label, or a stale one already down to
        // its busy containers. The act does not exist for this row, which is
        // not a role being refused it.
        if (version.target || version.idle === 0) {
          return null
        }

        const busy = retiringLabel === getVersionId(version)
        const denial = can(session, "compute.manage")
          ? null
          : needsLabel("compute.manage")

        // A bin glyph in a 80px cell, so the word it stands in for arrives on
        // hover *and* on focus — which the `title` it replaces never managed.
        // A refused act puts its sentence in the same place, because `denied`
        // keeps the control focusable and hoverable exactly so it can.
        return (
          <span className={styles.actions}>
            <Tooltip
              content={denial ?? "Retire the idle workers on this label"}
            >
              <Button
                size="icon-sm"
                variant="destructive"
                data-test="version-retire"
                disabled={busy}
                denied={denial}
                aria-busy={busy || undefined}
                aria-label={`Retire ${version.idle} idle workers on ${versionLabel(version)}`}
                onClick={(event) => {
                  event.stopPropagation()
                  onRetire(version)
                }}
              >
                {busy ? (
                  <Loader2 className={styles.spin} aria-hidden="true" />
                ) : (
                  <Trash2 aria-hidden="true" />
                )}
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 80, align: "end", label: "actions" },
    },
  ]
}
