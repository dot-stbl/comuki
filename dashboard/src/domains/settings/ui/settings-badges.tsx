import { Check, Feather, Lock, TriangleAlert, Users, Zap } from "lucide-react"

import type {
  AutonomyMode,
  KeyStatus,
  RuleKind,
} from "@/domains/settings/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./settings-badges.module.css"

/**
 * Four marks the control plane needs and the kit does not have.
 *
 * `StatusBadge` speaks the six *run* statuses, and none of these four are one:
 * a rule is `hard` or `soft`, a provider key is `ok` or over budget, a change
 * class is decided by the swarm or by a person, and an environment is a name
 * rather than a state at all. Widening a shared primitive to carry vocabularies
 * only this screen speaks is how a primitive stops meaning anything.
 *
 * They follow the kit's construction exactly, though, because that is what
 * makes them read as the same system: an icon, a hairline and the same tokens,
 * at the badge step of the corner scale. Every value carries a silhouette as
 * well as a hue, so the reading survives greyscale — and the two that describe
 * a *kind* rather than a *state* carry no hue at all, because saturation in
 * this product belongs to the flow and a rule's severity is not a run status.
 */

export interface RuleKindMarkProps {
  kind: RuleKind
  className?: string
}

/**
 * Binding or advisory. Neutral on purpose: `hard` is not a failure and `soft`
 * is not a warning — the lock and the quill say which is which, and the border
 * weight says which one the swarm may not talk its way past.
 */
export function RuleKindMark({ kind, className }: RuleKindMarkProps) {
  const Icon = kind === "hard" ? Lock : Feather

  return (
    <span
      data-test="rule-kind-mark"
      data-kind={kind}
      className={cn(styles.badge, styles[kind], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {kind}
    </span>
  )
}

export interface KeyStatusMarkProps {
  status: KeyStatus
  /** What the provider actually said — `ok`, or `budget 67%`. */
  label: string
  className?: string
}

/**
 * A provider key's health. This one *is* a state somebody has to act on, so it
 * takes a hue — and it says the provider's own words rather than the enum, so
 * `budget 67%` reaches the screen instead of being flattened to `warn`.
 */
export function KeyStatusMark({ status, label, className }: KeyStatusMarkProps) {
  const Icon = status === "ok" ? Check : TriangleAlert

  return (
    <span
      data-test="key-status-mark"
      data-status={status}
      className={cn(styles.badge, styles[status], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {label}
    </span>
  )
}

export interface AutonomyModeMarkProps {
  mode: AutonomyMode
  className?: string
}

/**
 * Who decides this class of change. `human` takes the product's waiting hue on
 * purpose — a change class set to `human` is exactly the thing the run status
 * `waiting on a human` describes, one level up.
 */
export function AutonomyModeMark({ mode, className }: AutonomyModeMarkProps) {
  const Icon = mode === "auto" ? Zap : Users

  return (
    <span
      data-test="autonomy-mode-mark"
      data-mode={mode}
      className={cn(styles.badge, styles[mode], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {mode}
    </span>
  )
}

export interface EnvTagsProps {
  envs: string[]
  className?: string
}

/**
 * The environments an app deploys to. Furniture, not status: hairline chips in
 * the chrome's own material, exactly the toolbar's filter chip — transparent
 * ground, a `--rule-strong` border, no fill and no hue. An app that deploys
 * nowhere says so rather than rendering an empty row of nothing.
 */
export function EnvTags({ envs, className }: EnvTagsProps) {
  if (envs.length === 0) {
    return <span className={styles.absent}>nowhere</span>
  }

  return (
    <span className={cn(styles.tags, className)} data-test="env-tags">
      {envs.map((env) => (
        <span key={env} className={styles.tag}>
          {env}
        </span>
      ))}
    </span>
  )
}
