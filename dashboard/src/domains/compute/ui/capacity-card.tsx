import {
  bindingSentence,
  idleReading,
  readCapacity,
  share,
} from "@/domains/compute/model/capacity"
import type {
  ComputePool,
  ComputeProvider,
  Constraint,
} from "@/domains/compute/model/types"
import { cn } from "@/shared/lib/utils"

import { ProviderKindMark } from "./compute-badges"
import styles from "./capacity-card.module.css"

export interface CapacityCardProps {
  pool: ComputePool
  /** The provider under the pool. `undefined` only while a registry is partial. */
  provider: ComputeProvider | undefined
  /** The project's short handle — what the operator calls it. */
  projectKey: string
  className?: string
}

/**
 * One pool's two ceilings, side by side, with the binding one marked.
 *
 * The screen's reason to exist. v1 scaling is quota-aware *plus* the provider's
 * capacity API, so a scale-up stops at whichever of two independent limits runs
 * out first — and those two limits answer to different people. Shown as two
 * bare numbers this is a subtraction and a comparison an operator has to
 * perform while they are already looking for why nothing started; shown as two
 * tracks against the same axis with one of them marked, it is a glance.
 *
 * Three channels carry the marking, never hue alone: the binding track keeps
 * the word `binding` beside its name, takes the heavier figure, and is the only
 * one drawn in a status hue. The other track stays neutral no matter how full
 * it is, because "72% used" on the side that is not stopping anything is not a
 * reading, it is a distraction.
 *
 * The pool's own knobs sit on the same card rather than in a table three
 * sections down, because `min idle 0` is the sentence that stops an empty pool
 * from reading as an outage — and the place it has to be said is exactly where
 * somebody is looking at a pool with nothing in it.
 */
export function CapacityCard({
  pool,
  provider,
  projectKey,
  className,
}: CapacityCardProps) {
  const reading = readCapacity(pool, provider)
  const quotaBinds = reading.binding === "quota" || reading.binding === "both"
  const capacityBinds =
    reading.binding === "capacity" || reading.binding === "both"

  return (
    <article
      className={cn(styles.card, className)}
      data-test="capacity-card"
      data-pool={`${pool.projectId}/${pool.providerId}`}
      data-binding={reading.binding}
    >
      <header className={styles.head}>
        <h3 className={styles.title}>
          <span className={styles.project}>{projectKey}</span>
          <span className={styles.on}>on</span>
          {/* The backend as its own mark. This is a naming line rather than
              prose — "atlas on <backend>" — so the glyph reads as the identity
              it is, and the heading keeps the whole sentence as its accessible
              name because the mark carries one. A provider the registry has not
              answered for yet has no mark and no word to draw, and says so. */}
          {provider ? (
            <ProviderKindMark
              kind={provider.kind}
              className={styles.providerMark}
            />
          ) : (
            <span className={styles.provider}>unknown</span>
          )}
        </h3>
        <p className={styles.room} data-test="capacity-room">
          {reading.room === null ? (
            <span className={styles.roomNone}>no reading</span>
          ) : (
            <>
              <span className={styles.roomFigure}>{reading.room}</span>{" "}
              <span className={styles.roomUnit}>
                {reading.room === 1 ? "slot free" : "slots free"}
              </span>
            </>
          )}
        </p>
      </header>

      <div className={styles.tracks}>
        <Track
          name="quota"
          constraint={reading.quota}
          binding={quotaBinds}
          room={reading.quotaRoom}
        />
        <Track
          name="allocatable"
          constraint={reading.capacity}
          binding={capacityBinds}
          room={reading.capacityRoom}
        />
      </div>

      <p className={styles.sentence} data-test="capacity-binding">
        {bindingSentence(reading)}
      </p>

      <p className={styles.knobs} data-test="capacity-knobs">
        {idleReading(pool)}
        <span className={styles.knobSep}>·</span>
        <span className={styles.knobFigure}>{pool.workers}</span> up,{" "}
        <span className={styles.knobFigure}>{pool.idle}</span> idle
      </p>
    </article>
  )
}

interface TrackProps {
  name: string
  /** `null` is a provider that did not answer — not a limit of zero. */
  constraint: Constraint | null
  binding: boolean
  room: number | null
}

/**
 * One ceiling: its name, its own numbers, and a bar measured against the same
 * empty channel as the other. Both tracks share the channel width on purpose —
 * two bars on different scales cannot be compared, and comparing them is the
 * entire task.
 *
 * The figures are the reading, so they are text and the bar is decoration on
 * top of it: nothing here is announced only as a length.
 */
function Track({ name, constraint, binding, room }: TrackProps) {
  if (!constraint) {
    return (
      <div className={styles.track} data-track={name} data-empty="">
        <span className={styles.trackName}>{name}</span>
        <span className={styles.channel} aria-hidden="true" />
        <span className={styles.figure}>no answer</span>
      </div>
    )
  }

  return (
    <div
      className={styles.track}
      data-track={name}
      data-binding={binding ? "" : undefined}
    >
      <span className={styles.trackName}>
        {name}
        {binding ? <span className={styles.bindingTag}>binding</span> : null}
      </span>
      <span className={styles.channel} aria-hidden="true">
        <span
          className={styles.fill}
          style={{ inlineSize: `${Math.round(share(constraint) * 100)}%` }}
        />
      </span>
      <span className={styles.figure}>
        <span className={styles.used}>{constraint.used}</span>
        <span className={styles.of}>/</span>
        <span className={styles.limit}>{constraint.limit}</span>{" "}
        <span className={styles.spare}>
          {room === 0 ? "full" : `${room} free`}
        </span>
      </span>
    </div>
  )
}
