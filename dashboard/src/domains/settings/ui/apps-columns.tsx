import type { AppRegistryItem } from "@/domains/settings/model/types"
import type { SettingsTranslator } from "@/domains/settings/model/translate"
import { sharedSettingsT } from "@/domains/settings/model/translate"
import type { DataColumn, DataFilterOption } from "@/shared/ui"

import { EnvTags } from "./settings-badges"
import styles from "./settings-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getAppId = (app: AppRegistryItem) => app.name

/** The deploy targets the registry actually names — the filter's options. */
export function uniqueDeployTargets(
  apps: AppRegistryItem[]
): DataFilterOption[] {
  return [...new Set(apps.map((app) => app.deploy))]
    .sort()
    .map((deploy) => ({ value: deploy, label: deploy }))
}

/**
 * The app registry's column declarations.
 *
 * No factory argument beyond the filter's own options, and no session: the
 * registry is declared in the client's git, so nothing on a row is an act and
 * there is no permission for a cell to ask about. The moment one of these rows
 * grows a control, this takes the session the way `createUserColumns` does — a
 * `cell` is a plain function TanStack calls while it builds a row, so a hook
 * inside one is a hook called outside a render.
 *
 * Copy arrives as a `t` parameter for that same reason; the panel passes its
 * hook-bound translator and tests fall back to the shared instance.
 */
export function createAppColumns(
  deployTargets: DataFilterOption[],
  t: SettingsTranslator = sharedSettingsT
): DataColumn<AppRegistryItem>[] {
  return [
    {
      accessorKey: "name",
      header: t("apps.column.app"),
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.name}>
          {row.original.name}
        </span>
      ),
      meta: {
        width: 148,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: t("apps.filterPlaceholder"),
          match: (app, needle) =>
            `${app.name} ${app.repo} ${app.stack}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "repo",
      header: t("apps.column.repo"),
      cell: ({ row }) => (
        <span className={styles.value} title={row.original.repo}>
          {row.original.repo}
        </span>
      ),
      meta: { width: 180 },
    },
    {
      accessorKey: "stack",
      header: t("apps.column.stack"),
      cell: ({ row }) => (
        <span className={styles.stack} title={row.original.stack}>
          {row.original.stack}
        </span>
      ),
      meta: { width: 168 },
    },
    {
      id: "envs",
      accessorFn: (app) => app.envs.join(" "),
      header: t("apps.column.envs"),
      // A set has no order worth sorting by, and the alphabet is not it.
      enableSorting: false,
      cell: ({ row }) => <EnvTags envs={row.original.envs} />,
      meta: { width: 200, label: t("apps.column.envs") },
    },
    {
      accessorKey: "deploy",
      header: t("apps.column.deploy"),
      cell: ({ row }) => (
        <span className={styles.faint}>{row.original.deploy}</span>
      ),
      meta: {
        width: 120,
        filter: {
          kind: "select",
          placeholder: t("apps.allTargets"),
          options: deployTargets,
        },
      },
    },
  ]
}
