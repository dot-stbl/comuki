import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import styles from "./section.module.css"

/**
 * Which of the two headings this section wears.
 *
 * `region` names a region of a screen: a mono micro-label in the wide gesture,
 * with an optional figure riding at the end of its own line. `screen` states
 * what a section of a page *is* before it can be read: a title in the interface
 * voice with a paragraph under it.
 *
 * There is no third, and neither of these two lets a call site choose a
 * tracking — see `section.module.css`.
 */
export type SectionVariant = "region" | "screen"

export interface SectionProps {
  title: ReactNode
  /**
   * The line that goes with the title.
   *
   * In a `region` it rides at the end of the heading line and is a value — a
   * count, a mix, a clock. In a `screen` it is the paragraph under the title
   * and is prose. The variant decides which, so the same prop cannot land in
   * the wrong voice.
   */
  note?: ReactNode
  variant?: SectionVariant
  /**
   * The heading's id, which the section is then labelled by.
   *
   * Only meaningful on `region`, where the heading is the section's whole name;
   * a `screen` heading is followed by a paragraph, and naming the region after
   * both would read the prose out as the label.
   */
  id?: string
  /** `h2` by default. `h3` for a section nested inside another one. */
  level?: 2 | 3
  className?: string
  "data-test"?: string
  children: ReactNode
}

/**
 * A titled region of a screen.
 *
 * Home, Compute, Models, Observability, Sources and the work-item inspector all
 * grew this shape privately while they were being built in parallel, and the
 * copies had already drifted apart on the two things that were never a choice:
 * which tracking a heading takes, and which voice its note is set in. This is
 * the one spelling, and it holds both of those fixed.
 *
 * It is deliberately not a card. The section is a heading and a stack; whatever
 * bounds the content below is the content's own business.
 */
export function Section({
  title,
  note,
  variant = "region",
  id,
  level = 2,
  className,
  "data-test": dataTest,
  children,
}: SectionProps) {
  const Heading = level === 3 ? "h3" : "h2"

  if (variant === "screen") {
    return (
      <section className={cn(styles.section, className)} data-test={dataTest}>
        <header className={styles.screenHead}>
          <Heading className={styles.screenTitle}>{title}</Heading>
          {note ? <p className={styles.screenNote}>{note}</p> : null}
        </header>
        {children}
      </section>
    )
  }

  return (
    <section
      className={cn(styles.section, className)}
      aria-labelledby={id}
      data-test={dataTest}
    >
      <Heading id={id} className={styles.regionHead}>
        {title}
        {note ? <span className={styles.regionNote}>{note}</span> : null}
      </Heading>
      {children}
    </section>
  )
}
