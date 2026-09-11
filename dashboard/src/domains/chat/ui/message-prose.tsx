import { lazy, Suspense } from "react"

import { MessageText } from "./message-text"

export interface MessageProseProps {
  markdown: string
  /**
   * A reply still being written. It renders as plain text and never reaches
   * the parser — see below.
   */
  streaming?: boolean
}

/**
 * The markdown renderer, loaded only when there is markdown to render.
 *
 * `react-markdown`, its unified/remark pipeline and `highlight.js` are roughly
 * the weight of the rest of the console put together, and the console is
 * mounted on *every* screen: the dock's trigger lives in the app shell. So the
 * parser is behind a dynamic import, and the fallback is the plain paragraph
 * the thread rendered before this change — identical text, identical
 * references, no layout shift when the real renderer arrives.
 *
 * ## A reply in flight is not parsed
 *
 * Deliberately, and not as an optimisation. Half a markdown document is a
 * *different* document: an unclosed fence swallows the rest of the message, an
 * unfinished table has no delimiter row, and a partial list item reflows the
 * thread several times a second while the operator is reading it. So tokens
 * arrive as plain text with their newlines kept, and the parse happens once,
 * when the reply settles — which is the same moment the thread moves it into
 * the log and announces it.
 */
const Markdown = lazy(() => import("./message-markdown"))

export function MessageProse({ markdown, streaming }: MessageProseProps) {
  if (streaming) {
    return <MessageText text={markdown} />
  }

  return (
    <Suspense fallback={<MessageText text={markdown} />}>
      <Markdown markdown={markdown} />
    </Suspense>
  )
}
