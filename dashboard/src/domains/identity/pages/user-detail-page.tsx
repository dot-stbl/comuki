import { useMemo } from "react"
import { Link, useNavigate } from "@tanstack/react-router"
import {
  ArrowLeft,
  KeyRound,
  RotateCw,
  UserCheck,
  UserMinus,
} from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { useIdentityQuery } from "@/domains/identity/api/queries"
import { useUserDisabledAct } from "@/domains/identity/ui/use-user-disabled"
import { cn } from "@/shared/lib/utils"
import { useCan } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  Section,
  Tooltip,
  buttonClass,
} from "@/shared/ui"

import styles from "./user-detail-page.module.css"

const SKELETON_WIDTHS = ["46%", "68%", "38%", "72%"]

export interface UserDetailPageProps {
  /** From the path. An account is a thing, so it has an address of its own. */
  userId: string
}

/**
 * One person, at `/identity/users/<id>`.
 *
 * The list answers "who exists"; this answers "who is this". The difference is
 * not more columns — it is the facts that are only knowable about *one*
 * account and are therefore illegible in a row: whether a provider subject was
 * ever written, whether the account has ever been used, and what it holds and
 * where. Everything else is handed off with a count and a link, because **a
 * detail page links to the real screens with a filter applied; it does not
 * redraw their tables.** A grants table drawn here would be a second grants
 * table to keep in step with the first, narrowed by a URL parameter this
 * product would then have two spellings of.
 *
 * It reads `useIdentityQuery()` — the whole section in one payload — for the
 * reason that query's own comment gives: the three lists reference each other,
 * and a page that fetched this account and its grants separately could render
 * a grant against an account the other half had already switched off.
 *
 * The page takes the id as a prop rather than reading the route itself, so it
 * can be mounted in a story and in a test without the generated route tree.
 * `LinkOidcPage` beside it already does this and it is the better pattern.
 */
