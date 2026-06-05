import { cn } from "@/lib/utils"

export type AppBackgroundVariant = "faint-grid"

export interface AppBackgroundProps {
  /**
   * Pattern to render behind content. Reserved for marketing / auth / empty
   * states / hero panels per design spec (A4). Never behind dense data.
   *
   * - `faint-grid` — engineering paper feel, 34px hairline grid, opacity 0.25,
   *   radial mask so it fades near text. From `Comuki Dashboard — Design System`
   *   § A4.
   */
  variant?: AppBackgroundVariant
  className?: string
}

/**
 * Decorative texture layer. Sits **behind** content (`-z-10`), is
 * non-interactive (`pointer-events-none`), and is announced as decorative
 * (`aria-hidden`). Fills its parent — the parent must be `position: relative`
 * (or `absolute`/`fixed`) with `isolate` for stacking context.
 */
export function AppBackground({
  variant = "faint-grid",
  className,
}: AppBackgroundProps) {
  return (
    <div
      aria-hidden="true"
      className={cn(
        "pointer-events-none absolute inset-0 -z-10",
        variant === "faint-grid" && "bg-faint-grid",
        className,
      )}
    />
  )
}
