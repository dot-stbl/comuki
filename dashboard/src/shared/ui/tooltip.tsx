import type { DOMAttributes, ReactElement, ReactNode } from "react"
import {
  Focusable,
  Tooltip as AriaTooltip,
  TooltipTrigger,
  type Placement,
} from "react-aria-components"

import { TooltipContext } from "./tooltip-context"

import styles from "./tooltip.module.css"

export interface TooltipProps {
  /**
   * The supplementary reading — usually the word an icon is standing in for.
   *
   * It is a *description*, never the accessible name: React Aria hangs it off
   * the trigger with `aria-describedby`, so the control keeps whatever name it
   * already had and the tooltip is read after it. A control whose only name is
   * its tooltip is a control with no name at all when the pointer is elsewhere.
   */
  content: ReactNode
  /**
   * The control being described. One element, and it must forward its ref to a
   * real node — the hover, the focus and the `aria-describedby` all have to
   * land on something the browser can position against.
   */
  children: ReactElement<DOMAttributes<Element>, string>
  /** Which side the tooltip arrives on. Logical, so it mirrors under RTL. */
  placement?: Placement
  /**
   * Off when the trigger already says what the tooltip would say — a rail item
   * that has its label back does not need the label a second time.
   *
   * A prop rather than "don't render the wrapper": swapping the wrapper in and
   * out remounts the control underneath it, and a remounted element has no
   * previous state to transition from.
   */
  disabled?: boolean
  /** Hover dwell before it opens. Focus is always immediate. */
  delay?: number
}

/**
 * A tooltip — the name an icon lost, handed back on hover and on focus.
 *
 * React Aria owns the behaviour (warmup, the shared close delay, the escape
 * key, touch suppression); this owns the look. `Focusable` is what lets an
 * ordinary anchor or button be the trigger: it clones the trigger props onto
 * the child instead of demanding the child be a React Aria control.
 */
export function Tooltip({
  content,
  children,
  placement = "top",
  disabled = false,
  delay = 400,
}: TooltipProps) {
  return (
    <TooltipTrigger delay={delay} closeDelay={0} isDisabled={disabled}>
      {/* Tells a nested kit control that the pointer is already being served,
          so it does not add a native title saying the same thing. */}
      <TooltipContext value={!disabled}>
        {/* Forwarded, not just consumed above: React Aria's `useFocusable`
            runs an unguarded `console.warn` whenever its child is a disabled
            element, and that warning ships. A steadily disabled control — a
            drain button on a worker already draining — would log it on every
            mount. `isDisabled` tells `Focusable` to stop trying to make it
            focusable, which is the truth about a disabled control anyway. */}
        <Focusable isDisabled={disabled}>{children}</Focusable>
      </TooltipContext>
      <AriaTooltip
        className={styles.tooltip}
        placement={placement}
        offset={6}
        data-test="tooltip"
      >
        {content}
      </AriaTooltip>
    </TooltipTrigger>
  )
}
