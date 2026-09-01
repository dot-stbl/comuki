import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import type { ReactNode } from "react"
import type { PanelImperativeHandle, PanelSize } from "react-resizable-panels"
import { useNavigate } from "@tanstack/react-router"

import {
  AppShellTwoPaneInner,
  AppShellTwoPaneOuter,
} from "@/app/layout/app-shell-two-pane-sidebar"
import { AppShellTopbar } from "@/app/layout/app-shell-topbar"
import { useActiveNavSection } from "@/app/layout/nav-active-section"
import {
  productNavSections,
  visibleNavSections,
} from "@/app/layout/nav-sections"
import { RailAccount } from "@/app/layout/rail-account"
import { RailContext, type RailState } from "@/app/layout/rail-context"
import { useApprovalsQuery } from "@/domains/approvals/api/queries"
import { ChatDock } from "@/domains/chat"
import { useRunsQuery } from "@/domains/runs/api/queries"
import { useSession } from "@/shared/session"
import { cn } from "@/shared/lib/utils"
import { SplitPane, SplitPanel, SplitSeparator } from "@/shared/ui"

import styles from "./app-shell.module.css"

/** Icon-only rail width, in pixels because that is what the panel measures in. */
const RAIL_COLLAPSED = 48

/**
 * The rail's resting width, mirroring `--w-rail` in `tokens.css`.
 *
 * It is spelled out here rather than read from the token because the panel
 * measures rather than styles: `react-resizable-panels` parses this string into
 * a number, and `var(--w-rail)` parses into nothing. The two have to be changed
 * together — which is why the token is named at all rather than the value being
 * a literal in both places.
 */
const RAIL_DEFAULT = "13.5rem"

/** Below this a rail item has no room for a word beside its icon. */
const RAIL_MIN = "10.5rem"

/** Below this the rail has no room for words and collapses itself. */
const NARROW = "(max-width: 1000px)"

const RAIL_LAYOUT_KEY = "comuki.shell.rail"

export interface AppShellProps {
  children: ReactNode
  /**
   * The screen's fixed chrome — a `PageHeader`. It is pinned above the scroll
   * port rather than living inside it, so it cannot scroll away no matter what
   * a screen does below it.
   */
  header?: ReactNode
  /** Screens that own their own scroll (the duty screen) opt out of padding. */
  padded?: boolean
}

