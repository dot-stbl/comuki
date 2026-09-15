import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import styles from "./surface.module.css"

/**
 * Which edges carry the rule.
 *
 * - `all` — a hairline on four sides. For a block that is one of several: the
 *   rule is what tells this one from the next.
 * - `start` — a hairline on the start edge alone. For a block that owns its
 *   column, where four sides would draw a box around something nothing is
 *   competing with — and where an accent can be read off the edge at a glance
 *   down a stack of tiles.
 */
export type SurfaceBound = "all" | "start"

/**
 * What the rule is saying, when it is saying anything.
 *
 * Never the whole reading: every surface that lights an edge says the same
 * thing in words inside itself. Hue plus a sentence, like a status band.
 */
export type SurfaceTone = "neutral" | "attention" | "danger"

/** How far apart the surface holds its own children. */
export type SurfaceSpacing = "default" | "roomy"

/** The elements a surface is allowed to be. */
export type SurfaceAs = "div" | "article" | "section" | "li"

export interface SurfaceProps {
  bound?: SurfaceBound
  tone?: SurfaceTone
  spacing?: SurfaceSpacing
  /**
   * The element underneath. `div` unless the block means something in the
   * document — `article` for one decision in a queue, `li` for one tile in a
   * list. A surface is chrome; the semantics are the screen's to declare.
   */
  as?: SurfaceAs
  className?: string
  "data-test"?: string
  children: ReactNode
}

const TONE = {
  neutral: undefined,
  attention: styles.attention,
  danger: styles.danger,
} as const

const BOUND = {
  all: styles.all,
  start: styles.start,
} as const

/**
 * A bounded block of the screen's own material.
 *
 * The concept the kit did not have. `Section` is a heading and a stack and says
 * so out loud — "whatever bounds the content below is the content's own
 * business" — which was a correct division of labour and left a hole: six
 * domains then answered "what bounds it" for themselves, four of them with the
 * identical eight declarations, each carrying its own paragraph explaining that
 * it is not a card. This is that paragraph, once.
 *
 * The two compose rather than compete: `Section` names a region, `Surface`
 * bounds a block inside it.
 *
 * ```tsx
 * <Section title="pools" id="pools">
 *   <Surface bound="start" tone={binding ? "attention" : "neutral"}>
 *     <p>plexor · eu-west</p>
 *     <p>at its quota ceiling</p>
 *   </Surface>
 * </Section>
 * ```
 *
 * There is no `radius` prop and there will not be one. `tokens.css` assigns the
 * largest step to "the screen's own surfaces", and a box this size on a smaller
 * corner reads as a square slab while every control standing on it is rounded.
 * Smaller boxes that are *not* this role — a chat's inline reference card, a
 * pickable provider tile — take the step their own size asks for and are not
 * this component.
 */
export function Surface({
  bound = "all",
  tone = "neutral",
  spacing = "default",
  as: Element = "div",
  className,
  "data-test": dataTest,
  children,
}: SurfaceProps) {
  return (
    <Element
      className={cn(
        styles.surface,
        BOUND[bound],
        TONE[tone],
        spacing === "roomy" && styles.roomy,
        className
      )}
      data-tone={tone === "neutral" ? undefined : tone}
      data-test={dataTest}
    >
      {children}
    </Element>
  )
}
