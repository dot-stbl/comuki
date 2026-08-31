import { createContext, useContext } from "react"

/**
 * True inside a kit `Tooltip`.
 *
 * It exists for one reason: `Button` hands a permission denial to the pointer
 * through the native `title` attribute, which is the only channel an unwrapped
 * control has. Wrap that same button in a `Tooltip` carrying the same sentence
 * and the operator gets the sentence twice, from two tooltips with different
 * shapes and different delays.
 *
 * A context rather than a prop, because the call site that wraps the control is
 * not always the call site that renders it — a column factory hands back a
 * cell, and the screen wraps it.
 */
export const TooltipContext = createContext(false)

export function useInTooltip(): boolean {
  return useContext(TooltipContext)
}
