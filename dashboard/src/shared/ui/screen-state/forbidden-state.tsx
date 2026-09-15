import type { ReactNode } from "react"

import { ScreenState } from "./screen-state"

export interface ForbiddenStateProps {
  /**
   * The sentence naming the roles that would open it — `needsLabel(permission)`
   * from `shared/session`.
   *
   * Passed in rather than derived here on purpose: the kit renders, the app
   * decides policy. A component that imported the permission matrix would drag
   * the product's access rules into every Storybook story that shows a state.
   */
  needs: string
  /** What is closed, in the product's own words — a screen or a list. */
  subject?: string
  /** An extra line, when a screen has something more useful to add. */
  children?: ReactNode
  className?: string
}

/**
 * The fourth list state.
 *
 * §17 names four — Empty, Loading, Error, Forbidden — and this was the one the
 * kit had while the other three were being copied by hand into the domains. It
 * is now what it always claimed to be: the same slot, the same weight, the same
 * construction as the other three, because it is literally
 * {@link ScreenState} with the words already written.
 *
 * It is the one of the four whose sentence the kit writes rather than the call
 * site. A closed view is not a screen-specific event — the answer is always
 * "which role would work", and letting twenty screens phrase that for
 * themselves is how a product ends up apologising on one and accusing on
 * another. So this takes a policy string and a subject and composes the rest.
 *
 * It says what is missing and stops. No lock glyph, no red, no "you are not
 * allowed" — the operator did nothing wrong, they simply hold a different role,
 * and the useful half of the message is which role would work.
 */
export function ForbiddenState({
  needs,
  subject = "This view",
  children,
  className,
}: ForbiddenStateProps) {
  return (
    <ScreenState
      kind="forbidden"
      inset="gutter"
      title={`${subject} is closed to your roles`}
      description={`${needs} — ask for the role, or switch to a project where you already hold it.`}
      className={className}
      data-test="forbidden-state"
    >
      {children}
    </ScreenState>
  )
}