export function UserDetailPage({ userId }: UserDetailPageProps) {
  const { data, isLoading, isError, error, refetch } = useIdentityQuery()
  const navigate = useNavigate()
  // Identity is a platform act: `can` with no project id. Being project-admin
  // of every project must never open it.
  const manage = useCan("identity.manage")
  const disable = useUserDisabledAct()

  const user = data?.users.find((entry) => entry.id === userId) ?? null

  /* What this person holds, from the payload the screen already has. `grants`
     is the same list the role-assignments table reads, filtered to one
     subject — not a second query and not a second shape. */
  const held = useMemo(
    () =>
      (data?.grants ?? []).filter(
        (grant) => grant.subjectKind === "user" && grant.subjectId === userId
      ),
    [data, userId]
  )

  const keysInForce = (data?.keys ?? []).filter(
    (key) => key.status === "active"
  ).length

  const missing = !isLoading && !isError && !user
  const off = user?.status === "disabled"
  const busy = user !== null && disable.busyId === user.id

  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={[
            { label: "platform" },
            { label: "identity", to: "/identity" },
            // The address, not the id: the URL names an id and an id is not
            // something an administrator recognises. Before the payload lands
            // there is nothing to name it with, so the crumb says what kind of
            // thing this is instead of naming the wrong one.
            { label: user?.email ?? "person" },
          ]}
          title={user?.name ?? "Person"}
          summary={
            user ? (
              <>
                <span className={styles.address}>{user.email}</span>
                {" · "}
                <span className={off ? styles.off : undefined}>
                  {user.status}
                </span>
              </>
            ) : undefined
          }
          actions={
            user ? (
              /* The one act on this page. Gated and *visible*: a screen that
                 hid it would teach nobody what to ask for. Same spelling as
                 the row in the list — icon, kit tooltip, real name. */
              <Tooltip
                content={
                  manage.denial ?? (off ? "Enable account" : "Disable account")
                }
              >
                <Button
                  size="icon-sm"
                  variant={off ? "ghost" : "destructive"}
                  data-test="user-toggle-disabled"
                  denied={manage.denial}
                  disabled={busy}
                  aria-busy={busy || undefined}
                  aria-label={
                    off ? `Enable ${user.email}` : `Disable ${user.email}`
                  }
                  onClick={() => disable.toggle(user)}
                >
                  {off ? (
                    <UserCheck aria-hidden="true" />
                  ) : (
                    <UserMinus aria-hidden="true" />
                  )}
                </Button>
              </Tooltip>
            ) : null
          }
        />
      }
    >
      {/* No height and no `flex: 1` on this column: `AppShell` already owns a
          sized scroll port, and a reading that ends where its last fact ends
          must be allowed to do so. See the height-chain note in
          `form-page.module.css` — this screen is that shape, not the duty
          board's. */}
      <div className={styles.screen} data-test="user-detail">
        {isLoading ? (
          <div className={styles.skeleton} data-test="user-loading">
            {SKELETON_WIDTHS.map((width, index) => (
              <span
                key={index}
                className={styles.skeletonBar}
                style={{ width }}
              />
            ))}
          </div>
        ) : null}

        {isError ? (
          <div className={styles.state} role="alert">
            <p className={styles.stateTitle}>This account did not load</p>
            <p className={styles.stateBody}>
              {error instanceof Error ? error.message : "Unknown error"}
            </p>
            <span>
              <Tooltip content="Retry">
                <Button
                  size="icon-sm"
                  data-test="user-retry"
                  aria-label="Retry"
                  onClick={() => {
                    void refetch()
                  }}
                >
                  <RotateCw aria-hidden="true" />
                </Button>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {missing ? (
          /* The id resolved to nothing, and the answer names the thing that is
             missing rather than saying "not found". A stale tab and an old
             link are the ordinary ways to arrive here — the same two cases the
             link page already answers for, in the same register. */
          <div className={styles.state} data-test="user-not-found">
            <p className={styles.stateTitle}>No account with that id</p>
            <p className={styles.stateBody}>
              No account on this platform has the id{" "}
              <span className={styles.id}>{userId}</span>. It may have been
              removed since this link was written, or the link may have been
              copied from somewhere that never had it.
            </p>
            <span>
              <Tooltip content="Back to identity">
                <Link
                  to="/identity"
                  aria-label="Back to identity"
                  className={buttonClass({ size: "icon-sm" })}
                >
                  <ArrowLeft aria-hidden="true" />
                </Link>
              </Tooltip>
            </span>
          </div>
        ) : null}

        {user ? (
          <>
            <Section
              id="user-account"
              title="account"
              data-test="user-account"
              className={styles.region}
            >
              <dl className={styles.facts}>
                <div className={styles.fact}>
                  <dt className={styles.factName}>address</dt>
                  <dd className={styles.factValue}>{user.email}</dd>
                </div>

                <div className={styles.fact}>
                  <dt className={styles.factName}>name</dt>
                  {/* A person's name is the one thing on this screen a human
                      wrote, so it is the one thing in the interface voice. */}
                  <dd className={styles.factProse}>{user.name}</dd>
                </div>

                <div className={styles.fact}>
                  <dt className={styles.factName}>account</dt>
                  {/* The word carries the reading; the hue only sharpens it. A
                      cell that said this in colour alone would say nothing in
                      greyscale. */}
                  <dd
                    className={cn(styles.factValue, off && styles.off)}
                    data-test="user-status"
                  >
                    {user.status}
                  </dd>
                </div>

                <div className={styles.fact}>
                  <dt className={styles.factName}>oidc subject</dt>
                  {user.oidcSubject ? (
                    /* Already written, and there is nothing on offer beside
                       it. Relinking is not an act this product has — a subject
                       is written once, and a screen that offered to overwrite
                       one silently would be inventing the act. */
                    <dd className={styles.factValue} data-test="user-subject">
                      {user.oidcSubject}
                    </dd>
                  ) : (
                    <dd className={styles.factAct} data-test="user-subject">
                      {/* Local only is not broken. OIDC says who you are, and
                          linking is a separate act from existing here — so the
                          fact carries the act rather than an apology. */}
                      <span className={styles.absent}>local only</span>
                      <Tooltip
                        content={manage.denial ?? "Link an oidc subject"}
                      >
                        <Button
                          size="icon-sm"
                          variant="ghost"
                          data-test="user-link-oidc"
                          denied={manage.denial}
                          aria-label={`Link an oidc subject to ${user.email}`}
                          onClick={() => {
                            void navigate({
                              to: "/identity/users/$userId/link",
                              params: { userId: user.id },
                            })
                          }}
                        >
                          <KeyRound aria-hidden="true" />
                        </Button>
                      </Tooltip>
                    </dd>
                  )}
                </div>

                <div className={styles.fact}>
                  <dt className={styles.factName}>last seen</dt>
                  <dd
                    className={
                      user.lastSeenAt ? styles.factValue : styles.absent
                    }
                    data-test="user-last-seen"
                  >
                    {/* `never` is a real answer for an account that was
                        invited and has not arrived. A blank reads as a broken
                        render. */}
                    {user.lastSeenAt ?? "never"}
                  </dd>
                </div>

                <div className={styles.fact}>
                  <dt className={styles.factName}>created</dt>
                  <dd className={styles.factValue}>{user.createdAt}</dd>
                </div>
              </dl>
            </Section>

            <Section
              id="user-roles"
              title="roles by project"
              note={`${held.length} held`}
              data-test="user-roles"
              className={styles.region}
            >
              {held.length > 0 ? (
                <ul className={styles.grants}>
                  {held.map((grant) => (
                    <li
                      key={grant.id}
                      className={styles.grant}
                      data-test="user-grant"
                    >
                      <span
                        className={cn(
                          styles.role,
                          grant.subjectInactive && styles.inert
                        )}
                      >
                        {grant.role}
                      </span>
                      <span className={styles.on}>on</span>
                      <span
                        className={cn(
                          styles.scope,
                          grant.subjectInactive && styles.inert
                        )}
                      >
                        {grant.scopeLabel}
                      </span>
                      <span className={styles.granted}>
                        granted {grant.grantedAt}
                      </span>
                      {grant.subjectInactive ? (
                        /* A grant on a disabled account is a real row and an
                           inert one, and saying so is the whole reason this
                           screen is worth reading: disabling somebody and
                           un-granting them are different acts, and an account
                           that comes back should come back as itself. */
                        <span
                          className={styles.inertNote}
                          data-test="user-grant-inert"
                        >
                          inert while the account is disabled
                        </span>
                      ) : null}
                    </li>
                  ))}
                </ul>
              ) : (
                /* An account that holds nothing anywhere is a common and quiet
                   state, so it is said in words. An empty box would read as a
                   list that failed to load. */
                <p className={styles.quiet} data-test="user-holds-nothing">
                  This account holds nothing — no role on the platform and none
                  on any project. It can sign in and it can see nothing until
                  somebody grants it something.
                </p>
              )}

              <div className={styles.handoffs}>
                {/* Granting is a form, so it is a page, so it is spelled as
                    navigation — and gated the way the list's own new-user link
                    is: allowed, an anchor; refused, a button that stays where
                    it was and says what it needs. An anchor has no way to
                    refuse and explain itself. */}
                {manage.allowed ? (
                  <Link
                    to="/identity/grants/new"
                    data-test="user-grant-new"
                    className={buttonClass({ variant: "link", size: "sm" })}
                  >
                    grant a role
                  </Link>
                ) : (
                  <Button
                    variant="link"
                    size="sm"
                    data-test="user-grant-new"
                    denied={manage.denial}
                  >
                    grant a role
                  </Button>
                )}

                {/* The list, narrowed to this person, in the URL the product
                    already spells. The grants list matches on `subjectLabel`,
                    which is the address — so the link lands rather than
                    arriving on an empty table. */}
                <Link
                  to="/identity"
                  search={{ tab: "grants", q: user.email }}
                  data-test="user-grants-all"
                  className={buttonClass({ variant: "link", size: "sm" })}
                >
                  every assignment for this person
                </Link>
              </div>
            </Section>

            <Section
              id="user-keys"
              title="api keys"
              note={`${keysInForce} in force`}
              data-test="user-keys"
              className={styles.region}
            >
              {/* The honest modelling fact, stated rather than invented
                  around: a key is a subject in its own right here. It is
                  granted roles directly, it appears in the assignments list
                  beside people, and nothing on it records who made it — so
                  this page has no "their keys" to show and does not fabricate
                  one. The count is the platform's, and it says so. */}
              <p className={styles.quiet} data-test="user-keys-note">
                An api key is a subject in its own right on this platform: it
                holds its own roles and answers for itself, so it belongs to no
                account and there is no such thing as this person&apos;s keys.{" "}
                <span className={styles.figure}>{keysInForce}</span> are in
                force across the platform.
              </p>

              <div className={styles.handoffs}>
                {manage.allowed ? (
                  <Link
                    to="/identity/keys/new"
                    data-test="user-key-new"
                    className={buttonClass({ variant: "link", size: "sm" })}
                  >
                    new api key
                  </Link>
                ) : (
                  <Button
                    variant="link"
                    size="sm"
                    data-test="user-key-new"
                    denied={manage.denial}
                  >
                    new api key
                  </Button>
                )}

                <Link
                  to="/identity"
                  search={{ tab: "keys" }}
                  data-test="user-keys-all"
                  className={buttonClass({ variant: "link", size: "sm" })}
                >
                  every api key
                </Link>
              </div>
            </Section>
          </>
        ) : null}
      </div>

      {/* The words are the act's own — this screen only says where they
          appear. See `use-user-disabled.ts`. */}
      <ConfirmDialog {...disable.dialog} />
    </AppShell>
  )
}