export function AppShell({ children, header, padded = true }: AppShellProps) {
  const rail = useRef<PanelImperativeHandle | null>(null)
  const [railCollapsed, setRailCollapsed] = useState(false)
  // Remembers whether the viewport collapsed the rail, so widening the window
  // only re-opens a rail the user had not closed themselves.
  const collapsedByViewport = useRef(false)
  const navigate = useNavigate()
  const session = useSession()
  /* When the URL is somewhere the rail does not name (`/`, login, …), fall
     back to the first visible section so the inner column is never empty —
     an empty rail next to a page of content reads as broken chrome. */
  const matchedSection = useActiveNavSection(productNavSections)
  const activeSection = useMemo(() => {
    if (matchedSection) {
      return matchedSection
    }
    return visibleNavSections(productNavSections, session)[0]
  }, [matchedSection, session])
  const { data: runs = [] } = useRunsQuery()
  const { data: approvals = [] } = useApprovalsQuery()
  const navCounts = useMemo(
    () => ({
      running: runs.filter((run) => run.status === "running").length,
      needsHuman: approvals.length,
    }),
    [runs, approvals]
  )

  useEffect(() => {
    const query = window.matchMedia(NARROW)

    const apply = (narrow: boolean) => {
      const panel = rail.current
      if (!panel) {
        return
      }
      if (narrow && !panel.isCollapsed()) {
        collapsedByViewport.current = true
        panel.collapse()
        return
      }
      if (!narrow && collapsedByViewport.current) {
        collapsedByViewport.current = false
        panel.expand()
      }
    }

    apply(query.matches)
    const onChange = (event: MediaQueryListEvent) => apply(event.matches)
    query.addEventListener("change", onChange)
    return () => query.removeEventListener("change", onChange)
  }, [])

  const onRailResize = useCallback((size: PanelSize) => {
    setRailCollapsed(size.inPixels <= RAIL_COLLAPSED + 1)
  }, [])

  // Once a human has touched the rail, the viewport stops having an opinion:
  // widening the window must not undo a decision that was made on purpose.
  const toggleRail = useCallback(() => {
    const panel = rail.current
    if (!panel) {
      return
    }
    collapsedByViewport.current = false
    if (panel.isCollapsed()) {
      panel.expand()
    } else {
      panel.collapse()
    }
  }, [])

  const railState = useMemo<RailState>(
    () => ({ railCollapsed, toggleRail }),
    [railCollapsed, toggleRail]
  )

  /**
   * The remembered rail width is a wide-board decision, so a narrow window is
   * not allowed to write one.
   *
   * The layout is a single key. Without this, the collapse the viewport forces
   * at 1000px is persisted like any other, and a session that *ended* narrow —
   * a window left small, a laptop undocked — reopened collapsed on the 27-inch
   * board, because the next mount has no memory that it was the viewport's
   * doing rather than the operator's. The rail stayed shut until someone found
   * the toggle, and nothing on the screen said why.
   *
   * Asked at write time, so it answers about the window as it is rather than as
   * it was: whatever the environment does while the window is narrow is acted
   * on and forgotten, and the width the operator dragged at their desk survives
   * it untouched. Their own toggle still works while narrow — it just is not
   * remembered, which is the honest answer when the viewport rule would undo it
   * on the next load anyway.
   */
  const persistRail = useCallback(
    () => !window.matchMedia(NARROW).matches,
    []
  )

  return (
    <RailContext value={railState}>
      <div className={styles.shell}>
        <AppShellTopbar />
        <div className={styles.body}>
          {/* The outer column is a sibling of the SplitPane, never a child of
             it. `react-resizable-panels` Group only understands Panel and
             Separator children. */}
          <div className={styles.outerPane} data-collapsed>
            <AppShellTwoPaneOuter
              sections={productNavSections}
              activeId={activeSection?.id}
              onSelect={(section) => {
                const first = section.items[0]
                if (first) {
                  void navigate({ to: first.href })
                }
              }}
            />
            {/* `data-collapsed` on the wrapper is what `RailAccount`'s CSS
               keys off — the prop alone only moves the popover. */}
            <RailAccount collapsed />
          </div>

          <SplitPane
            orientation="horizontal"
            storageKey={RAIL_LAYOUT_KEY}
            shouldPersist={persistRail}
            className={styles.split}
          >
            <SplitPanel
              id="rail"
              panelRef={rail}
              defaultSize={RAIL_DEFAULT}
              minSize={RAIL_MIN}
              maxSize="20rem"
              collapsible
              /* The outer column already carries navigation when the inner is
                 gone — collapsing to 48px left an empty strip. Collapse to
                 zero so only the outer survives. */
              collapsedSize={0}
              onResize={onRailResize}
            >
              <AppShellTwoPaneInner
                section={activeSection}
                counts={navCounts}
                collapsed={railCollapsed}
              />
            </SplitPanel>

            <SplitSeparator
              orientation="horizontal"
              aria-label="Resize the navigation rail"
            />

            <SplitPanel id="content" minSize="40%">
              {/* The contract, enforced here rather than left to each screen:
                  the header slot never scrolls, and the region under it owns
                  the scroll for whatever the screen puts in it. */}
              <main className={styles.main}>
                {header ? (
                  <div className={styles.headerSlot}>{header}</div>
                ) : null}
                <div className={cn(styles.content, padded && styles.padded)}>
                  {children}
                </div>
              </main>
            </SplitPanel>
          </SplitPane>
        </div>
        {/* The console's dock: a floating trigger over the board and, behind
            it, the modal bottom sheet that renders the same console component
            the `/chat` route renders. Sits in every screen's shell — the
            console is reachable from anywhere, which is the whole idea of a
            dock. Its own state lives outside the tree, so a navigation (each
            screen mounts its own shell) neither closes an open sheet nor
            drops a draft. Hidden entirely without `chat.use`. */}
        <ChatDock />
      </div>
    </RailContext>
  )
}