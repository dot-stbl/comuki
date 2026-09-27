import { Link } from "@tanstack/react-router"

import { resultLabel } from "@/domains/verify/model/gate"
import type { VerifyTranslator } from "@/domains/verify/model/gate"
import { sharedVerifyT } from "@/domains/verify/model/gate"
import type {
  VerifyCommand,
  VerifyResultLabel,
} from "@/domains/verify/model/types"
import { VerifyResultBadge } from "@/domains/verify/ui/verify-result-badge"
import { rankSort, type DataColumn } from "@/shared/ui"

import styles from "./verify-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getCommandId = (command: VerifyCommand) => command.id

/** Failing first, then the ones nothing has reached, then the green ones. */
const RESULT_RANK: Record<VerifyResultLabel, number> = {
  failed: 0,
  "never ran": 1,
  passed: 2,
}

const resultSort = rankSort(RESULT_RANK)

/** A result label → the catalogue key that spells it in the active locale. */
const RESULT_KEY: Record<VerifyResultLabel, string> = {
  failed: "result.failed",
  "never ran": "result.neverRan",
  passed: "result.passed",
}

/**
 * The commands one project declares, and what each last said.
 *
 * **There is no actions column, and that is the point of the screen.** Every
 * row here is a line in the client's repository, so the only thing that could
 * change one is a commit over there. A disabled Edit sitting at the end of each
 * row would describe a feature the product deliberately does not have — this
 * project has already shipped that mistake once, as a panel titled
 * "RulesEditor" over a table nobody could edit — so the affordance is absent
 * and the section header says where the file is instead.
 *
 * A factory with a translator parameter rather than a constant: a `cell` is a
 * plain function TanStack calls while it builds a row, so the panel passes its
 * hook-bound `t` and tests fall back to the shared instance. The filter's
 * *values* stay the raw labels — `resultLabel` is the sort and match key —
 * while the options' *labels* localise.
 */
export function createVerifyColumns(
  t: VerifyTranslator = sharedVerifyT
): DataColumn<VerifyCommand>[] {
  return [
    {
      id: "result",
      accessorFn: (command) => resultLabel(command.last),
      header: t("column.lastResult"),
      cell: ({ row }) => <VerifyResultBadge result={row.original.last} />,
      sortFn: resultSort,
      meta: {
        width: 132,
        pinned: true,
        filter: {
          kind: "select",
          placeholder: t("column.anyResult"),
          options: (
            ["failed", "never ran", "passed"] as VerifyResultLabel[]
          ).map((label) => ({
            value: label,
            label: t(RESULT_KEY[label], { defaultValue: label }),
          })),
          match: (command, value) => resultLabel(command.last) === value,
        },
      },
    },
    {
      accessorKey: "name",
      header: t("column.check"),
      cell: ({ row }) => (
        <span className={styles.value}>{row.original.name}</span>
      ),
      meta: {
        width: 160,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: t("column.filterPlaceholder"),
          match: (command, needle) =>
            `${command.name} ${command.command} ${command.path}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "command",
      header: t("column.command"),
      cell: ({ row }) => (
        <span className={styles.value} title={row.original.command}>
          {row.original.command}
        </span>
      ),
      meta: { label: t("column.command") },
    },
    {
      accessorKey: "path",
      header: t("column.declaredIn"),
      cell: ({ row }) => (
        <span className={styles.muted} title={row.original.path}>
          {row.original.path}
        </span>
      ),
      meta: { width: 200, label: t("column.declaredIn") },
    },
    {
      id: "detail",
      accessorFn: (command) => command.last?.detail ?? "",
      header: t("column.output"),
      cell: ({ row }) => {
        const last = row.original.last
        if (!last) {
          // Not a blank: a blank cell reads as a render that failed, and this
          // row is the one on the screen most worth noticing.
          return (
            <span className={styles.never}>{t("column.neverReached")}</span>
          )
        }
        if (last.outcome === "failed" && last.detail) {
          return (
            <span className={styles.detail} title={last.detail}>
              {last.detail}
            </span>
          )
        }
        return <span className={styles.never}>—</span>
      },
      meta: { label: t("column.output") },
    },
    {
      id: "at",
      accessorFn: (command) => command.last?.at ?? "",
      header: t("column.when"),
      cell: ({ row }) => (
        <span className={styles.muted}>{row.original.last?.at ?? "—"}</span>
      ),
      meta: { width: 116, label: t("column.when") },
    },
    {
      id: "duration",
      accessorFn: (command) => command.last?.durationSec ?? "",
      header: t("column.took"),
      cell: ({ row }) => {
        const last = row.original.last
        return last ? `${last.durationSec}s` : "—"
      },
      meta: { width: 88, numeric: true, label: t("column.took") },
    },
    {
      id: "run",
      accessorFn: (command) => command.last?.runId ?? "",
      header: t("column.run"),
      cell: ({ row }) => {
        const last = row.original.last
        if (!last) {
          return <span className={styles.never}>—</span>
        }
        return (
          <Link
            to="/runs/$runId"
            params={{ runId: last.runId }}
            className={styles.runLink}
            data-test="verify-run-link"
          >
            {last.runId}
          </Link>
        )
      },
      meta: { width: 112, label: t("column.run") },
    },
  ]
}
