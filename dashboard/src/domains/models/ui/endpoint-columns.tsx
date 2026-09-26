import {
  wireLabel,
  sharedModelsT,
  type ModelsTranslator,
} from "@/domains/models/model/keys"
import type { EndpointState, ModelEndpoint } from "@/domains/models/model/types"
import { rankSort, type DataColumn } from "@/shared/ui"

import { EndpointStateBadge, WireBadge } from "./model-badges"
import styles from "./models-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getEndpointId = (endpoint: ModelEndpoint) => endpoint.id

const ENDPOINT_STATES: EndpointState[] = ["ok", "degraded", "disabled"]

/** Worst first: the thing that is wrong, then the thing that is parked. */
const stateSort = rankSort({ degraded: 0, ok: 1, disabled: 2 })

/**
 * Upstream endpoints.
 *
 * Four columns and no actions: an endpoint is configured in env and git, not in
 * this screen, so the registry's job here is to say what exists and whether it
 * is answering. A self-hosted url is an ordinary row — the only thing that
 * makes it self-hosted is the host in its own base url, and inventing a badge
 * for that would be inventing a distinction the product does not make.
 *
 * The wire is a badge rather than prose because it is the one fact that decides
 * whether a model can be reached at all: a worker's provider config and the
 * lead's chat client both speak exactly these two protocols.
 */
export function createEndpointColumns(
  t: ModelsTranslator = sharedModelsT
): DataColumn<ModelEndpoint>[] {
  return [
    {
      accessorKey: "name",
      header: t("endpoints.column.endpoint"),
      cell: ({ row }) => (
        <span className={styles.strong}>{row.original.name}</span>
      ),
      meta: {
        width: 132,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: t("endpoints.filterPlaceholder"),
          match: (endpoint, needle) =>
            `${endpoint.name} ${endpoint.baseUrl} ${endpoint.models.join(" ")} ${endpoint.note}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "wire",
      header: t("endpoints.column.wire"),
      cell: ({ row }) => <WireBadge wire={row.original.wire} />,
      meta: {
        width: 128,
        filter: {
          kind: "select",
          placeholder: t("endpoints.allWires"),
          options: [
            { value: "openai", label: wireLabel("openai", t) },
            { value: "anthropic", label: wireLabel("anthropic", t) },
          ],
        },
      },
    },
    {
      accessorKey: "baseUrl",
      header: t("endpoints.column.baseUrl"),
      cell: ({ row }) => (
        <span className={styles.strong} title={row.original.baseUrl}>
          {row.original.baseUrl}
        </span>
      ),
      meta: { width: 280, label: t("endpoints.column.baseUrl") },
    },
    {
      accessorKey: "state",
      header: t("endpoints.column.state"),
      cell: ({ row }) => (
        <EndpointStateBadge state={row.original.state} t={t} />
      ),
      sortFn: stateSort,
      meta: {
        width: 116,
        filter: {
          kind: "select",
          placeholder: t("endpoints.allStates"),
          options: ENDPOINT_STATES.map((state) => ({
            value: state,
            label: t(`state.${state}`, { defaultValue: state }),
          })),
        },
      },
    },
    {
      id: "models",
      accessorFn: (endpoint) => endpoint.models.join(" "),
      header: t("endpoints.column.models"),
      cell: ({ row }) => (
        <span className={styles.models} title={row.original.models.join(", ")}>
          {row.original.models.map((model, index) => (
            <span key={model} className={styles.model}>
              {index > 0 ? (
                <span className={styles.modelSep} aria-hidden="true">
                  ·{" "}
                </span>
              ) : null}
              {model}
            </span>
          ))}
        </span>
      ),
      meta: { width: 200, label: t("endpoints.column.models") },
    },
    {
      accessorKey: "note",
      header: t("endpoints.column.note"),
      cell: ({ row }) => (
        <span className={styles.note} title={row.original.note}>
          {row.original.note}
        </span>
      ),
      meta: { label: t("endpoints.column.note") },
    },
  ]
}
