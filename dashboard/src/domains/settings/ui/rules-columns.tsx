import type { RuleKind, SwarmRule } from "@/domains/settings/model/types"
import type { DataColumn, DataFilterOption } from "@/shared/ui"

import { RuleKindMark } from "./settings-badges"
import styles from "./settings-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getRuleId = (rule: SwarmRule) => rule.id

const RULE_KINDS: RuleKind[] = ["hard", "soft"]

/** The scopes the rule set actually names — the filter's options. */
export function uniqueRuleScopes(rules: SwarmRule[]): DataFilterOption[] {
  return [...new Set(rules.map((rule) => rule.scope))]
    .sort()
    .map((scope) => ({ value: scope, label: scope }))
}

/**
 * The swarm rule set's column declarations.
 *
 * No session and no factory argument beyond the scope list: rules live in the
 * client's git and change by commit, so nothing on a row is an act. The
 * description is the one field on the row a person wrote for another person to
 * read, and it is the only one in the interface voice.
 */
export function createRuleColumns(
  scopes: DataFilterOption[]
): DataColumn<SwarmRule>[] {
  return [
    {
      accessorKey: "id",
      header: "rule",
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.id}>
          {row.original.id}
        </span>
      ),
      meta: {
        width: 140,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: "filter rule, scope, description…",
          match: (rule, needle) =>
            `${rule.id} ${rule.scope} ${rule.desc}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "scope",
      header: "scope",
      cell: ({ row }) => (
        <span className={styles.value} title={row.original.scope}>
          {row.original.scope}
        </span>
      ),
      meta: {
        width: 160,
        filter: {
          kind: "select",
          placeholder: "all scopes",
          options: scopes,
        },
      },
    },
    {
      accessorKey: "kind",
      header: "kind",
      cell: ({ row }) => <RuleKindMark kind={row.original.kind} />,
      meta: {
        width: 96,
        filter: {
          kind: "select",
          placeholder: "all kinds",
          options: RULE_KINDS.map((kind) => ({ value: kind, label: kind })),
        },
      },
    },
    {
      accessorKey: "ver",
      header: "version",
      cell: ({ row }) => (
        <span className={styles.faint}>@{row.original.ver}</span>
      ),
      meta: { width: 104 },
    },
    {
      accessorKey: "desc",
      header: "description",
      cell: ({ row }) => (
        <span className={styles.note} title={row.original.desc}>
          {row.original.desc}
        </span>
      ),
      meta: { label: "description" },
    },
  ]
}
