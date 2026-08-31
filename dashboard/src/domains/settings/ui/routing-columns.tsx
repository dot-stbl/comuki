import { Cpu } from "lucide-react"

import type { ModelRole, ModelRoute } from "@/domains/settings/model/types"
import { rankSort, type DataColumn } from "@/shared/ui"

import styles from "./settings-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getRouteId = (route: ModelRoute) => route.role

/**
 * Escalation order, not spelling: a step that fails on the worker goes up to
 * the lead, and the judge sits across both. The alphabet would put `judge`
 * first, which is the one reading this table must not give.
 */
const ROLE_RANK: Record<ModelRole, number> = { lead: 0, worker: 1, judge: 2 }

const roleSort = rankSort(ROLE_RANK)

/**
 * Which physical model answers for each role.
 *
 * No session and no factory argument. The table states the current map and the
 * form under it is what writes — one control that writes, gated once, rather
 * than three inline edits each having to ask the same question. So nothing on a
 * row is an act and no cell needs a permission.
 */
export function createRoutingColumns(): DataColumn<ModelRoute>[] {
  return [
    {
      accessorKey: "role",
      header: "role",
      sortFn: roleSort,
      cell: ({ row }) => (
        <span className={styles.role}>
          {/* The role is the row's identity and the mark is part of it, not a
              decoration beside it: three rows of bare words read as a list of
              nouns rather than as three places a model can be plugged in. */}
          <Cpu className={styles.roleIcon} aria-hidden="true" />
          {row.original.role}
        </span>
      ),
      meta: { width: 120, pinned: true },
    },
    {
      accessorKey: "model",
      header: "model",
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.model}>
          {row.original.model}
        </span>
      ),
      meta: { width: 180 },
    },
    {
      accessorKey: "use",
      header: "usage",
      cell: ({ row }) => (
        <span className={styles.note} title={row.original.use}>
          {row.original.use}
        </span>
      ),
      meta: { label: "usage" },
    },
  ]
}
