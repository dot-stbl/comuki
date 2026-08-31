import { Fragment } from "react"
import type { ReactNode } from "react"
import { Link } from "@tanstack/react-router"
import { PanelLeftClose, PanelLeftOpen } from "lucide-react"

import { useRail } from "@/app/layout/rail-context"
import { cn } from "@/shared/lib/utils"
import { Button } from "@/shared/ui"

import styles from "./page-header.module.css"

export interface PageHeaderCrumb {
  label: string
  /**
   * Where the crumb goes. Ancestors have one and are links; the last crumb is
   * the page you are already on, so its destination is ignored.
   */
  to?: string
}

export interface PageHeaderProps {
  /** The path to this screen, the current page last. */
  breadcrumbs: PageHeaderCrumb[]
  /** The screen, named once. Rendered as the page's only `h1`. */
  title: ReactNode
  /** One quiet line under the title — a count, a window, a scope. */
  summary?: ReactNode
  /** This screen's own controls, at the right of the crumb line. */
  actions?: ReactNode
  /**
   * This screen's filter bar — the band under the title. For a screen built on
   * `DataTable` this is its `DataTableToolbar`, moved up out of the scroll port
   * so the controls that narrow a list cannot scroll away from the list they
   * narrow. See the note on the component for the whole contract.
   */
  filters?: ReactNode
  className?: string
}

/**
 * Every screen's header, and the contract that goes with it: `AppShell` renders
 * this above a scroll port it owns, so the header never scrolls and everything
 * below it is the screen's content.
 *
 * It is shell chrome that screens parameterise rather than a kit primitive —
 * it knows about the router and about the navigation rail — so it lives with
 * the shell, not in `shared/ui`.
 *
 * ## Where a screen's chrome goes
 *
 * The header is the one band on a screen that never moves, which makes it the
 * only honest home for anything that acts on *the whole screen*. Three slots,
 * and the split between them is what a control acts on rather than what it
 * looks like:
 *
 * - **`actions`** — verbs aimed at the screen or at the world behind it: "New
 *   task", "Collapse flow", a legend, a refresh. They ride at the end of the
 *   crumb line, opposite the rail control.
 * - **`filters`** — the controls that decide *which rows the screen is showing*.
 *   For a table screen that is its `DataTableToolbar`, handed here whole:
 *
 *   ```tsx
 *   <AppShell
 *     header={
 *       <PageHeader
 *         breadcrumbs={[{ label: "live runs" }]}
 *         title="Live runs"
 *         filters={
 *           <DataTableToolbar
 *             columns={columns}
 *             filters={filters}
 *             onFiltersChange={setFilters}
 *             columnVisibility={columnVisibility}
 *             onColumnVisibilityChange={setColumnVisibility}
 *             trailing={<span>{rows.length} shown</span>}
 *           />
 *         }
 *       />
 *     }
 *   >
 *   ```
 *
 *   The screen keeps the state and keeps the table; only the bar moves. The
 *   slot stretches its child to the full page gutter, so the search field
 *   lands on the same left edge as the first column of the table below it.
 *
 *   What arrives in the slot is one row — a search field, a button holding
 *   every other filter the screen's columns declare, and a chip for each one
 *   that is currently on:
 *
 *   ```
 *   [ search…              ]  [ filters 3 ▾ ]   waiting ×   plexor ×   planner ×
 *   ```
 *
 *   The row grows downward, never sideways: chips wrap under themselves and
 *   the two controls on the left keep their place, so the band's height is the
 *   only thing a filter can change. The slot is a grid track rather than a
 *   flex row for the same reason — a lone track stretches, and a row that
 *   wraps stays inside it.
 * - **`summary`** — a reading, never a control. One quiet line of prose.
 *
 * What does *not* come up here is anything scoped to a selection or a row: a
 * bulk bar belongs beside the rows it acts on, where the selection is visible.
 *
 * `src/domains/runs/pages/runs-page.tsx` is the worked example.
 */
export function PageHeader({
  breadcrumbs,
  title,
  summary,
  actions,
  filters,
  className,
}: PageHeaderProps) {
  const { railCollapsed, toggleRail } = useRail()
  const RailIcon = railCollapsed ? PanelLeftOpen : PanelLeftClose
  const last = breadcrumbs.length - 1

  return (
    <header className={cn(styles.header, className)} data-test="page-header">
      <div className={styles.top}>
        <Button
          variant="ghost"
          size="icon-sm"
          className={styles.rail}
          data-test="rail-toggle"
          aria-controls="rail"
          aria-expanded={!railCollapsed}
          aria-label={
            railCollapsed
              ? "Show the navigation rail"
              : "Hide the navigation rail"
          }
          onClick={toggleRail}
        >
          <RailIcon aria-hidden="true" />
        </Button>

        <nav aria-label="Breadcrumb" className={styles.crumbs}>
          {breadcrumbs.map((crumb, index) => {
            const current = index === last
            return (
              <Fragment key={crumb.label}>
                {index > 0 ? (
                  <span className={styles.connector} aria-hidden="true" />
                ) : null}
                {current || !crumb.to ? (
                  <span
                    className={styles.crumb}
                    aria-current={current ? "page" : undefined}
                  >
                    {crumb.label}
                  </span>
                ) : (
                  <Link
                    to={crumb.to}
                    data-test="crumb"
                    className={cn(styles.crumb, styles.crumbLink)}
                  >
                    {crumb.label}
                  </Link>
                )}
              </Fragment>
            )
          })}
        </nav>

        {actions ? <div className={styles.actions}>{actions}</div> : null}
      </div>

      <h1 className={styles.title}>{title}</h1>
      {summary ? <p className={styles.summary}>{summary}</p> : null}

      {filters ? (
        <div className={styles.filters} data-test="page-header-filters">
          {filters}
        </div>
      ) : null}
    </header>
  )
}
