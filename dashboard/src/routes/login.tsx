import { createFileRoute, useNavigate } from "@tanstack/react-router"

import { LoginPage, parseLoginSearch } from "@/domains/auth"

/**
 * The one route outside the shell — and outside the session guard.
 *
 * A rail and a topbar on a sign-in screen would offer navigation to someone the
 * product has not yet identified, and the root's `beforeLoad` cannot guard the
 * screen whose whole job is to get you a session in the first place.
 *
 * The three arrivals of §1.3 are `?reason=`, and where sign-in returns to is
 * `?redirect=`. Both are validated here rather than read raw: they come from
 * the address bar, so `redirect` is checked for being an in-app path before the
 * router is ever asked to navigate to it.
 */
export const Route = createFileRoute("/login")({
  validateSearch: parseLoginSearch,
  component: RouteComponent,
})

function RouteComponent() {
  const navigate = useNavigate()
  const { reason, redirect } = Route.useSearch()

  return (
    <LoginPage
      reason={reason}
      redirect={redirect}
      // `href` rather than `to`: the target is a path carried in a search param,
      // so it is a string at runtime and cannot be a literal the router's types
      // know. It has already been checked for being in-app.
      onSignedIn={(target) => void navigate({ href: target, replace: true })}
    />
  )
}
