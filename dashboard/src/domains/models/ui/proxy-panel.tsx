import { Loader2, Power, PowerOff } from "lucide-react"

import { proxySentence } from "@/domains/models/model/keys"
import type { Proxy } from "@/domains/models/model/types"
import { formatCost } from "@/domains/runs/model/format"
import { can, needsLabel, useSession } from "@/shared/session"
import { Button, Tooltip } from "@/shared/ui"

import styles from "./proxy-panel.module.css"

export interface ProxyPanelProps {
  proxy: Proxy
  busy?: boolean
  onToggle: (next: boolean) => void
}

/** Days, rounded, from a relative age in seconds. */
function days(seconds: number): string {
  const value = Math.round(seconds / 86_400)
  if (value <= 0) {
    return "just now"
  }
  return `${value} ${value === 1 ? "day" : "days"} ago`
}

/**
 * The thin proxy, and what its switch actually decides.
 *
 * It is optional in v1 — a developer may run without it — and it is the one
 * control on this screen that changes what every other section *means*. With it
 * off, workers get a url and a key injected directly: the virtual keys below
 * are not checked, their caps are not enforced, and no run is metered. Printing
 * `proxy: off` and stopping would make that a status; naming the three things
 * that stop happening makes it a reading.
 *
 * The cost figures stay on the panel when it is off, marked as the last
 * metered window rather than as current. A six-day-old cost-per-run shown as
 * live would be a lie; removing it entirely would hide the actual argument for
 * turning the proxy back on.
 */
export function ProxyPanel({ proxy, busy = false, onToggle }: ProxyPanelProps) {
  const session = useSession()
  const denial = can(session, "models.manage")
    ? null
    : needsLabel("models.manage")

  const stale = !proxy.enabled

  return (
    <section
      className={styles.panel}
      data-test="proxy-panel"
      data-enabled={proxy.enabled ? "" : undefined}
    >
      <div className={styles.head}>
        <div className={styles.identity}>
          <p className={styles.state} data-test="proxy-state">
            <span className={styles.stateWord}>
              {proxy.enabled ? "on" : "off"}
            </span>
            <span className={styles.since}>
              since {days(proxy.changedAgoSec)}
            </span>
          </p>
          <p className={styles.sentence}>{proxySentence(proxy.enabled)}</p>
        </div>

        {/* Four words became a switch. The word `on` or `off` beside it is
            already the panel's largest reading, so the glyph says which way
            the act runs and the tooltip and the name say it in full. */}
        <Tooltip
          content={
            denial ??
            (proxy.enabled ? "turn the proxy off" : "turn the proxy on")
          }
        >
          <Button
            size="icon-sm"
            variant={proxy.enabled ? "outline" : "default"}
            data-test="proxy-toggle"
            disabled={busy}
            denied={denial}
            aria-busy={busy || undefined}
            aria-label={
              proxy.enabled ? "turn the proxy off" : "turn the proxy on"
            }
            onClick={() => onToggle(!proxy.enabled)}
          >
            {busy ? (
              <Loader2 className={styles.spin} aria-hidden="true" />
            ) : proxy.enabled ? (
              <PowerOff aria-hidden="true" />
            ) : (
              <Power aria-hidden="true" />
            )}
          </Button>
        </Tooltip>
      </div>

      <dl className={styles.figures} data-test="proxy-figures">
        <div className={styles.figure}>
          <dt className={styles.figureName}>cost per run</dt>
          <dd className={styles.figureValue}>
            {formatCost(proxy.costPerRunUsd)}
          </dd>
        </div>
        <div className={styles.figure}>
          <dt className={styles.figureName}>spend</dt>
          <dd className={styles.figureValue}>{formatCost(proxy.spendUsd)}</dd>
        </div>
        <div className={styles.figure}>
          <dt className={styles.figureName}>runs</dt>
          <dd className={styles.figureValue}>{proxy.runs}</dd>
        </div>
        <p className={styles.window} data-test="proxy-window">
          {stale ? "last metered over " : "over "}
          {proxy.windowLabel}
          {stale ? " — not current" : ""}
        </p>
      </dl>
    </section>
  )
}
