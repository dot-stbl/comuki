import { useEffect, useMemo, useState, type CSSProperties } from "react"
import { ChevronDown, ChevronUp, WrapText } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import { Button } from "../button"
import { CopyButton } from "../form"
import {
  highlightCode,
  PLAIN_LANGUAGE_LABEL,
  resolveLanguage,
} from "./highlight"

import styles from "./code-block.module.css"

export interface CodeBlockProps {
  /** The code itself. A trailing newline is dropped — a fence always has one. */
  source: string
  /** The fence's language as it was written. Unknown spellings read as text. */
  language?: string
  /** Where it came from. Rendered with `startLine` as `path:line`. */
  path?: string
  startLine?: number
  /** Longer than this and the block starts collapsed. Default 24. */
  collapseAfter?: number
  /** Start with wrapping on. Off by default — see the Shape-Not-Reading Rule. */
  wrap?: boolean
  className?: string
  "data-test"?: string
}

/**
 * A block of code, as a reading surface.
 *
 * The console renders what the conversation produced, and what a coding agent
 * produces most of is code. A patch pasted into a `<p>` with `pre-wrap` is not
 * a patch: indentation survives and nothing else does — no language, no way to
 * take it out of the page, no way to tell nine lines of context from the two
 * that changed.
 *
 * ## The four controls, and why each one is there
 *
 * - **The language chip** says which grammar is being applied, so a block that
 *   comes out unstyled is legibly *unknown* rather than apparently broken.
 * - **Copy** is the reason most code blocks are looked at at all. It is the
 *   kit's `CopyButton`, so it confirms the way every other copy in the product
 *   confirms.
 * - **Wrap** is off by default: long lines scroll sideways, because the
 *   Shape-Not-Reading Rule says a component that runs out of room scrolls to
 *   keep its reading rather than crushing it. Wrapping a shell command
 *   silently changes what it looks like, so turning it on is the operator's
 *   decision and it is one press.
 * - **Collapse** past 24 lines, because a 300-line file pasted into a thread
 *   buries the sentence that explained it. The control says how many lines are
 *   behind it — a clipped box with no count is a box nobody expands.
 *
 * ## The highlighting is classes, never colour
 *
 * `highlight.ts` explains the choice. The short of it: highlight.js emits
 * `hljs-*` classes and the stylesheet beside this file maps them onto the
 * theme's own tokens, so a new palette highlights code without this component
 * knowing it exists. Nothing renders until the grammar has loaded, and until
 * then — and forever, for a language outside the registered nine — the source
 * is on screen as plain text. There is no state in which the code is missing.
 */
export function CodeBlock({
  source,
  language,
  path,
  startLine,
  collapseAfter = COLLAPSE_AFTER,
  wrap = false,
  className,
  "data-test": dataTest = "code-block",
}: CodeBlockProps) {
  const body = useMemo(() => source.replace(/\n+$/, ""), [source])
  const resolved = useMemo(() => resolveLanguage(language), [language])
  const lines = useMemo(() => body.split("\n").length, [body])

  const [markup, setMarkup] = useState<string | null>(null)
  const [wrapping, setWrapping] = useState(wrap)
  const [expanded, setExpanded] = useState(false)

  useEffect(() => {
    if (!resolved) {
      setMarkup(null)
      return
    }
    let live = true
    void highlightCode(body, resolved)
      .then((html) => {
        if (live) {
          setMarkup(html)
        }
      })
      .catch(() => {
        // A grammar that failed to load is a block that reads as plain text.
        // There is nothing to tell the operator: the code is already on
        // screen, and a banner about a missing highlighter would be the only
        // thing on this surface that is about the dashboard rather than about
        // the code.
        if (live) {
          setMarkup(null)
        }
      })
    return () => {
      live = false
    }
  }, [body, resolved])

  const collapsible = lines > collapseAfter
  const collapsed = collapsible && !expanded
  const origin = originOf(path, startLine)

  return (
    <div
      className={cn(styles.block, className)}
      data-test={dataTest}
      data-language={resolved ?? PLAIN_LANGUAGE_LABEL}
      data-collapsed={collapsed || undefined}
      data-wrap={wrapping || undefined}
      /* The fold is a count of lines, and the count is the component's — the
         same contract the data table's `--dt-row-h` keeps, so the painted
         height and the number that decided it are one value. */
      style={{ "--code-lines": collapseAfter } as CSSProperties}
    >
      <div className={styles.head}>
        <span className={styles.chip} data-test="code-block-language">
          {resolved ?? PLAIN_LANGUAGE_LABEL}
        </span>
        {origin ? (
          <span className={styles.origin} data-test="code-block-origin">
            {origin}
          </span>
        ) : null}
        <div className={styles.actions}>
          <Button
            variant="ghost"
            size="sm"
            data-test="code-block-wrap"
            aria-pressed={wrapping}
            onClick={() => setWrapping((on) => !on)}
          >
            <WrapText aria-hidden="true" />
            <span>wrap</span>
          </Button>
          <CopyButton value={body} data-test="code-block-copy" />
        </div>
      </div>

      <pre className={styles.pre} data-test="code-block-body" tabIndex={0}>
        {/* The class is on the `<code>` so `hljs-*` descendants resolve
            against one ancestor whether the markup arrived or not. */}
        <code className={styles.code}>
          {markup === null ? (
            body
          ) : (
            /* Safe by construction — see `highlightCode`: the source is
               HTML-escaped before a single span is written, so the only
               markup here is the highlighter's own. */
            <span dangerouslySetInnerHTML={{ __html: markup }} />
          )}
        </code>
      </pre>

      {collapsible ? (
        <Button
          variant="ghost"
          size="sm"
          className={styles.more}
          data-test="code-block-expand"
          aria-expanded={expanded}
          onClick={() => setExpanded((open) => !open)}
        >
          {expanded ? (
            <ChevronUp aria-hidden="true" />
          ) : (
            <ChevronDown aria-hidden="true" />
          )}
          <span>{expanded ? "show less" : `show all ${lines} lines`}</span>
        </Button>
      ) : null}
    </div>
  )
}

/**
 * How many lines before the block folds itself.
 *
 * Twenty-four is a screenful of code at this type step and the point at which
 * a block stops being something read in passing. Below it, folding costs a
 * press and saves nothing.
 */
const COLLAPSE_AFTER = 24

/** `path:line`, or just the path, or nothing. Values, so the data voice. */
function originOf(path?: string, startLine?: number): string | null {
  if (!path) {
    return null
  }
  return startLine === undefined ? path : `${path}:${startLine}`
}
