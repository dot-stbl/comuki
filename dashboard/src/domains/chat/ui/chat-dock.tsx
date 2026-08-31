import { useEffect, useState, type MouseEvent } from "react"
import { Link, useLocation } from "@tanstack/react-router"
import { MessageSquare, Wand2 } from "lucide-react"

import { useSearchCatalogue } from "@/app/search"
import { referenceFromLocation } from "@/domains/chat/model/references"
import { ChatConsole } from "@/domains/chat/ui/chat-console"
import { chatDockMemory } from "@/domains/chat/ui/chat-dock-memory"
import { isApple } from "@/shared/lib/is-apple"
import { can, useCan, useSession } from "@/shared/session"
import { BottomSheet, buttonClass, Tooltip } from "@/shared/ui"

import styles from "./chat-dock.module.css"

/** Where the dragged depth of the sheet lives, between sessions. */
const DEPTH_KEY = "comuki.chat.dock.depth"
/** Where the fill-the-window preference lives, between sessions. */
const EXPANDED_KEY = "comuki.chat.dock.expanded"

/** The console's chord, spelled the way the operator's keyboard says it. */
const CHORD = isApple() ? "⌘ j" : "ctrl j"

function readExpanded(): boolean {
  try {
    return window.localStorage.getItem(EXPANDED_KEY) === "true"
  } catch {
    return false
  }
}

/**
 * The console as a dock — a floating trigger, a modal bottom sheet, and one
 * rule that is not negotiable: the sheet renders `ChatConsole`, the same
 * component the `/chat` route renders. The two are two containers around one
 * console — one thread, one composer, one proposal card — because a state
 * change confirmed in either lands in the same journal, and the day two
 * implementations of the console disagreed, the operator would believe the
 * wrong one. Filling the window with the sheet lands on exactly the reading
 * the route shows, because it is the same tree.
 *
 * ## Why the trigger sits where it sits
 *
 * Bottom-right, one step above the bottom edge and one gutter in from the
 * right — flush with the content region's edge, above the table's horizontal
 * scrollbar. The right edge of the runs board is occupied top to bottom: the
 * toolbar's trailing controls at the top, the split separator at the split,
 * the scrollbar at the floor. Sitting on the scrollbar's end would take the
 * thumb's resting place; sitting one step up covers only the extreme corner
 * cell of a table — the least-read pixel on the board, and only when the
 * operator has scrolled the whole way down.
 *
 * While the sheet is open the trigger stays where it is, under the scrim:
 * dimmed and unreachable like the rest of the board, not a second way out
 * that the focus trap has to argue with. Escape and the bar's close control
 * are the two ways out, and both are deliberate.
 *
 * ## The scrim is paid for, not hidden
 *
 * A scrim means the board cannot be read while the console is open — the
 * owner took that trade knowingly. The compensation: opening the dock seeds
 * the composer with what the location says the operator was looking at (see
 * `referenceFromLocation`), so what the scrim covers arrives inside the sheet
 * with them. A suggestion, not a decision — the chip leaves in one gesture,
 * and a general question never had to start by deleting something.
 *
 * ## Leaving the sheet
 *
 * Escape closes, the close control closes, and clicking a hand-off (or any
 * link) inside the console closes it too: a hand-off's whole answer is a
 * *screen*, narrowed — keeping the sheet open over a navigation the operator
 * cannot see would be a sheet lying about where it is.
 */
