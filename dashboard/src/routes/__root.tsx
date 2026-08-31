import { Outlet, createRootRoute } from "@tanstack/react-router"

import { guardSession } from "@/domains/auth"

export const Route = createRootRoute({
  /**
   * The session check, once, for every route there is and every route there
   * will be. It runs before any screen's loader or component, so an
   * unidentified visitor never sees half a shell on the way to being turned
   * away — and the screen added next week inherits the guard instead of
   * remembering it. `/login` is the one exemption, and `guardSession` owns it.
   */
  beforeLoad: ({ location }) => {
    guardSession(location)
  },
  component: RootComponent,
})

function RootComponent() {
  return (
    <>
      <Outlet />
    </>
  )
}
