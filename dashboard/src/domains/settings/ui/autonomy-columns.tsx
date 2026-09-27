import type { AutonomyMode, AutonomyRow } from "@/domains/settings/model/types"
import type { SettingsTranslator } from "@/domains/settings/model/translate"
import { sharedSettingsT } from "@/domains/settings/model/translate"
import type { DataColumn } from "@/shared/ui"

import { AutonomyModeMark } from "./settings-badges"
import styles from "./settings-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getAutonomyId = (row: AutonomyRow) => row.cls

const MODES: AutonomyMode[] = ["auto", "human"]

/**
 * What the swarm decides on its own, and what it has to ask about.
 *
 * No session and no factory argument: this panel is read-only today. That is
 * *not* because its source is git — it is a live setting the screen has no
 * control for yet, which is recorded on the panel rather than papered over
 * here. When the toggle arrives this takes the session the way
 * `createUserColumns` does, because a `cell` is a plain function TanStack calls
 * while it builds a row and a hook inside one throws.
 *
 * The change class is the one field a person wrote for another person to read,
 * so it is the only one in the interface voice. Copy arrives as a `t`
 * parameter (`dashboard-i18n` D7), defaulting to the shared instance.
 */
export function createAutonomyColumns(
  t: SettingsTranslator = sharedSettingsT
): DataColumn<AutonomyRow>[] {
  return [
    {
      accessorKey: "cls",
      header: t("autonomy.column.changeClass"),
      cell: ({ row }) => (
        <span className={styles.note} title={row.original.cls}>
          {row.original.cls}
        </span>
      ),
      meta: {
        pinned: true,
        width: 260,
        label: t("autonomy.column.changeClass"),
        filter: {
          kind: "text",
          placeholder: t("autonomy.filterPlaceholder"),
          match: (entry, needle) =>
            entry.cls.toLowerCase().includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "mode",
      header: t("autonomy.column.mode"),
      cell: ({ row }) => <AutonomyModeMark mode={row.original.mode} />,
      meta: {
        width: 120,
        align: "end",
        filter: {
          kind: "select",
          placeholder: t("autonomy.allModes"),
          options: MODES.map((mode) => ({ value: mode, label: mode })),
        },
      },
    },
  ]
}
