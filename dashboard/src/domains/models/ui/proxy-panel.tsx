import { Power, PowerOff } from "lucide-react"

import { burnPeak, hourLabel, proxySentence } from "@/domains/models/model/keys"
import type { Proxy } from "@/domains/models/model/types"
import { formatCost } from "@/domains/runs/model/format"
import { can, needsLabel, useSession } from "@/shared/session"
import { Button, Sparkline, Surface, Tooltip } from "@/shared/ui"
import { formatRelativeTime } from "@/shared/lib/relative-time"
import { cn } from "@/shared/lib/utils"

import styles from "./proxy-panel.module.css"

export interface ProxyPanelProps {
  proxy: Proxy
  busy?: boolean
  /**
   * The switch, when the surface has one. Absent in real mode: the host's
   * key store is config-seeded and immutable at runtime (a PATCH answers
   * 501), and the panel states that as a fact about the proxy rather than
   * offering an act the host has already refused.
   */
  onToggle?: (next: boolean) => void
}

/** The burn reading in words — the sparkline's whole accessible name. */
function burnLabel(proxy: Proxy): string {
  const peak = burnPeak(proxy.burnHourlyUsd)
  if (!peak) {
    return "Spend by hour: nothing metered."
  }
  return `Spend by hour across the metered day, peak ${formatCost(peak.usd)} at ${hourLabel(peak.hour)}.`
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
  const peak = burnPeak(proxy.burnHourlyUsd)
  // Real mode: no switch to carry, and no metered figures to state — the
  // catalogue says the proxy is composed, and inventing a cost-per-run would
  // be the lie this panel exists to prevent.
  const metered = onToggle !== undefined

  return (
    /* The kit's `Surface`, not a card and not a hand-spelled panel: a hairline
       on the start edge, the lane material and the screen-surface corner. The
       edge is the accent channel, so `tone` is read off the switch — a proxy
       that is off marks its own edge and says so in words inside itself.

       `Surface` forwards `data-test` and nothing else, on purpose. The
       `data-enabled` hook therefore moves to the line it was always about —
       the word `on` or `off` — and the figures' staleness becomes a class on
       the figures, which is where that reading applies. */
    <Surface
      as="section"
      bound="start"
      tone={proxy.enabled ? "neutral" : "attention"}
      spacing="roomy"
      data-test="proxy-panel"
    >
      <div className={styles.head}>
        <div className={styles.identity}>
          <p
            className={styles.state}
            data-test="proxy-state"
            data-enabled={proxy.enabled ? "" : undefined}
          >
            <span className={styles.stateWord}>
              {proxy.enabled ? "on" : "off"}
            </span>
            {metered ? (
              <span className={styles.since}>
                since {formatRelativeTime(proxy.changedAgoSec * 1000)}
              </span>
            ) : null}
          </p>
          <p className={styles.sentence}>
            {metered
              ? proxySentence(proxy.enabled)
              : "virtual keys are seeded from configuration and immutable at runtime — spend is metered at the proxy, not reported here"}
          </p>
        </div>

        {/* Four words became a switch. The word `on` or `off` beside it is
            already the panel's largest reading, so the glyph says which way
            the act runs and the tooltip and the name say it in full. */}

        {metered && onToggle ? (
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
              loading={busy}
              denied={denial}
              aria-label={
                proxy.enabled ? "turn the proxy off" : "turn the proxy on"
              }
              onClick={() => onToggle(!proxy.enabled)}
            >
              {proxy.enabled ? (
                <PowerOff aria-hidden="true" />
              ) : (
                <Power aria-hidden="true" />
              )}
            </Button>
          </Tooltip>
        ) : null}
      </div>

      {metered ? (
        <dl
          className={cn(styles.figures, stale && styles.figuresStale)}
          data-test="proxy-figures"
        >
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
          {/* The shape of the metered day, beside the figures that say it in
              words: a quiet night, the morning ramp, the heavy afternoon. The
              line takes the same staleness the values take when the proxy is
              off, because a six-day-old burn curve shown as live would be the
              same lie a six-day-old cost-per-run would. */}
          {peak ? (
            <div className={cn(styles.figure, styles.burn)}>
              <dt className={styles.figureName}>burn by hour</dt>
              <dd className={styles.figureValue} data-test="proxy-burn">
                <Sparkline
                  className={stale ? styles.burnStale : undefined}
                  values={proxy.burnHourlyUsd}
                  label={burnLabel(proxy)}
                />
                <span className={styles.burnPeak}>
                  peak {formatCost(peak.usd)} at {hourLabel(peak.hour)}
                </span>
              </dd>
            </div>
          ) : null}
          <p className={styles.window} data-test="proxy-window">
            {stale ? "last metered over " : "over "}
            {proxy.windowLabel}
            {stale ? " — not current" : ""}
          </p>
        </dl>
      ) : null}
    </Surface>
  )
}
