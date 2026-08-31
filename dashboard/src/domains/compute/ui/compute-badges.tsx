import type { ComponentType } from "react"
import { LogOut, Pause, Play, PlugZap } from "lucide-react"

import type { ProviderKind, ProviderState } from "@/domains/compute/model/types"
import { cn } from "@/shared/lib/utils"
import { BrandTag, type BrandId } from "@/shared/ui"

import styles from "./compute-badges.module.css"

/**
 * A state badge and a provider mark, and neither vocabulary is the run's.
 *
 * `StatusBadge` in the kit speaks the six run statuses. A provider is not
 * `queued` and cannot `escalate`; it is active, on standby, draining, or it did
 * not answer. Borrowing the kit badge would mean either lying about the word or
 * widening a shared primitive to carry a vocabulary one screen speaks — the
 * same call the queue screen made for work items and workers, made the same way
 * so the three read as one system: an icon, a hue and a hairline, sized from
 * the same tokens.
 *
 * Every state carries its own silhouette as well as its hue, so the reading
 * survives greyscale, and the state that is not anybody's problem — `standby` —
 * takes the least saturated end on purpose.
 */

const stateIcons: Record<
  ProviderState,
  ComponentType<{ className?: string }>
> = {
  active: Play,
  standby: Pause,
  draining: LogOut,
  unreachable: PlugZap,
}

export interface ProviderStateBadgeProps {
  state: ProviderState
  className?: string
}

export function ProviderStateBadge({
  state,
  className,
}: ProviderStateBadgeProps) {
  const Icon = stateIcons[state]

  return (
    <span
      data-test="provider-state-badge"
      data-state={state}
      className={cn(styles.badge, styles[state], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {state}
    </span>
  )
}

/**
 * Which mark stands for which `IComputeProvider`.
 *
 * Both of v1's backends publish a monochrome mark of their own, so both are
 * drawn rather than spelled: the whale and the helm are recognised faster than
 * the two words ever were, and the words cost a column of width to say it. The
 * map is explicit rather than passing the kind straight through as an id,
 * because `containerd` is already named in `ProviderKind`'s own comment as the
 * next one along — and when it lands it needs a considered answer here, not a
 * lookup that quietly returns nothing.
 */
const KIND_BRAND: Record<ProviderKind, BrandId | null> = {
  docker: "docker",
  kubernetes: "kubernetes",
}

export interface ProviderKindMarkProps {
  kind: ProviderKind
  className?: string
}

/**
 * The implementation behind the provider, as its own mark.
 *
 * No badge chrome and no hue: which `IComputeProvider` a row is has no urgency,
 * it is an identity — and a hairline box around a logo would be drawing a
 * container for a fact that has no state to carry. The name travels with the
 * mark as its accessible name and its hover reading, so a monochrome glyph at
 * table size never becomes the only thing said.
 */
export function ProviderKindMark({ kind, className }: ProviderKindMarkProps) {
  return (
    <BrandTag brand={KIND_BRAND[kind]} label={kind} className={className} />
  )
}
