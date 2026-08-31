import type { ReactNode } from "react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader, type PageHeaderCrumb } from "@/app/layout/page-header"
import { needsLabel, useCan, type Permission } from "@/shared/session"
import { ForbiddenState } from "@/shared/ui"

export interface RequirePermissionProps {
  /** The act the screen behind this is for. */
  permission: Permission
  /** The screen's name — it still gets a header, so the shell stays navigable. */
  title: string
  /** The crumb path; defaults to the screen standing alone. */
  crumbs?: PageHeaderCrumb[]
  children: ReactNode
}

/**
 * The route half of the access rule.
 *
 * Hiding the rail item is not enough: a URL can be typed, bookmarked, pasted
 * into a ticket or arrived at from a stale tab, and every one of those reaches
 * the screen with the rail's opinion never consulted. This is the check that
 * actually holds — and it is a *client* check, which means it is a courtesy to
 * the operator and not a security boundary. The API answers for the data.
 *
 * The denied branch keeps the whole shell — topbar, rail, header — because the
 * useful thing to do with a closed screen is leave it, and a bare state on an
 * empty floor leaves only the browser's back button. `padded={false}` because
 * `ForbiddenState` carries the content gutter itself, the way every other
 * state in a screen's body does.
 *
 * App policy, not a kit part: it knows the product's permissions and the
 * product's shell, so it lives with the shell.
 */
export function RequirePermission({
  permission,
  title,
  crumbs,
  children,
}: RequirePermissionProps) {
  const { allowed } = useCan(permission)

  if (allowed) {
    return <>{children}</>
  }

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader breadcrumbs={crumbs ?? [{ label: title }]} title={title} />
      }
    >
      <ForbiddenState needs={needsLabel(permission)} subject={title} />
    </AppShell>
  )
}
