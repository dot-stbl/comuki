import { endpointOf } from "@/domains/models/model/keys"
import type { ModelEndpoint, ModelRoute } from "@/domains/models/model/types"
import { rankSort, type DataColumn } from "@/shared/ui"

import { EndpointStateBadge } from "./model-badges"
import styles from "./models-table.module.css"

export interface RoutingColumnsOptions {
  endpoints: ModelEndpoint[]
}

/** Row identity: a role and a duty together are the key. */
export const getRouteId = (route: ModelRoute) => `${route.role}|${route.duty}`

/** The lead's work before the worker's, which is the order it happens in. */
const roleSort = rankSort({ lead: 0, worker: 1 })

/**
 * Role → model. The resolution, and the only place it exists.
 *
 * The platform speaks in roles rather than in vendors: the lead thinks — plan,
 * contract, review, repair — and the worker runs profile steps in a container.
 * Nothing upstream of this table names a model, and nothing downstream of it
 * names a role, which is what lets a physical model be swapped without touching
 * a prompt.
 *
 * The lead's four duties are four rows rather than one because they do not
 * resolve the same way, and that is the point of routing by role: review is
 * cheaper work than planning, and somebody gets to make that call once, here,
 * instead of four times in four prompts.
 *
 * The endpoint's own state rides along, because a resolution that lands on a
 * degraded or disabled upstream is the failure this table can see and no other
 * screen can.
 */
export function createRoutingColumns({
  endpoints,
}: RoutingColumnsOptions): DataColumn<ModelRoute>[] {
  return [
    {
      id: "role",
      accessorFn: (route) => route.role,
      header: "role",
      sortFn: roleSort,
      cell: ({ row }) => (
        <span className={styles.route}>
          <span className={styles.role}>{row.original.role}</span>
          <span className={styles.duty}>{row.original.duty}</span>
        </span>
      ),
      meta: {
        width: 160,
        pinned: true,
        filter: {
          kind: "select",
          placeholder: "all roles",
          options: [
            { value: "lead", label: "lead" },
            { value: "worker", label: "worker" },
          ],
        },
      },
    },
    {
      accessorKey: "model",
      header: "model",
      cell: ({ row }) => (
        <span className={styles.strong}>{row.original.model}</span>
      ),
      meta: { width: 152 },
    },
    {
      id: "endpoint",
      accessorFn: (route) => route.endpointId,
      header: "endpoint",
      cell: ({ row }) => {
        const endpoint = endpointOf(endpoints, row.original.endpointId)
        return endpoint ? (
          <span className={styles.value} title={endpoint.baseUrl}>
            {endpoint.name}
          </span>
        ) : (
          <span className={styles.faint}>—</span>
        )
      },
      meta: {
        width: 136,
        filter: {
          kind: "select",
          placeholder: "all endpoints",
          options: endpoints.map((endpoint) => ({
            value: endpoint.id,
            label: endpoint.name,
          })),
        },
      },
    },
    {
      id: "reachable",
      accessorFn: (route) =>
        endpointOf(endpoints, route.endpointId)?.state ?? "disabled",
      header: "upstream",
      // A resolution that lands on a degraded or disabled endpoint is the one
      // failure this table can see and no other screen can — the routing is
      // fine, the wire under it is not.
      cell: ({ row }) => {
        const endpoint = endpointOf(endpoints, row.original.endpointId)
        return endpoint ? (
          <EndpointStateBadge state={endpoint.state} />
        ) : (
          <span className={styles.faint}>unknown</span>
        )
      },
      meta: { width: 116, label: "upstream" },
    },
    {
      accessorKey: "note",
      header: "used for",
      cell: ({ row }) => (
        <span className={styles.note} title={row.original.note}>
          {row.original.note}
        </span>
      ),
      meta: { label: "used for" },
    },
  ]
}
