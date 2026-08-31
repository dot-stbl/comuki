import type { KeyStatus, ProviderKey } from "@/domains/settings/model/types"
import type { DataColumn } from "@/shared/ui"

import { KeyStatusMark } from "./settings-badges"
import styles from "./settings-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getProviderKeyId = (key: ProviderKey) => key.provider

const KEY_STATUSES: KeyStatus[] = ["ok", "warn"]

/**
 * The provider credentials the proxy holds.
 *
 * No session and no factory argument: the keys come from env and the rotation
 * runs in the proxy, so there is nothing on a row anybody can do from here and
 * no permission for a cell to ask about. The status cell says the provider's
 * own sentence rather than the enum, because `budget 67%` is a reading and
 * `warn` is only a category.
 */
export function createProviderKeyColumns(): DataColumn<ProviderKey>[] {
  return [
    {
      accessorKey: "provider",
      header: "provider",
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.provider}>
          {row.original.provider}
        </span>
      ),
      meta: {
        width: 148,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: "filter provider, scope…",
          match: (key, needle) =>
            `${key.provider} ${key.scope}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "scope",
      header: "scope",
      cell: ({ row }) => (
        <span className={styles.note} title={row.original.scope}>
          {row.original.scope}
        </span>
      ),
      meta: { width: 180 },
    },
    {
      accessorKey: "rotation",
      header: "rotation",
      cell: ({ row }) => (
        <span className={styles.faint}>{row.original.rotation}</span>
      ),
      meta: { width: 132 },
    },
    {
      accessorKey: "status",
      header: "status",
      cell: ({ row }) => (
        <KeyStatusMark
          status={row.original.status}
          label={row.original.statusLabel}
        />
      ),
      meta: {
        width: 148,
        filter: {
          kind: "select",
          placeholder: "all statuses",
          options: KEY_STATUSES.map((status) => ({
            value: status,
            label: status,
          })),
        },
      },
    },
  ]
}
