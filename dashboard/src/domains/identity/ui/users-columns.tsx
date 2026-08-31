import { Link } from "@tanstack/react-router"
import { KeyRound, UserCheck, UserMinus } from "lucide-react"

import type { UserRow, UserStatus } from "@/domains/identity/model/types"
import { can, needsLabel, type Session } from "@/shared/session"
import { Button, Tooltip, type DataColumn } from "@/shared/ui"

import styles from "./identity-table.module.css"

/** Row identity for the virtualized body. Module scope keeps it stable. */
export const getUserId = (user: UserRow) => user.id

const USER_STATUSES: UserStatus[] = ["active", "invited", "disabled"]

export interface UserColumnsOptions {
  /**
   * The signed-in shift itself, not an answer about it.
   *
   * It has to be the session rather than a `useCan` in the cell because a
   * `cell` is not a component: TanStack calls it as a plain function while it
   * builds a row, so a hook inside one is a hook called outside a render —
   * which typechecks and then throws. `can()` and `needsLabel()` are the same
   * rules as plain functions, which is why they are exported beside the hook.
   */
  session: Session
  /** The user id a mutation is currently running against, if any. */
  busyId: string | null
  onLink: (user: UserRow) => void
  onToggleDisabled: (user: UserRow) => void
}

/**
 * Who exists on the platform.
 *
 * The row is arranged around the two questions an administrator actually
 * arrives with: can this person get in, and what do they hold. So the address
 * leads, the status and the identity-provider subject answer the first, and the
 * scopes column answers the second at a glance — an account holding nothing
 * anywhere is a common and quiet state, and it should be visible without
 * opening the grants list.
 */
export function createUserColumns({
  session,
  busyId,
  onLink,
  onToggleDisabled,
}: UserColumnsOptions): DataColumn<UserRow>[] {
  // Identity is a platform act — it reads platform roles alone, and no project
  // id goes in. Being project-admin of every project must never open it.
  const denial = can(session, "identity.manage")
    ? null
    : needsLabel("identity.manage")

  return [
    {
      accessorKey: "email",
      header: "address",
      /* The address is the value the row is about, so the address is what
         opens it — the same way a run id opens its run. Deliberately the cell
         and not the row: a row-wide click target would swallow the two buttons
         in the actions column, and a person who meant to disable an account
         would land on its page instead. */
      cell: ({ row }) => (
        <Link
          to="/identity/users/$userId"
          params={{ userId: row.original.id }}
          className={styles.link}
          data-test="user-link"
          title={row.original.email}
        >
          {row.original.email}
        </Link>
      ),
      meta: {
        width: 200,
        pinned: true,
        label: "address",
        filter: {
          kind: "text",
          placeholder: "filter address, name, subject…",
          match: (user, needle) =>
            // The internal id is in the haystack so a pasted `u_…` finds its
            // person. Nothing shows it, but the global search resolves that
            // shape and hands it here, and a list that cannot receive what it
            // is sent lands the operator on an empty screen.
            `${user.id} ${user.email} ${user.name} ${user.oidcSubject ?? ""}`
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
      header: "account",
      cell: ({ row }) => {
        const status = row.original.status
        // The word carries the reading; the hue only sharpens it. A cell that
        // said this in colour alone would say nothing in greyscale.
        return (
          <span className={status === "disabled" ? styles.off : styles.value}>
            {status}
          </span>
        )
      },
      meta: {
        width: 104,
        label: "account",
        filter: {
          kind: "select",
          placeholder: "all accounts",
          options: USER_STATUSES.map((status) => ({
            value: status,
            label: status,
          })),
        },
      },
    },
    {
      accessorKey: "oidcSubject",
      header: "oidc subject",
      cell: ({ row }) => {
        const subject = row.original.oidcSubject
        return subject ? (
          <span className={styles.scope} title={subject}>
            {subject}
          </span>
        ) : (
          // A local account is not a broken one — OIDC says who you are, and
          // linking is a separate act from existing here.
          <span className={styles.absent}>local only</span>
        )
      },
      meta: { label: "oidc subject" },
    },
    {
      id: "scopes",
      accessorFn: (user) => user.scopes.join(" "),
      header: "holds",
      enableSorting: false,
      cell: ({ row }) => {
        const scopes = row.original.scopes
        return scopes.length > 0 ? (
          <span className={styles.grants} title={scopes.join(", ")}>
            {scopes.join(" · ")}
          </span>
        ) : (
          <span className={styles.absent}>nothing</span>
        )
      },
      meta: { width: 180, label: "holds" },
    },
    {
      accessorKey: "lastSeenAt",
      header: "last seen",
      cell: ({ row }) => {
        const seen = row.original.lastSeenAt
        return seen ? (
          <span className={styles.scope}>{seen}</span>
        ) : (
          <span className={styles.absent}>never</span>
        )
      },
      meta: { width: 140, label: "last seen" },
    },
    {
      id: "actions",
      header: "actions",
      enableSorting: false,
      cell: ({ row }) => {
        const user = row.original
        const busy = busyId === user.id
        const disabled = user.status === "disabled"

        return (
          <span className={styles.actions}>
            {/* The kit tooltip rather than a native `title`: it arrives on
                focus as well as on hover, and a refused act puts its sentence
                where the word would have been.

                Kept now that the address opens a page of its own, because the
                two answer different questions. The address is "show me this
                person"; this is "do the one thing this row says is missing",
                and taking it away would make a local-only account a two-click
                job from the list whose whole point is working down it. */}
            {user.oidcSubject ? null : (
              <Tooltip content={denial ?? "Link oidc subject"}>
                <Button
                  size="icon-sm"
                  variant="ghost"
                  data-test="user-link-oidc"
                  denied={denial}
                  disabled={busy}
                  aria-label={`Link an oidc subject to ${user.email}`}
                  onClick={(event) => {
                    event.stopPropagation()
                    onLink(user)
                  }}
                >
                  <KeyRound aria-hidden="true" />
                </Button>
              </Tooltip>
            )}
            <Tooltip
              content={
                denial ?? (disabled ? "Enable account" : "Disable account")
              }
            >
              <Button
                size="icon-sm"
                variant={disabled ? "ghost" : "destructive"}
                data-test="user-toggle-disabled"
                denied={denial}
                disabled={busy}
                aria-busy={busy || undefined}
                aria-label={
                  disabled ? `Enable ${user.email}` : `Disable ${user.email}`
                }
                onClick={(event) => {
                  event.stopPropagation()
                  onToggleDisabled(user)
                }}
              >
                {disabled ? (
                  <UserCheck aria-hidden="true" />
                ) : (
                  <UserMinus aria-hidden="true" />
                )}
              </Button>
            </Tooltip>
          </span>
        )
      },
      meta: { width: 88, align: "end", label: "actions" },
    },
  ]
}
