import { useState } from "react"
import { Link } from "@tanstack/react-router"
import { Wand2 } from "lucide-react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader } from "@/app/layout/page-header"
import { ChatConsole } from "@/domains/chat/ui/chat-console"
import { useCan } from "@/shared/session"
import { buttonClass } from "@/shared/ui"

/**
 * The console, as a screen.
 *
 * One of the console's two containers. The whole console — the thread, the
 * composer, the proposals — lives in `ChatConsole`, and this page is the
 * container with a URL: a destination that can be linked from a ticket,
 * bookmarked, and found in the rail. The dock's bottom sheet is the other
 * container, and the rule between them is that they render the *same*
 * component; a conversation, a draft and a decision are the same thing
 * wherever they are shown, and two implementations of either would be two
 * paths into one journal.
 *
 * The state a container holds is exactly the state that must outlive the
 * box: which conversation is open, and the message being typed. Everything
 * else is the console's own or the store's.
 */
export function ChatPage() {
  const [chosenId, setChosenId] = useState<string | null>(null)
  const [draft, setDraft] = useState("")

  // The wizard is a screen, not a modal — creating an entity is always its own
  // page — so the header links to it rather than opening one. Refused rather
  // than hidden, because it is an *act* on this screen even though it lands on
  // another: the operator who cannot run it should learn what to ask for.
  const onboard = useCan("sources.edit")

  return (
    <AppShell
      padded={false}
      header={
        <PageHeader
          breadcrumbs={[{ label: "console" }]}
          title="Console"
          summary="the same control plane the screens drive, typed at instead of clicked"
          actions={
            onboard.allowed ? (
              <Link
                to="/chat/init"
                data-test="chat-init"
                className={buttonClass({ variant: "outline", size: "sm" })}
              >
                <Wand2 aria-hidden="true" />
                Onboard a repo
              </Link>
            ) : null
          }
        />
      }
    >
      <ChatConsole
        chosenId={chosenId}
        onChosenIdChange={setChosenId}
        draft={draft}
        onDraftChange={setDraft}
      />
    </AppShell>
  )
}
