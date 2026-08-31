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

/**
 * A message, with the identifiers in it turned back into the things they name.
 *
 * The shapes come from the product's own catalogue — see
 * `model/references.ts` for why only the keyed tier is allowed in prose, and
 * why a reference this session cannot open renders as text rather than as a
 * link into a forbidden state.
 *
 * The identifier is set in the data voice inside a sentence set in the
 * interface voice, which is the Two Voices Rule applied at word scale: the
 * sentence is meaning, the run id is a value, and a mono run id in an Archivo
 * paragraph is exactly how an operator finds it again with their eye.
 */
export function MessageText({ text, className }: MessageTextProps) {
  const session = useSession()
  const catalogue = useSearchCatalogue()

  const tokens = useMemo(
    () => tokenizeReferences(text, catalogue, session),
    [text, catalogue, session]
  )

  return (
    <p className={cn(styles.text, className)}>
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
    </p>
  )
}
