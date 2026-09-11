import { Children, type ReactNode } from "react"
import Markdown, { type Components } from "react-markdown"
import remarkGfm from "remark-gfm"
import type { Element, ElementContent } from "hast"

import { CodeBlock } from "@/shared/ui"

import { ReferencedText } from "./message-text"

import styles from "./chat-message.module.css"

export interface MessageMarkdownProps {
  markdown: string
}

/**
 * Prose, rendered as what the backend has always said it was.
 *
 * `ChatMessage.cs` has claimed markdown since it was written and the console
 * rendered a `<p>` with `white-space: pre-wrap` — so every list arrived as
 * hyphens, every table as pipes, and every fenced block as four spaces of
 * indentation the operator had to parse themselves. This is the missing half.
 *
 * ## Three decisions, all of them narrowing
 *
 * **No `rehype-raw`, and this is not a configuration gap.** Raw HTML never
 * enters the thread. Everything on this surface is therefore something the
 * markdown grammar produced, which means there is no element an operator's
 * paste or a model's output can introduce — no `<script>`, no `<iframe>`, no
 * `<img onerror>`, no attribute at all that this file did not write. The XSS
 * question is not mitigated here; it is absent, because the input can only
 * become a fixed set of elements. A model that wants a table writes a table;
 * a model that writes `<div>` gets the four characters it typed.
 *
 * **The component map is the kit, not a stylesheet.** A fenced block is the
 * kit's `CodeBlock` — the same primitive a screen would use, with the same
 * copy control and the same highlighting — rather than a `<pre>` this file
 * styles a second way. Everything else lands on the thread's own sheet.
 *
 * **Headings shift down rather than through.** A turn is an entry in a log,
 * not a document: an `#` from a model must not become the page's `<h1>` and
 * push the screen's own title out of the outline. They land at `h3`–`h6`,
 * below the thread's own `h2`, so the outline stays readable and a screen
 * reader's heading list stays navigable.
 *
 * ## Identifiers keep resolving
 *
 * Every text-bearing element runs its own string children through
 * `ReferencedText`, so a run id inside a list item or a table cell is still
 * the link it is inside a paragraph. Inline code is skipped on purpose — see
 * that component's note.
 */
export default function MessageMarkdown({ markdown }: MessageMarkdownProps) {
  return (
    <div className={styles.markdown} data-test="chat-markdown">
      <Markdown remarkPlugins={[remarkGfm]} components={COMPONENTS}>
        {markdown}
      </Markdown>
    </div>
  )
}

/**
 * String children, with the identifiers in them turned into links.
 *
 * Only *direct* strings: an element child has already been through the map
 * and resolved whatever it was going to resolve, and walking into it would
 * either re-tokenize prose twice or start linking the inside of a code span.
 */
function referenced(children: ReactNode): ReactNode {
  return Children.map(children, (child) =>
    typeof child === "string" ? <ReferencedText text={child} /> : child
  )
}

/** The fenced block inside a `<pre>`, as the parser saw it. */
function fenceOf(node?: Element): { source: string; language?: string } | null {
  const code = node?.children.find(
    (child: ElementContent): child is Element =>
      child.type === "element" && child.tagName === "code"
  )
  if (!code) {
    return null
  }
  const source = code.children
    .map((child) => (child.type === "text" ? child.value : ""))
    .join("")
  const classes = code.properties?.className
  const language = Array.isArray(classes)
    ? classes
        .map(String)
        .find((name) => name.startsWith("language-"))
        ?.slice("language-".length)
    : undefined
  return { source, language }
}

