import { createFileRoute, redirect } from "@tanstack/react-router"

/**
 * `/verify` is no longer a screen — the gate lives as a section of Knowledge
 * now, behind the `gate` tab. The route stays so that everything already
 * pointing here keeps resolving: bookmarks, tickets, stale tabs, and the
 * address somebody half-remembers. A permanent door, not a 404.
 *
 * The redirect carries no permission of its own on purpose. The gate's
 * `verify.view` is asked where the gate is rendered — the tab hides below the
 * screen's own `knowledge.view` — so an old link in the hands of a session
 * that cannot see the gate lands on the library rather than on a denial, and
 * `replace` keeps the dead path out of the history they would press back
 * through.
 */
export const Route = createFileRoute("/verify")({
  beforeLoad: () => {
    throw redirect({
      to: "/knowledge",
      search: { tab: "gate" },
      replace: true,
    })
  },
})
