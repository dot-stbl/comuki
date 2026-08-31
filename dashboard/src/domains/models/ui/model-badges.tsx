import type { ComponentType } from "react"
import { Ban, Check, Clock, Power, TriangleAlert } from "lucide-react"

import { keyState } from "@/domains/models/model/keys"
import type {
  EndpointState,
  ModelWire,
  VirtualKey,
} from "@/domains/models/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./model-badges.module.css"

/**
 * Three badges, and none of the three vocabularies is the run's.
 *
 * `StatusBadge` in the kit speaks the six run statuses. An endpoint is not
 * `queued`; a key is not `escalated`. Borrowing the kit badge would mean either
 * lying about the word or widening a shared primitive to carry a vocabulary one
 * screen speaks — the same call the queue screen made, made the same way, so
 * the screens read as one system: an icon, a hue and a hairline, sized from the
 * same tokens.
 *
 * Every value carries a silhouette as well as a hue, so the reading survives
 * greyscale — and the wire badge takes no hue at all, because which protocol an
 * upstream speaks is an identity rather than a state.
 */

const endpointIcons: Record<
  EndpointState,
  ComponentType<{ className?: string }>
> = {
  ok: Check,
  degraded: TriangleAlert,
  disabled: Power,
}

export interface EndpointStateBadgeProps {
  state: EndpointState
  className?: string
}

export function EndpointStateBadge({
  state,
  className,
}: EndpointStateBadgeProps) {
  const Icon = endpointIcons[state]

  return (
    <span
      data-test="endpoint-state-badge"
      data-state={state}
      className={cn(styles.badge, styles[state], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {state}
    </span>
  )
}

const keyIcons = {
  live: Check,
  expired: Clock,
  revoked: Ban,
} satisfies Record<string, ComponentType<{ className?: string }>>

export interface KeyStateBadgeProps {
  /** The key itself, because its state is derived rather than stored. */
  entry: VirtualKey
  className?: string
}

/**
 * A key's state is a reading, not a field: it lapses on its own clock, and
 * revocation overrides that. Taking the key rather than a string is what keeps
 * the badge and the table from ever disagreeing about which rule won.
 */
export function KeyStateBadge({ entry, className }: KeyStateBadgeProps) {
  const state = keyState(entry)
  const Icon = keyIcons[state]

  return (
    <span
      data-test="key-state-badge"
      data-state={state}
      className={cn(styles.badge, styles[state], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {state}
    </span>
  )
}

export interface WireBadgeProps {
  wire: ModelWire
  className?: string
}

/**
 * The protocol, not a state: deliberately unsaturated.
 *
 * **And deliberately a word rather than a mark.** Every other provider on this
 * product is drawn as its own logo now — the sources table shows the octocat,
 * the compute registry shows the whale — and this column is the one that must
 * not. `ModelWire` is `"openai" | "anthropic"`, and those name *wire
 * protocols*, not the companies that first shipped them. The registry beside
 * this badge makes the gap concrete: `ep_self_host` is a vLLM url on the
 * cluster's own network sitting on the `openai` wire, and it has no commercial
 * relationship with anybody. Drawing a vendor's mark on that row would assert
 * one. Until the model registry stores a vendor — which it does not, and may
 * never, because the platform speaks in roles and never in vendors — the wire
 * stays two lower-case words.
 */
export function WireBadge({ wire, className }: WireBadgeProps) {
  return (
    <span
      data-test="wire-badge"
      data-wire={wire}
      className={cn(styles.badge, styles.wire, className)}
    >
      {wire}
    </span>
  )
}