const COMPONENTS: Components = {
  p: ({ children }) => <p className={styles.mdParagraph}>{referenced(children)}</p>,

  /* h1–h6 shift down so the deepest a turn can reach is h6 and the shallowest
     sits under the thread's own h2. Three levels is more outline than any
     message in a console needs. */
  h1: ({ children }) => <h3 className={styles.mdHeading}>{referenced(children)}</h3>,
  h2: ({ children }) => <h4 className={styles.mdHeading}>{referenced(children)}</h4>,
  h3: ({ children }) => <h5 className={styles.mdHeading}>{referenced(children)}</h5>,
  h4: ({ children }) => <h6 className={styles.mdHeading}>{referenced(children)}</h6>,
  h5: ({ children }) => <h6 className={styles.mdHeading}>{referenced(children)}</h6>,
  h6: ({ children }) => <h6 className={styles.mdHeading}>{referenced(children)}</h6>,

  ul: ({ children }) => <ul className={styles.mdList}>{children}</ul>,
  ol: ({ children }) => <ol className={styles.mdList}>{children}</ol>,
  li: ({ children }) => <li className={styles.mdItem}>{referenced(children)}</li>,

  strong: ({ children }) => (
    <strong className={styles.mdStrong}>{referenced(children)}</strong>
  ),
  em: ({ children }) => <em>{referenced(children)}</em>,
  del: ({ children }) => <del className={styles.mdDeleted}>{referenced(children)}</del>,

  blockquote: ({ children }) => (
    <blockquote className={styles.mdQuote}>{children}</blockquote>
  ),
  hr: () => <hr className={styles.mdRule} />,

  /* A table in a message is a reading, so it scrolls rather than crushing its
     columns — the Shape-Not-Reading Rule, and the one place on this surface
     allowed to be wider than the measure. It is not the kit's `DataTable`:
     that is a virtualized surface a screen owns, and four rows of markdown
     asking for sorting, filtering and pinning would be a second duty table
     drawn by a model. */
  table: ({ children }) => (
    <div className={styles.mdTableScroll}>
      <table className={styles.mdTable}>{children}</table>
    </div>
  ),
  th: ({ children }) => <th className={styles.mdTh}>{referenced(children)}</th>,
  td: ({ children }) => <td className={styles.mdTd}>{referenced(children)}</td>,

  /* A task list's checkbox is a *reading* of what the turn said, not a control
     the operator can act on — nothing here writes anywhere. So it renders
     read-only and says so, rather than offering a press that does nothing. */
  input: ({ type, checked }) =>
    type === "checkbox" ? (
      <input
        type="checkbox"
        className={styles.mdCheck}
        checked={Boolean(checked)}
        readOnly
        aria-disabled="true"
        tabIndex={-1}
      />
    ) : null,

  a: ({ href, children }) => {
    const external = /^https?:/i.test(href ?? "")
    return (
      <a
        className={styles.mdLink}
        href={href}
        data-test="chat-markdown-link"
        {...(external
          ? { target: "_blank", rel: "noreferrer noopener" }
          : undefined)}
      >
        {referenced(children)}
      </a>
    )
  },

  /* An image is a request this console does not make. A remote fetch from a
     thread is a beacon that says which operator read which message and when,
     and the markdown here comes from a model. The alt text and the address are
     rendered instead — everything the operator needs to decide to open it. */
  img: ({ src, alt }) => (
    <a
      className={styles.mdLink}
      href={typeof src === "string" ? src : undefined}
      target="_blank"
      rel="noreferrer noopener"
      data-test="chat-markdown-image"
    >
      {alt || "image"}
    </a>
  ),

  /* The fence. `pre` is the marker rather than `code`, because react-markdown
     v10 no longer tells a component whether it is inline — and the parser
     already knows: a block is the only code that has a `<pre>` around it. */
  pre: ({ node, children }) => {
    const fence = fenceOf(node)
    return fence ? (
      <CodeBlock
        className={styles.mdCodeBlock}
        source={fence.source}
        language={fence.language}
      />
    ) : (
      <pre className={styles.mdPre}>{children}</pre>
    )
  },

  code: ({ children }) => <code className={styles.mdCodeInline}>{children}</code>,
}
