import type { ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

import styles from "./screen-state.module.css"

/**
 * Which of the four §17 states this is.
 *
 * One union rather than four components, because the four differ in exactly
 * two things — the words, which the call site writes, and whether a screen
 * reader is interrupted, which follows from the kind. Everything else about
 * them has to be identical or they stop being interchangeable, and four
 * components that must not drift from each other is the arrangement that
 * produced the twenty-two hand-copied `.state` blocks this replaces.
 *
 * - `error` — the request failed. The only kind that takes `role="alert"`:
 *   something the operator was looking at is not there, and they are told
 *   without having to find the sentence themselves.
 * - `empty` — the request answered and the answer is nothing. Ordinary, often
 *   correct, never an alarm.
 * - `notFound` — an id resolved to nothing. A stale tab or an old link is the
 *   usual way to arrive, so it reads like `empty` and not like `error`.
 * - `forbidden` — the roles this session holds do not open it. Written for you
 *   by {@link ForbiddenState}; reach for that rather than this kind directly.
 */
export type ScreenStateKind = "empty" | "error" | "notFound" | "forbidden"

/**
 * How much inline room the state pays for itself.
 *
 * The vertical room never changes. This is the one axis the twenty-two copies
 * actually varied on, so it is the one prop that exists.
 *
 * - `flush` (default) — no inline padding: the state lines up with the start
 *   edge of the content column it stands in.
 * - `gutter` — `--s6`, for a screen body that is not already inset.
 * - `page` — `--page-x`, for a state that spans the page.
 * - `none` — no padding at all, for a state nested inside a section that has
 *   already paid for its own room.
 */
export type ScreenStateInset = "flush" | "gutter" | "page" | "none"

export interface ScreenStateProps {
  kind: ScreenStateKind
  /** One line naming what happened, in the product's own words. */
  title: ReactNode
  /**
   * The sentence under it — measured at 52ch, because it is read rather than
   * scanned. Takes marked-up children: an id in the data voice, a `<code>`,
   * a link back to the list.
   */
  description?: ReactNode
  /**
   * A reading under the prose: a status, a count, the id that did not resolve.
   * Evidence for the sentence, in the data voice.
   */
  hint?: ReactNode
  /**
   * What the state hands back — a retry button, a way out, a link. Laid out in
   * a wrapping row at the start edge.
   */
  action?: ReactNode
  inset?: ScreenStateInset
  /**
   * Anything the four props above cannot say. Rendered between the prose and
   * the actions; use {@link StateText} for further measured paragraphs so the
   * measure and the ink stay the state's and not the screen's.
   */
  children?: ReactNode
  className?: string
  "data-test"?: string
}

const INSET = {
  flush: styles.flush,
  gutter: styles.gutter,
  page: styles.page,
  none: undefined,
} as const

/**
 * The state a screen is in when it has nothing to show yet.
 *
 * §17 names four — Empty, Loading, Error, Forbidden — and for a long time the
 * kit carried one of them. The other three were copied by hand into twenty-two
 * domain stylesheets as `.state` / `.stateTitle` / `.stateBody`, byte-identical
 * in most of them and quietly divergent in the rest: two measures (52ch and
 * 62ch), two spellings of the same measure (`max-width` and `max-inline-size`)
 * and two names for the same ink (`--text` and `--foreground`). This is the one
 * spelling, and the drift is not reachable from it.
 *
 * Loading is the one state that is not here, because it is not this shape: it
 * has no title and no sentence, it is the shape of the thing that has not
 * arrived. It is {@link Skeleton}, and it renders as a sibling of this.
 *
 * ```tsx
 * <ScreenState
 *   kind="error"
 *   title="The backlog did not load"
 *   description={requestFailureMessage(error, "Unknown error")}
 *   action={
 *     <Tooltip content="Retry">
 *       <Button size="icon-sm" aria-label="Retry" onClick={() => void refetch()}>
 *         <RotateCw aria-hidden="true" />
 *       </Button>
 *     </Tooltip>
 *   }
 * />
 * ```
 */
export function ScreenState({
  kind,
  title,
  description,
  hint,
  action,
  inset = "flush",
  children,
  className,
  "data-test": dataTest,
}: ScreenStateProps) {
  return (
    <div
      className={cn(styles.state, INSET[inset], className)}
      /* Only the failure interrupts. An empty list, a missing id and a closed
         view are all ordinary arrivals, and a screen that shouted every one of
         them would teach the operator to stop listening to the one that
         matters. */
      role={kind === "error" ? "alert" : undefined}
      data-state={kind}
      data-test={dataTest}
    >
      <p className={styles.title}>{title}</p>
      {description ? <p className={styles.body}>{description}</p> : null}
      {children}
      {hint ? <p className={styles.hint}>{hint}</p> : null}
      {action ? <div className={styles.actions}>{action}</div> : null}
    </div>
  )
}

export interface StateTextProps {
  children: ReactNode
  className?: string
}

/**
 * One more measured paragraph inside a {@link ScreenState}.
 *
 * Most states are a title and a line. A few genuinely carry two or three — a
 * container that was torn down while the page was open has to say what went
 * with it and what did not — and the alternative to this is every one of those
 * screens re-declaring the 52ch measure and the muted ink in its own
 * stylesheet, which is how the twenty-two copies happened the first time.
 */
export function StateText({ children, className }: StateTextProps) {
  return <p className={cn(styles.body, className)}>{children}</p>
}
