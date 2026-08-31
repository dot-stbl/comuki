import { createContext, useContext } from "react"

/**
 * The navigation rail's collapse state, and the one way to flip it.
 *
 * The rail is a resizable panel whose imperative handle `AppShell` owns, so
 * only the shell can collapse it — but the control that does the collapsing
 * belongs at the top-left of the page header, where every tool people already
 * use puts it. A context rather than a prop chain: global chrome must not have
 * to travel through the signature of every screen to reach the header.
 */
export interface RailState {
  /** True while the rail is an icon strip rather than a labelled list. */
  railCollapsed: boolean
  /** Collapse an open rail, expand a collapsed one. */
  toggleRail: () => void
}

/**
 * Outside the shell — a story, a unit test — the control still renders and is
 * simply inert: there is no rail to collapse, and a header is not a good place
 * to throw.
 */
export const RailContext = createContext<RailState>({
  railCollapsed: false,
  toggleRail: () => {},
})

export function useRail(): RailState {
  return useContext(RailContext)
}
