import { Fragment, useMemo } from "react"
import { Link } from "@tanstack/react-router"

import { useSearchCatalogue } from "@/app/search"
import { tokenizeReferences } from "@/domains/chat/model/references"
import { cn } from "@/shared/lib/utils"
import { useSession } from "@/shared/session"

import styles from "./chat-message.module.css"

export interface MessageTextProps {
  text: string
  className?: string
}

export interface ReferencedTextProps {
  text: string
}

/**
 * A run of prose with the identifiers in it turned back into the things they
 * name — the piece, without a paragraph around it.
 *
 * Extracted because the markdown renderer needs exactly this and nothing else:
 * a heading, a list item, a table cell and a blockquote all carry prose, and
 * an identifier has to keep resolving in every one of them. Inline code is the
 * deliberate exception — a `runs.get` inside backticks is a *quotation* of an
 * identifier, not a reference to one, and linking it would make the console
 * offer to navigate to something the operator was only naming.
 *
 * The shapes come from the product's own catalogue — see `model/references.ts`
 * for why only the keyed tier is allowed in prose, and why a reference this
 * session cannot open renders as text rather than as a link into a forbidden
 * state.
 */
export function ReferencedText({ text }: ReferencedTextProps) {
  const session = useSession()
  const catalogue = useSearchCatalogue()

  const tokens = useMemo(
    () => tokenizeReferences(text, catalogue, session),
    [text, catalogue, session]
  )

  return (
    <>
      {tokens.map((token, index) =>
        token.target ? (
          <Link
            key={index}
            to={token.target.href}
            className={styles.reference}
            data-test="chat-reference"
            data-kind={token.target.kind}
            title={`${token.target.kind}${
              token.target.hint ? ` — ${token.target.hint}` : ""
            }`}
          >
            {token.text}
          </Link>
        ) : (
          <Fragment key={index}>{token.text}</Fragment>
        )
      )}
    </>
  )
}

/**
 * A message as one plain paragraph.
 *
 * The markdown renderer is behind a dynamic import — it and the highlighter
 * are the console's own weight and have no business in the first paint of a
 * board that may never open the dock — so this is what stands in the gap while
 * that chunk arrives, and what the streaming reply uses outright: a reply
 * still being written is half a markdown document, and half a markdown
 * document re-parsed several times a second reflows the thread under the
 * operator's eye. `pre-wrap`, so the line breaks the model actually wrote
 * survive until the parser can say what they meant.
 *
 * The identifier is set in the data voice inside a sentence set in the
 * interface voice, which is the Two Voices Rule applied at word scale: the
 * sentence is meaning, the run id is a value, and a mono run id in an Archivo
 * paragraph is exactly how an operator finds it again with their eye.
 */
export function MessageText({ text, className }: MessageTextProps) {
  return (
    <p className={cn(styles.text, className)}>
      <ReferencedText text={text} />
    </p>
  )
}
