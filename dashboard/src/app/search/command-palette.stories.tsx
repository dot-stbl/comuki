import type { Meta, StoryObj } from "@storybook/react"

import { CommandPalette } from "./command-palette"
import type { SearchItem } from "./resolve"

/* The palette takes rows and hands back the one that was chosen, so every
   state it can be in is a prop away. The four below are the four the design
   turns on: resolving an identifier, disambiguating between two, handing free
   text off, and having nothing to say. */

const SECTIONS: SearchItem[] = [
  {
    id: "section:/runs",
    group: "section",
    kind: "section",
    label: "Live runs",
    value: false,
    hint: "observe",
    href: "/runs",
  },
  {
    id: "section:/queue",
    group: "section",
    kind: "section",
    label: "Queue",
    value: false,
    hint: "observe",
    href: "/queue",
  },
  {
    id: "section:/approvals",
    group: "section",
    kind: "section",
    label: "Approvals",
    value: false,
    hint: "observe",
    href: "/approvals",
  },
  {
    id: "act:/tasks",
    group: "act",
    kind: "act",
    label: "New run",
    value: false,
    hint: "take a ticket from the inbox",
    href: "/tasks",
  },
  {
    id: "act:/projects/new",
    group: "act",
    kind: "act",
    label: "New project",
    value: false,
    hint: "register a project",
    href: "/projects/new",
  },
]

function handoffs(query: string): SearchItem[] {
  return [
    ["/runs", "live runs"],
    ["/queue", "the queue"],
    ["/tasks", "the inbox"],
  ].map(([path, where]) => ({
    id: `handoff:${path}`,
    group: "handoff" as const,
    kind: "search",
    label: query,
    value: true,
    hint: `in ${where}`,
    href: `${path}?q=${encodeURIComponent(query)}`,
  }))
}

const meta: Meta<typeof CommandPalette> = {
  title: "Shell/CommandPalette",
  component: CommandPalette,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
  args: {
    open: true,
    onOpenChange: () => {},
    onQueryChange: () => {},
    onSelect: () => {},
  },
}

export default meta
type Story = StoryObj<typeof CommandPalette>

/**
 * Nothing typed. The resting list is every place and act this session can
 * reach — the honest answer to "what is here", and it costs no data to
 * produce because it is the rail read a second way.
 */
export const Resting: Story = {
  args: {
    query: "",
    items: SECTIONS,
  },
}

/**
 * An identifier, resolved by its shape.
 *
 * Nothing was searched: `5b1d7e40` is eight hex characters, which in this
 * product can only be a run, so the palette routes rather than looks. The row
 * is one destination because the shape admits exactly one.
 */
export const Resolving: Story = {
  args: {
    query: "5b1d7e40",
    items: [
      {
        id: "resolved:run:5b1d7e40",
        group: "resolved",
        kind: "run",
        label: "5b1d7e40",
        value: true,
        hint: "in live runs",
        href: "/runs/5b1d7e40",
      },
      ...handoffs("5b1d7e40"),
    ],
  },
}

/**
 * Two shapes answered, so the palette asks rather than picks.
 *
 * A handle can be a project's and the head of an application's name at once.
 * The candidates are labelled by kind and there are two of them, because both
 * catalogues are closed and small — this is a disambiguation bounded by
 * construction, not a results list.
 */
export const Ambiguous: Story = {
  args: {
    query: "atlas",
    items: [
      {
        id: "resolved:project:atlas",
        group: "resolved",
        kind: "project",
        label: "atlas",
        value: true,
        hint: "Atlas",
        href: "/projects?q=atlas",
      },
      {
        id: "resolved:app:atlas-api",
        group: "resolved",
        kind: "app",
        label: "atlas-api",
        value: true,
        hint: "runs on this app",
        href: "/runs?q=atlas-api",
      },
      {
        id: "resolved:app:atlas-web",
        group: "resolved",
        kind: "app",
        label: "atlas-web",
        value: true,
        hint: "runs on this app",
        href: "/runs?q=atlas-web",
      },
      ...handoffs("atlas"),
    ],
  },
}

/**
 * Free text, handed off.
 *
 * `webhook` is neither an identifier nor a screen, and the palette will not
 * invent a list of rows it cannot back — an honest full-text answer needs a
 * server-side index this product does not have. So it offers *acts*: enter
 * lands on that screen with its filter already applied, and the address it
 * lands on is a link somebody can paste into a ticket.
 */
export const HandingOff: Story = {
  args: {
    query: "webhook",
    items: handoffs("webhook"),
  },
}

/**
 * A session that can reach nothing the query names.
 *
 * The access rule is the rail's: a destination this role cannot open is
 * hidden, not shown and refused. When that empties every band, the palette
 * says so rather than drawing an empty list.
 */
export const Empty: Story = {
  args: {
    query: "sha256:9c41ab",
    items: [],
  },
}