export function ChatDock() {
  const session = useSession()
  const location = useLocation()
  const catalogue = useSearchCatalogue()

  const [open, setOpen] = useState(chatDockMemory.open)
  const [expanded, setExpanded] = useState(readExpanded)
  const [chosenId, setChosenId] = useState(chatDockMemory.chosenId)
  const [draft, setDraft] = useState(chatDockMemory.draft)
  const [seed, setSeed] = useState(chatDockMemory.seed)

  useEffect(() => {
    chatDockMemory.open = open
    chatDockMemory.chosenId = chosenId
    chatDockMemory.draft = draft
    chatDockMemory.seed = seed
  }, [open, chosenId, draft, seed])

  // Asked during render rather than after the null return, so the chord
  // listener below can refuse to exist for a session that may not use the
  // console — a viewer pressing ctrl j must not even set hidden state.
  const allowed = can(session, "chat.use")

  // The wizard is the one act that outgrew the sheet — creating an entity is
  // always its own page — so its link rides in the sheet's bar. It lived in
  // the `/chat` page header until the console stopped being a section, and an
  // entry point that moves must arrive in the container that replaced it.
  // Asked before the null return because a hook is a hook.
  const onboard = useCan("sources.edit")

  /* The console's chord, on the document the same way search's ctrl k is:
     the point of a global shortcut is that it works while the operator is
     three panels deep in a table, not once the trigger already has focus.
     The same chord closes — a toggle, because "open" and "get out of my
     way" are the two halves of one gesture, and j is the terminal-panel
     key the operator's hand already knows. Registered before the null
     return and gated by `allowed`, so the hook order is fixed while the
     chord still refuses to exist for a session that may not use it. */
  useEffect(() => {
    if (!allowed) {
      return
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() !== "j") {
        return
      }
      if (!(event.metaKey || event.ctrlKey) || event.altKey) {
        return
      }
      event.preventDefault()
      if (chatDockMemory.open) {
        setOpen(false)
        return
      }
      setSeed(
        referenceFromLocation(
          location.pathname,
          location.searchStr,
          catalogue,
          session
        )
      )
      setOpen(true)
    }

    document.addEventListener("keydown", onKeyDown)
    return () => document.removeEventListener("keydown", onKeyDown)
  }, [allowed, catalogue, location.pathname, location.searchStr, session])

  // Hidden rather than explained without `chat.use`, the way the rail hides
  // what a role cannot reach: this is a door to the console, and a door a
  // role cannot walk through is not drawn.
  if (!allowed) {
    return null
  }

  const openSheet = () => {
    setSeed(
      referenceFromLocation(
        location.pathname,
        location.searchStr,
        catalogue,
        session
      )
    )
    setOpen(true)
  }

  const onExpandedChange = (next: boolean) => {
    setExpanded(next)
    try {
      window.localStorage.setItem(EXPANDED_KEY, String(next))
    } catch {
      // Storage is unavailable; the session keeps working, it just forgets.
    }
  }

  // A link pressed inside the console is a navigation — a hand-off, a
  // reference, the wizard. The sheet gets out of the destination's way rather
  // than navigating underneath a scrim the operator cannot see through.
  const onConsoleClick = (event: MouseEvent<HTMLDivElement>) => {
    const anchor = (event.target as HTMLElement).closest("a")
    if (anchor?.getAttribute("href")?.startsWith("/") === true) {
      setOpen(false)
    }
  }

  return (
    <>
      <Tooltip content={`Console — ${CHORD}`}>
        <button
          type="button"
          className={styles.trigger}
          data-test="chat-dock-trigger"
          aria-label={`Open the console — ${CHORD}`}
          aria-expanded={open}
          onClick={openSheet}
        >
          <MessageSquare aria-hidden="true" />
        </button>
      </Tooltip>

      <BottomSheet
        open={open}
        onOpenChange={setOpen}
        title="Console"
        toolbar={
          onboard.allowed ? (
            <Link
              to="/chat/init"
              data-test="chat-init"
              className={buttonClass({ variant: "outline", size: "sm" })}
              /* The bar is outside the console's link watcher, so the sheet
                 is closed here rather than left hanging over a page it hid. */
              onClick={() => setOpen(false)}
            >
              <Wand2 aria-hidden="true" />
              Onboard a repo
            </Link>
          ) : null
        }
        storageKey={DEPTH_KEY}
        expanded={expanded}
        onExpandedChange={onExpandedChange}
      >
        <div className={styles.console} onClickCapture={onConsoleClick}>
          {/* Autofocus on: the sheet mounts fresh on every open, and a
              console that opens without the box ready to type in charges a
              click for the first word. The route declines it — a page that
              steals focus on load is presumptuous. */}
          <ChatConsole
            chosenId={chosenId}
            onChosenIdChange={setChosenId}
            draft={draft}
            onDraftChange={setDraft}
            seed={seed}
            onSeedChange={setSeed}
            focusComposerOnMount
          />
        </div>
      </BottomSheet>
    </>
  )
}
