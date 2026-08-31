import { Ban } from "lucide-react"

import { EXPIRY_SOON_DAYS } from "@/domains/identity/model/identity"
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
}: KeyColumnsOptions): DataColumn<ApiKeyRow>[] {
  const denial = can(session, "identity.manage")
    ? null
    : needsLabel("identity.manage")

  return [
    {
      accessorKey: "prefix",
      header: "prefix",
      cell: ({ row }) => (
        <span className={styles.value}>{row.original.prefix}</span>
      ),
      meta: {
        width: 132,
        pinned: true,
        filter: {
          kind: "text",
          placeholder: "filter prefix, name, grant…",
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
      header: "name",
      cell: ({ row }) => (
        <span className={styles.name} title={row.original.name}>
          {row.original.name}
        </span>
      ),
      meta: { width: 160 },
    },
    {
      accessorKey: "status",
      header: "key",
      cell: ({ row }) => {
        const status = row.original.status
        return (
          <span className={status === "revoked" ? styles.off : styles.value}>
            {status}
          </span>
        )
      },
      meta: {
        width: 96,
        label: "key",
        filter: {
          kind: "select",
          placeholder: "all keys",
          options: [
            { value: "active", label: "active" },
            { value: "revoked", label: "revoked" },
          ],
        },
      },
    },
    {
      id: "grants",
      accessorFn: (key) => key.grants.join(" "),
      header: "grants",
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
          <span className={styles.absent}>nothing</span>
        )
      },
      meta: { label: "grants" },
    },
    {
      accessorKey: "lastUsedAt",
      header: "last used",
      cell: ({ row }) => {
        const used = row.original.lastUsedAt
        return used ? (
          <span className={styles.scope}>{used}</span>
        ) : (
          <span className={styles.absent}>never</span>
        )
      },
      meta: { width: 140, label: "last used" },
    },
    {
      accessorKey: "expiresInDays",
      header: "expires",
      sortFn: numericSort,
      cell: ({ row }) => {
        const { expiresAt, expiresInDays } = row.original
        if (!expiresAt || expiresInDays === null) {
          return <span className={styles.absent}>no expiry</span>
        }
        if (expiresInDays < 0) {
          return <span className={styles.off}>expired {expiresAt}</span>
        }
        if (expiresInDays <= EXPIRY_SOON_DAYS) {
          // The count is the reading and the hue is the emphasis, never the
          // other way round: "in 3 days" says it in greyscale too.
          return (
            <span className={styles.warn}>
              {expiresInDays === 0
                ? "expires today"
                : `in ${expiresInDays} days`}
            </span>
          )
        }
        return <span className={styles.scope}>{expiresAt}</span>
      },
      meta: { width: 132, numeric: true, label: "expires" },
    },
    {
      id: "actions",
      header: "actions",
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
            <Tooltip content={denial ?? "Revoke key"}>
              <Button
                size="icon-sm"
                variant="destructive"
                data-test="key-revoke"
                denied={denial}
                disabled={busy}
                aria-busy={busy || undefined}
                aria-label={`Revoke key ${key.prefix}`}
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
      meta: { width: 72, align: "end", label: "actions" },
    },
  ]
}
