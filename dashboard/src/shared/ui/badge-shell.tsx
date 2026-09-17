import { cn } from "@/shared/lib/utils"

import styles from "./badge-shell.module.css"

/**
 * The two steps a mark is ever drawn at.
 *
 * `sm` is the table step — the one nine domain families and every dense row
 * use. `md` is the step a status takes when it is standing on its own in a
 * panel head. There is no third: a fifth size of the same object is how a
 * product ends up with two size languages on one screen, which is precisely
 * what `connection-state-badge` looked like before it was pulled back.
 */
export type BadgeShellSize = "sm" | "md"

export interface BadgeShellOptions {
  /** Defaults to `sm` — the step almost every badge in the product sits at. */
  size?: BadgeShellSize
  className?: string
}

/**
 * The badge's class recipe — a function, deliberately, and not a component.
 *
 * The precedent is `buttonClass` right next door, and the reason is the same
 * one twice over. First, the vocabulary: every badge in this product says a
 * word out of a *closed* set that only one screen speaks — a work item is
 * `succeeded` and never `success`, a connection is `connected` and can never be
 * `escalated`, a check has `never ran` and no run status covers that. Ten
 * screens each explained in their own doc comment why they could not be
 * `StatusBadge`, and they were all right. Widening one primitive to carry ten
 * vocabularies is how a primitive stops meaning anything.
 *
 * Second, what is actually shared. It was never the component — the icon, the
 * hue, the word and the `data-*` attributes are the caller's, and each caller
 * reads them off its own model. It was the *box*: `inline-flex`, a hairline at
 * `--r-sm`, the data voice at one of two steps, and the fit contract that keeps
 * a label inside its own border. That is a class list, so it ships as a class
 * list, and a caller composes it with its own module's rule for colour.
 *
 * ```tsx
 * <span className={cn(badgeShell(), styles.badge, styles[state])}>
 * ```
 *
 * The caller's own sheet declares `--badge-edge` for the hairline's colour
 * rather than `border-color` — see `badge-shell.module.css` for why an
 * override across two CSS modules is a coin toss and a custom property is not.
 */
export function badgeShell(options: BadgeShellOptions = {}): string {
  const { size = "sm", className } = options
  return cn(styles.shell, styles[size], className)
}
