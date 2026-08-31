import type { ReactNode } from "react"
import { AlertTriangle, Check, X } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import styles from "./form.module.css"

export interface NoticeProps {
  /**
   * `warn` is the rule the operator has to know before they act; `ok` and `bad`
   * are an answer the product just gave them. Three tones, each with its own
   * mark, because a band that only changed colour would say nothing in
   * greyscale — the same two-channel rule the status bands follow.
   */
  tone?: "warn" | "ok" | "bad"
  children: ReactNode
  "data-test"?: string
}

const marks = {
  warn: AlertTriangle,
  ok: Check,
  bad: X,
}

/**
 * The thing the operator has to know *before* they act, or the answer to the
 * thing they just pressed.
 *
 * A band with a left rule, not a card and not a toast: it belongs to the form
 * it is standing in, it cannot be dismissed, and it is above the button rather
 * than after it. The whole reason it exists is that an irreversible rule
 * explained afterwards is not an explanation — it is an apology.
 */
export function Notice({
  tone = "warn",
  children,
  "data-test": dataTest = "notice",
}: NoticeProps) {
  const Mark = marks[tone]

  return (
    <p
      className={cn(
        styles.notice,
        tone === "ok" && styles.noticeOk,
        tone === "bad" && styles.noticeBad
      )}
      data-test={dataTest}
      data-tone={tone}
    >
      <Mark className={styles.noticeIcon} aria-hidden="true" />
      <span>{children}</span>
    </p>
  )
}
