# domains/chat

## Purpose

The **console** — a full agent pult, not an assistant bolted to the side. It is
the same control plane the screens drive, reached by typing. Its tools are the
Orchestration API and the lead model.

## Routes

- `/chat` — the console (`src/routes/chat/index.tsx`)
- `/chat/init?step=` — the onboarding wizard (`src/routes/chat/init.tsx`)

## Containers

The console has exactly **one implementation and two containers**:

- `/chat` — the screen, with a URL that can be linked and bookmarked.
- **The dock** (`ui/chat-dock.tsx`) — a floating trigger over the board
  opening a modal bottom sheet (`shared/ui`'s `BottomSheet`), resizable by its
  top edge, able to fill the window.

Both render the same `ChatConsole` (`ui/chat-console.tsx`). Not a copy, not a
trimmed variant: one thread, one composer, one proposal card, one set of
tool-call records. The rule is the same one that forbids a second duty screen —
a state change confirmed in either container lands in the same journal, and the
day two implementations disagreed, the operator would believe the wrong one.
A test asserts it structurally (same component, same class) — `ui/chat-dock.test.tsx`.

The dock holds its state outside the component tree (`memory` in
`ui/chat-dock.tsx`): every screen mounts its own shell, so a navigation is a
remount, and an open sheet, an open conversation and a half-typed draft are
the operator's, not the box's. The dock closes itself when a link inside the
console is pressed — a hand-off's answer is a *screen*, and navigating under a
scrim would be the sheet lying about where it is.

The scrim is a deliberate trade the owner took knowingly: the board is not
readable while the sheet is open. The compensation is the **seeded reference**:
opening the dock reads what the location says the operator was looking at
(`model/references.ts` → `referenceFromLocation`) and offers it as a chip in
the composer — a suggestion, removable in one gesture, whose id rides along
with the next message as text.

## Three settled decisions

1. **It proposes; a human confirms.** Every state change is a `Proposal`
   (`model/types.ts`) with two controls that keep their words. There is no path
   from rendering a proposal to acting on one — see `ui/proposal-card.tsx`.
2. **Its acts land in the same journal.** Confirming writes through
   `shared/api/mock/chat.store.ts` into the *run* store, and the runs query is
   invalidated. A run stopped from the console is a run stopped.
3. **It hands off rather than rendering a second product.** `ChatHandoffs` asks
   `resolveQuery` from `app/search` for the destinations, so the console and the
   command palette cannot disagree.

## Public exports

`ChatPage`, `InitWizardPage`, `ChatDock` via `@/domains/chat`.

## Invariants

- Chat tools check the **same permissions as REST**, resolved per project with
  `can(session, permission, projectId)`. `model/proposals.ts` is the only place
  the mapping from act to permission lives.
- A refusal renders the control present, `aria-disabled`, with the reason on
  `data-denied`, and swallows the click. Never `disabled` for a permission.
- Identifier shapes come from `app/search/shapes.ts`. Only the **keyed** tier is
  allowed inside prose — see `model/references.ts`. The dock's seed reads the
  same shapes out of the location, and a seed the session cannot follow is
  offered never, for the same reason prose renders it as plain text.
- No language model. Replies are scripted in `shared/api/mock/chat.seed.ts`.
- The streaming reply is rendered **outside** the `role="log"` region and is
  `aria-hidden` while it arrives. See `ui/chat-thread.tsx`.
- The dock is hidden — not explained — without `chat.use`, the way the rail
  hides what a role cannot reach. The console is **not a rail section**: the
  floating trigger is its one door in the chrome, decided by the owner. The
  `/chat` route stays as the URL-addressable reading (and the wizard's
  neighbour), linked from nothing; the wizard's entry point rides in the
  sheet's bar, because an entry point must live in the container that is
  reachable.
