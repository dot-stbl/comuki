import { Ban } from "lucide-react"

import {
  EXPIRY_SOON_DAYS,
  sharedIdentityT,
  type IdentityTranslator,
} from "@/domains/identity/model/identity"
import type { ApiKeyRow } from "@/domains/identity/model/types"
import { can, needsLabel, type Session } from "@/shared/session"
import { Button, Tooltip, numericSort, type DataColumn } from "@/shared/ui"

import styles from "./identity-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getApiKeyId = (key: ApiKeyRow) => key.id

export interface KeyColumnsOptions {
  session: Session
  revokingId: string | null
  onRevoke: (key: ApiKeyRow) => void
  /**
   * Copy arrives as a parameter because `cell` is called as a plain function
   * while the table builds a row, so a `useTranslation` inside one throws.
   * The page passes its hook-bound `t`; tests and stories fall back to the
   * shared instance, which answers in the active locale.
   */
  t?: IdentityTranslator
}

/**
 * What a key is, what it opens, and whether anyone is using it.
 *
 * The secret is not here and never will be. The prefix is the whole of the key
 * this screen has ever seen after the moment it was created — everything else
 * on the row is about the key rather than of it, which is exactly the shape a
 * list of credentials should have.
 *
 * Two of the columns exist because of how keys actually go wrong. A key that
 * has never been used is usually a key somebody forgot they made; a key days
 * from expiry is a pipeline about to break at 02:00. Both read in words before
 * they read in colour.
 */
export function createApiKeyColumns({
  session,
  revokingId,
  onRevoke,
  t = sharedIdentityT,
}: KeyColumnsOptions): DataColumn<ApiKeyRow>[] {
  const denial = can(session, "identity.manage")
    ? null
    : needsLabel("identity.manage")

  return [
    {
      accessorKey: "prefix",
      header: t("keysColumn.prefix"),
      cell: ({ row }) => (
        <span className={styles.value}>{row.original.prefix}</span>
      ),
      meta: {
        width: 132,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: t("keysColumn.filterPlaceholder"),
          match: (key, needle) =>
            // Same reason as the people list: a pasted `k_…` has to land.
            `${key.id} ${key.prefix} ${key.name} ${key.grants.join(" ")}`
              .toLowerCase()
              .includes(needle.toLowerCase()),
        },
      },
    },
    {
      accessorKey: "name",
      header: t("keysColumn.name"),
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.name}>
          {row.original.name}
        </span>
      ),
      meta: { width: 160 },
    },
    {
      accessorKey: "status",
      header: t("keysColumn.key"),
      cell: ({ row }) => {
        const status = row.original.status
        return (
          <span className={status === "revoked" ? styles.off : styles.value}>
            {t(`keyStatus.${status}`)}
          </span>
        )
      },
      meta: {
        width: 96,
        label: t("keysColumn.key"),
        filter: {
          kind: "select",
          placeholder: t("keysColumn.allKeys"),
          options: [
            { value: "active", label: t("keyStatus.active") },
            { value: "revoked", label: t("keyStatus.revoked") },
          ],
        },
      },
    },
    {
      id: "grants",
      accessorFn: (key) => key.grants.join(" "),
      header: t("keysColumn.grants"),
      enableSorting: false,
      cell: ({ row }) => {
        const grants = row.original.grants
        return grants.length > 0 ? (
          <span className={styles.grants} title={grants.join(", ")}>
            {grants.join(" · ")}
          </span>
        ) : (
          // A key that opens nothing authenticates and then gets a 403 on
          // everything. Worth saying out loud rather than leaving blank.
          <span className={styles.absent}>{t("keysColumn.nothing")}</span>
        )
      },
      meta: { label: t("keysColumn.grants") },
    },
    {
      accessorKey: "lastUsedAt",
      header: t("keysColumn.lastUsed"),
      cell: ({ row }) => {
        const used = row.original.lastUsedAt
        return used ? (
          <span className={styles.scope}>{used}</span>
        ) : (
          <span className={styles.absent}>{t("keysColumn.never")}</span>
        )
      },
      meta: { width: 140, label: t("keysColumn.lastUsed") },
    },
    {
      accessorKey: "expiresInDays",
      header: t("keysColumn.expires"),
      sortFn: numericSort,
      cell: ({ row }) => {
        const { expiresAt, expiresInDays } = row.original
        if (!expiresAt || expiresInDays === null) {
          return (
            <span className={styles.absent}>{t("keysColumn.noExpiry")}</span>
          )
        }
        if (expiresInDays < 0) {
          return (
            <span className={styles.off}>
              {t("keysColumn.expired", { date: expiresAt })}
            </span>
          )
        }
        if (expiresInDays <= EXPIRY_SOON_DAYS) {
          // The count is the reading and the hue is the emphasis, never the
          // other way round: "in 3 days" says it in greyscale too.
          return (
            <span className={styles.warn}>
              {expiresInDays === 0
                ? t("keysColumn.expiresToday")
                : t("keysColumn.expiresInDays", { count: expiresInDays })}
            </span>
          )
        }
        return <span className={styles.scope}>{expiresAt}</span>
      },
      meta: { width: 132, numeric: true, label: t("keysColumn.expires") },
    },
    {
      id: "actions",
      header: t("keysColumn.actions"),
      enableSorting: false,
      cell: ({ row }) => {
        const key = row.original
        if (key.status === "revoked") {
          // Already gone. The row stays as the audit trail and has no act.
          return null
        }
        const busy = revokingId === key.id
        return (
          <span className={styles.actions}>
            <Tooltip content={denial ?? t("keysColumn.revokeKey")}>
              <Button
                size="icon-sm"
                variant="destructive"
                data-test="key-revoke"
                denied={denial}
                loading={busy}
                aria-label={t("keysColumn.revokeKeyAria", {
                  prefix: key.prefix,
                })}
                onClick={(event) => {
                  event.stopPropagation()
                  onRevoke(key)
                }}
              >
                <Ban aria-hidden="true" />
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 72, align: "end", label: t("keysColumn.actions") },
    },
  ]
}
