import { createRouter } from "@tanstack/react-router"

import { routeTree } from "../routeTree.gen"

/**
 * The app router, in its own module so the 401 watcher in `AppProviders`
 * can navigate through it. `main.tsx` is an entry file (excluded from
 * coverage, imported by nothing) — anything else that needs the router
 * used to have no legitimate import path.
 */
export const router = createRouter({
  routeTree,
  defaultPreload: "intent",
  defaultPreloadStaleTime: 0,
})

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router
  }
}
