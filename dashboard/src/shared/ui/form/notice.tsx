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
  /**
   * Whether the band is read out the moment it appears.
   *
   * **Defaults to `tone === "bad"`**, and that default is the whole reason
   * this prop exists rather than a `role` the call site remembers to pass. A
   * `bad` band is what the product answers a refused write with: it appears
   * *because* the operator pressed something, it is the only place the reason
   * is written, and an operator who cannot see the band has no other way to
   * learn the act did not land. Every one of those bands used to carry its own
   * `<p role="alert">` before the band became a primitive, and every one of
   * them lost it on the way in — a regression nobody could see, which is
   * exactly the kind a default is for.
   *
   * The kit already derives this from the variant elsewhere — `ScreenState`
   * announces `error` and nothing else — so the rule is the same rule.
   *
   * Pass it explicitly for the two cases the tone cannot know:
   *
   * - `announce` on an `ok` band that is *also* an answer to a press — a probe
   *   result, a wizard step that just completed. Success is still news when
   *   the operator asked for it.
   * - `announce={false}` on a `bad` band that is a standing property of the
   *   thing on screen rather than an answer — "this connection is in error" is
   *   true before the operator arrives, and interrupting a screen reader to
   *   say what the page already says is noise.
   *
   * The role follows the tone, not this flag: a refusal interrupts
   * (`role="alert"`), an answer waits its turn (`role="status"`). A success
   * read out assertively is a success that cuts off the sentence in progress.
   */
  announce?: boolean
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
 *
 * It is also read out when it is the answer to a press — see
 * {@link NoticeProps.announce} for which tones announce and why the default
 * rather than a prop is what carries it.
 */
export function Notice({
  tone = "warn",
  announce,
  children,
  "data-test": dataTest = "notice",
}: NoticeProps) {
  const Mark = marks[tone]
  const spoken = announce ?? tone === "bad"

  return (
    <p
      className={cn(
        styles.notice,
        tone === "ok" && styles.noticeOk,
        tone === "bad" && styles.noticeBad
      )}
      role={spoken ? (tone === "bad" ? "alert" : "status") : undefined}
      data-test={dataTest}
      data-tone={tone}
    >
      <Mark className={styles.noticeIcon} aria-hidden="true" />
      <span>{children}</span>
    </p>
  )
}
