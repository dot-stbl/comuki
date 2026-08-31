import { useCallback, useEffect, useMemo, useState } from "react"
import { Search } from "lucide-react"
import { useNavigate } from "@tanstack/react-router"

import { useSession } from "@/shared/session"
import { Button, Tooltip } from "@/shared/ui"

import { useSearchCatalogue } from "./catalogue"
import { CommandPalette } from "./command-palette"
import { resolveQuery, type SearchItem } from "./resolve"
import { isApple } from "@/shared/lib/is-apple"

/**
 * Global search, wired to the product.
 *
 * This is the policy half — it knows the product's identifiers, its rail, its
 * permissions and its router, so it lives in `app/` beside the shell rather
 * than in the kit. `CommandPalette` next door is the view, and knows none of
 * those things; the seam between them is a `SearchItem[]`.
 *
 * The palette owns no state that outlives it: the query resets on close, so
 * reopening is always a fresh question rather than the last one half-answered.
 */
export function GlobalSearch() {
  const session = useSession()
  const catalogue = useSearchCatalogue()
  const navigate = useNavigate()

  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState("")

  const changeOpen = useCallback((next: boolean) => {
    setOpen(next)
    if (!next) {
      setQuery("")
    }
  }, [])

  /* The chord, on the document rather than on the bar: the point of a global
     shortcut is that it works while the operator is looking at a table three
     panels away, and a handler bound to the control would only fire once the
     control already had focus. `preventDefault` because ctrl+k is the address
     bar's own gesture in more than one browser. */
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() !== "k") {
        return
      }
      if (!(event.metaKey || event.ctrlKey) || event.altKey) {
        return
      }
      event.preventDefault()
      setOpen((current) => {
        if (current) {
          setQuery("")
        }
        return !current
      })
    }

    document.addEventListener("keydown", onKeyDown)
    return () => document.removeEventListener("keydown", onKeyDown)
  }, [])

  const items = useMemo(
    () => resolveQuery(query, { session, catalogue }),
    [query, session, catalogue]
  )

  const onSelect = useCallback(
    (item: SearchItem) => {
      changeOpen(false)
      /* `href` rather than `to`: the resolver builds its destinations from a
         shape catalogue, so they are strings by construction and the router's
         generated union cannot know them. `href` is the router's own escape
         hatch for a fully built path and still navigates on the client — and
         it is the same string `GET /resolve?q=` will one day return, so the
         day this becomes a server call nothing here changes. */
      void navigate({ href: item.href })
    },
    [changeOpen, navigate]
  )

  const chord = isApple() ? "⌘ k" : "ctrl k"

  return (
    <>
      <Tooltip content={`Search — ${chord}`}>
        <Button
          variant="ghost"
          size="icon"
          data-test="global-search"
          aria-label={`Search — ${chord}`}
          onClick={() => {
            changeOpen(true)
          }}
        >
          <Search aria-hidden="true" />
        </Button>
      </Tooltip>

      <CommandPalette
        open={open}
        onOpenChange={changeOpen}
        query={query}
        onQueryChange={setQuery}
        items={items}
        onSelect={onSelect}
      />
    </>
  )
}
