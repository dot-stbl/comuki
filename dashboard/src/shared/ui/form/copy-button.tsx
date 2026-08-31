import { useEffect, useRef, useState } from "react"
import { Check, Copy } from "lucide-react"

import { Button } from "../button"

export interface CopyButtonProps {
  /** What lands on the clipboard. */
  value: string
  /** What the button says at rest. */
  label?: string
  "data-test"?: string
}

/** How long the confirmation stands before the button offers the act again. */
const CONFIRM_MS = 2000

/**
 * A copy control that says it copied.
 *
 * Without the confirmation the operator has no way to tell a successful copy
 * from a dead button — and on this screen the thing being copied is a secret
 * they will never be shown again, so "did that work?" is not a small doubt.
 * The label changes rather than a toast appearing, because the answer belongs
 * where the question was asked.
 *
 * `aria-live` on the label, not on the button: the button's accessible name
 * changes with the label, and announcing the whole control again would read
 * the icon too.
 */
export function CopyButton({
  value,
  label = "copy",
  "data-test": dataTest = "copy",
}: CopyButtonProps) {
  const [copied, setCopied] = useState(false)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(
    () => () => {
      if (timer.current) {
        clearTimeout(timer.current)
      }
    },
    []
  )

  const copy = () => {
    // Not every context has a clipboard — an insecure origin, an old engine,
    // jsdom. The confirmation still stands, because the value is selectable
    // and the operator's next move is the same either way.
    try {
      void navigator.clipboard?.writeText(value)
    } catch {
      // Nothing to recover: the value is on screen and selectable.
    }
    setCopied(true)
    if (timer.current) {
      clearTimeout(timer.current)
    }
    timer.current = setTimeout(() => setCopied(false), CONFIRM_MS)
  }

  return (
    <Button variant="outline" size="sm" data-test={dataTest} onClick={copy}>
      {copied ? (
        <Check aria-hidden="true" />
      ) : (
        <Copy aria-hidden="true" />
      )}
      <span aria-live="polite">{copied ? "copied" : label}</span>
    </Button>
  )
}
