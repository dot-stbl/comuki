import type { ComponentType } from "react"
import {
  ArrowDown,
  ArrowUp,
  FileText,
  Lock,
  LockOpen,
  Minus,
  Pin,
  Scale,
  Wrench,
} from "lucide-react"

import type {
  EvalDelta,
  KnowledgeKind,
  RuleKind,
} from "@/domains/knowledge/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./knowledge-badges.module.css"

/**
 * The knowledge screen's own marks — and none of them is a run status.
 *
 * `StatusBadge` in the kit speaks the six *run* statuses, and not one of them
 * describes a rule. A rule is `hard` or `soft`; an entry is a rule, a doc or a
 * skill; a golden task got better, worse or stayed put. Borrowing the kit badge
 * would have meant either lying about the word or widening a shared primitive
 * to carry three vocabularies only this screen speaks — the same call the queue
 * and compute screens already made, made the same way so the three read as one
 * system: an icon, a hairline and the same tokens.
 *
 * Where they differ from the queue's is *which* of them get a hue at all. The
 * chrome here is colourless by rule and saturation is reserved for data, so the
 * entry kind — a classification, not a state — is drawn in the neutral ramp: it
 * says which shelf a thing sits on, and nothing about it is urgent. The two
 * vocabularies that carry a consequence (a rule that will stop the swarm, a
 * golden task that regressed) take a hue *and* a silhouette, so both survive
 * greyscale.
 */

const kindIcons: Record<KnowledgeKind, ComponentType<{ className?: string }>> = {
  rule: Scale,
  doc: FileText,
  skill: Wrench,
}

export interface KindMarkProps {
  kind: KnowledgeKind
  className?: string
}

/**
 * Which shelf an entry sits on. Deliberately colourless: a doc is not a state
 * and giving it a hue would spend saturation the flow needs on a filing label.
 */
export function KindMark({ kind, className }: KindMarkProps) {
  const Icon = kindIcons[kind]

  return (
    <span
      data-test="knowledge-kind"
      data-kind={kind}
      className={cn(styles.badge, styles.kind, className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {kind}
    </span>
  )
}

const ruleKindIcons: Record<RuleKind, ComponentType<{ className?: string }>> = {
  hard: Lock,
  soft: LockOpen,
}

export interface RuleKindMarkProps {
  ruleKind: RuleKind
  className?: string
}

/**
 * Whether the rule stops the swarm or advises it.
 *
 * `hard` takes the product's hold hue, because a hard rule is the thing that
 * will refuse a run; `soft` takes the least saturated of the six, because
 * advice is not anybody's problem. The lock says the same thing a second way,
 * so the distinction is still there in greyscale.
 */
export function RuleKindMark({ ruleKind, className }: RuleKindMarkProps) {
  const Icon = ruleKindIcons[ruleKind]

  return (
    <span
      data-test="knowledge-rule-kind"
      data-rule-kind={ruleKind}
      className={cn(styles.badge, styles[ruleKind], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {ruleKind}
    </span>
  )
}

export interface PinnedMarkProps {
  /** Shown as `pinned @ rev` when a revision is worth naming beside it. */
  revision?: string
  className?: string
}

/**
 * An entry every run pins itself to. The one accent on this screen, because
 * being pinned is the reason the reproducibility figure above reads 100%.
 */
export function PinnedMark({ revision, className }: PinnedMarkProps) {
  return (
    <span
      data-test="knowledge-pinned"
      className={cn(styles.pinned, className)}
    >
      <Pin className={styles.icon} aria-hidden="true" />
      {revision ? `pinned @ ${revision}` : "pinned"}
    </span>
  )
}

const deltaIcons: Record<EvalDelta, ComponentType<{ className?: string }>> = {
  "+": ArrowUp,
  "-": ArrowDown,
  "=": Minus,
}

/** The word the arrow stands for. `=` deliberately has no hue: nothing moved. */
const deltaLabels: Record<EvalDelta, string> = {
  "+": "improved",
  "-": "regressed",
  "=": "no change",
}

const deltaClass: Record<EvalDelta, string> = {
  "+": "improved",
  "-": "regressed",
  "=": "unchanged",
}

export interface EvalDeltaMarkProps {
  delta: EvalDelta
  className?: string
}

/**
 * What a rule edit did to one golden task. Arrow and word together — a bare
 * `+` in green is a symbol the operator has to have been taught.
 */
export function EvalDeltaMark({ delta, className }: EvalDeltaMarkProps) {
  const Icon = deltaIcons[delta]

  return (
    <span
      data-test="eval-delta"
      data-delta={deltaLabels[delta]}
      className={cn(styles.badge, styles[deltaClass[delta]], className)}
    >
      <Icon className={styles.icon} aria-hidden="true" />
      {deltaLabels[delta]}
    </span>
  )
}
