import type { HTMLAttributes, ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import { Surface, type SurfaceAs, type SurfaceTone } from "../surface"

import styles from "./stat-tile.module.css"

export interface StatLabelProps extends Omit<
  HTMLAttributes<HTMLSpanElement>,
  "children"
> {
  /**
   * The element underneath. A `<span>` unless the name means something in the
   * document, which in this product is exactly one case: the `<dt>` of a
   * definition list, where the pairing of a name with a value *is* the
   * structure and a span would throw it away. Not a free-for-all — two
   * elements, because those are the two readings this voice has.
   */
  as?: "span" | "dt"
  children: ReactNode
}

/**
 * The line that names a value.
 *
 * Exported on its own for the same reason `FieldLabel` is: the voice is wanted
 * by things that are not the component it came from. A capacity card's
 * per-track legend, a detail screen's fact name and a tile's own label all have
 * to read exactly alike — same family, size, weight, tracking, ink — or two
 * readings on one screen measure differently. The five declarations that make
 * that true are the most hand-copied rule in the product.
 *
 * `FieldLabel` stays separate and is not this: it carries the required-word and
 * belongs to a control. This one names a *reading*.
 */
export function StatLabel({
  as: As = "span",
  children,
  className,
  ...rest
}: StatLabelProps) {
  return (
    <As {...rest} className={cn(styles.label, className)}>
      {children}
    </As>
  )
}

export interface StatFigureProps {
  /**
   * The figure, already formatted.
   *
   * A string rather than a node, because the precision is a decision the call
   * site has already made — cents for a per-success price, whole dollars for a
   * day's total, whole percent for a cap — and because a node here would let a
   * caller put prose in the data voice, which is the one defect the two-voices
   * rule exists to catch.
   */
  value: string
  /** A currency mark riding before the figure. */
  prefix?: string
  /** A unit riding after it — `slots free`, `%`, `/ $220`. */
  suffix?: string
  /**
   * What the reading means, when it means anything.
   *
   * The kit's own three-word tone vocabulary rather than any domain's heat
   * vocabulary: a tile does not know what a budget is. Domains map their own
   * reading onto this — `cost` maps `BudgetHeat`, `compute` maps which ceiling
   * binds — which is what keeps the tile ignorant of every one of them. It is
   * published as `data-tone`, always, including `neutral`: an attribute that
   * appears and disappears is one a test has to ask about twice.
   */
  tone?: SurfaceTone
  className?: string
  "data-test"?: string
}

/**
 * A figure with its mark and its unit, and nothing around it.
 *
 * The tile's middle line, pulled out because a screen sometimes wants the
 * reading without the box: a capacity card states "3 slots free" inside its own
 * header, beside the pool's name, and putting a whole `Surface` there to get
 * one number set correctly would be a card inside a card.
 */
export function StatFigure({
  value,
  prefix,
  suffix,
  tone = "neutral",
  className,
  "data-test": dataTest,
}: StatFigureProps) {
  return (
    <span
      className={cn(styles.figure, className)}
      data-tone={tone}
      data-test={dataTest}
    >
      {prefix ? <span className={styles.unit}>{prefix}</span> : null}
      <span className={styles.value}>{value}</span>
      {/* A real space in the markup before the unit, and none before the value.
          A currency mark belongs against its number (`$148.20`, never `$ 148`)
          and a unit belongs after a gap (`0 slots free`) — and that gap has to
          be a text node rather than the flex `gap`, because margins and gaps do
          not reach the accessibility tree or `textContent`. Without it a screen
          reader says "zeroslotsfree" and every assertion that reads the tile's
          text is reading a word the screen does not contain. It costs nothing
          in layout: a whitespace-only anonymous flex item is not rendered. */}
      {suffix ? (
        <>
          {" "}
          <span className={styles.unit}>{suffix}</span>
        </>
      ) : null}
    </span>
  )
}

export interface StatTileProps extends StatFigureProps {
  /** What the figure is — a data label, in the tight gesture. */
  label: string
  /**
   * Which reading this is, for anything traversing the DOM, published as
   * `data-stat` on the line that names it.
   *
   * A separate hook rather than an overridable `data-test`: "all the tiles on
   * this screen" and "the budget tile" are different questions, and an id that
   * replaces the component's own name can only answer the second. It rides on
   * the label because `Surface` forwards `data-test` and nothing else.
   */
  name?: string
  /**
   * The line under the figure — prose, in the interface voice. What the figure
   * means, or what it is out of.
   */
  sub?: ReactNode
  /**
   * The element underneath, handed to `Surface`. `article` for one reading in a
   * row of equal readings, `li` inside a list. A tile is chrome; the semantics
   * are the screen's to declare.
   */
  as?: SurfaceAs
  /**
   * Drawn between the figure and the line under it.
   *
   * The slot order is load-bearing, and it is the accessibility rule in
   * disguise: a meter is decoration on top of a reading already stated in
   * words, so it comes after the words. Nothing on a tile is announced only as
   * a length.
   */
  children?: ReactNode
}

/**
 * One reading: what it is, what it is, and what that means.
 *
 * Three lines over a `Surface` — the label, the figure, the sentence — and the
 * box is `Surface`'s job rather than this component's. That division is why
 * this is a primitive at all: the box had already been lifted into the kit, and
 * what stayed behind was the *contents*, arrived at independently by a cost
 * tile, a capacity card and a knowledge reading. The cost tile says so about
 * itself in a comment; the knowledge screen had not noticed, and had
 * re-declared `Surface`'s own five declarations on top of its copy.
 *
 * `tone` is one reading told twice — the lit edge on the `Surface` and the ink
 * on the figure — and never by hue alone: the line under the figure is where it
 * is said in words. Same two-channel rule a status band follows, and the reason
 * it reaches both elements from one prop instead of two a call site could set
 * against each other.
 *
 * There is deliberately no `scale`, no `subVoice` and no `bound` — the
 * stylesheet argues each one. A tile that can be drawn four ways is four tiles.
 */
export function StatTile({
  label,
  name,
  value,
  prefix,
  suffix,
  sub,
  tone = "neutral",
  as = "article",
  children,
  className,
  "data-test": dataTest = "stat-tile",
}: StatTileProps) {
  return (
    <Surface
      as={as}
      bound="start"
      tone={tone}
      className={className}
      data-test={dataTest}
    >
      <StatLabel data-stat={name}>{label}</StatLabel>
      <StatFigure value={value} prefix={prefix} suffix={suffix} tone={tone} />
      {children}
      {sub ? <p className={styles.sub}>{sub}</p> : null}
    </Surface>
  )
}
