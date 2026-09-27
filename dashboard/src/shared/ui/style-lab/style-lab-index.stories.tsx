import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Section } from "@/shared/ui"

interface Specimen {
  readonly storyId: string
  readonly title: string
  readonly role: string
  readonly hint: string
}

interface Group {
  readonly title: string
  readonly tagline: string
  readonly items: readonly Specimen[]
}

/* The index page of the Style Lab. Every specimen on this page is a peer
   of the ones it links to, and the order is the order a new operator
   should read them in: the status system first (because it is the
   load-bearing part of the design language), then colour, then the
   typography, geometry, depth, and reading measure scales that the kit
   draws on.

   Each group names its own specimens directly rather than slicing a
   master list — the names tell which page the specimen lives on, the
   page is the unit the reader clicks, and a positional slice here was
   the kind of indirection that disappears once groups say what they
   carry. */
const GROUPS: readonly Group[] = [
  {
    title: "Status",
    tagline: "the load-bearing part of the design language",
    items: [
      {
        storyId: "ui-kit-style-lab-tokens-status-system--encoding",
        title: "Status System",
        role: "Dual-channel encoding",
        hint: "Hue + weave for every status. The audit trail in tokens.css is the only place these are stated.",
      },
    ],
  },
  {
    title: "Color",
    tagline: "palette + theme gallery",
    items: [
      {
        storyId: "ui-kit-style-lab-tokens-color-palette--active-theme",
        title: "Color Palette",
        role: "Active theme readout",
        hint: "What the toolbar is showing right now, primitives and status hues. Flip the theme and it repaints.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-color-palette--all-themes",
        title: "Color Palette",
        role: "Theme gallery",
        hint: "Seven themes x two modes. The --preview-* tokens keep this legible inside any theme context.",
      },
    ],
  },
  {
    title: "Typography",
    tagline: "scale, voices, tracking, leading, weights",
    items: [
      {
        storyId: "ui-kit-style-lab-tokens-typography--scale",
        title: "Typography",
        role: "Type scale",
        hint: "Eleven through twenty-four. A new step has to buy its place.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-typography--voices",
        title: "Typography",
        role: "Voices",
        hint: "Interface voice carries meaning; data voice carries values.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-typography--tracking",
        title: "Typography",
        role: "Tracking",
        hint: "Three roles, not a free knob.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-typography--leading",
        title: "Typography",
        role: "Leading",
        hint: "Four modes, and a fifth is a defect.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-typography--weights",
        title: "Typography",
        role: "Weights",
        hint: "Regular, medium, semibold, bold. Black is not on this scale.",
      },
    ],
  },
  {
    title: "Spacing",
    tagline: "the eight-step scale and how it stacks",
    items: [
      {
        storyId: "ui-kit-style-lab-tokens-spacing--scale",
        title: "Spacing",
        role: "Spacing scale",
        hint: "Eight steps, four to thirty-two. Nothing below --s1 exists.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-spacing--stacks",
        title: "Spacing",
        role: "Stacks",
        hint: "The same five items at three gaps, so the role of each step is felt.",
      },
    ],
  },
  {
    title: "Geometry",
    tagline: "radius, heights, icons",
    items: [
      {
        storyId: "ui-kit-style-lab-tokens-geometry--radius",
        title: "Geometry",
        role: "Radius",
        hint: "Five steps. The corner grows with the box.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-geometry--heights",
        title: "Geometry",
        role: "Heights",
        hint: "Nine heights in active use, four for buttons and five for chrome.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-geometry--icons",
        title: "Geometry",
        role: "Icons",
        hint: "Five sizes. An icon sits at the cap height of the type beside it.",
      },
    ],
  },
  {
    title: "Depth",
    tagline: "z-index ladder, shadows, motion",
    items: [
      {
        storyId: "ui-kit-style-lab-tokens-depth--z-index",
        title: "Depth",
        role: "Z-index ladder",
        hint: "Nine rungs, ten apart. Named for what sits on them.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-depth--shadows",
        title: "Depth",
        role: "Shadows",
        hint: "Six shadow tokens. Offset and blur, never a zero-offset halo.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-depth--motion",
        title: "Depth",
        role: "Motion",
        hint: "One easing curve, one duration. A new motion recipe has to use them both.",
      },
    ],
  },
  {
    title: "Reading measures",
    tagline: "five stops, ch and rem",
    items: [
      {
        storyId: "ui-kit-style-lab-tokens-reading-measures--stops",
        title: "Reading Measures",
        role: "Five stops",
        hint: "ch where the cap is about the words, rem where it is about the furniture.",
      },
      {
        storyId: "ui-kit-style-lab-tokens-reading-measures--note-vs-prose",
        title: "Reading Measures",
        role: "Note vs prose",
        hint: "The same paragraph at two different measures, side by side.",
      },
    ],
  },
]

interface GroupProps {
  readonly title: string
  readonly tagline: string
  readonly items: readonly Specimen[]
}

function Group({ title, tagline, items }: GroupProps): ReactNode {
  return (
    <section
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s4)",
        padding: "var(--s6)",
        border: "var(--hairline) solid var(--border)",
        borderRadius: "var(--r-md)",
        background: "var(--card)",
      }}
    >
      <header
        style={{
          display: "flex",
          alignItems: "baseline",
          justifyContent: "space-between",
          gap: "var(--s4)",
        }}
      >
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-h2)",
            fontWeight: "var(--fw-semibold)",
            color: "var(--text)",
          }}
        >
          {title}
        </span>
        <span
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            letterSpacing: "var(--tracking-label)",
            color: "var(--text-faint)",
            textTransform: "uppercase",
          }}
        >
          {tagline}
        </span>
      </header>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fill, minmax(20rem, 1fr))",
          gap: "var(--s4)",
        }}
      >
        {items.map((item) => (
          <article
            key={item.storyId}
            style={{
              display: "flex",
              flexDirection: "column",
              gap: "var(--s2)",
              padding: "var(--s4)",
              border: "var(--hairline) solid var(--border)",
              borderRadius: "var(--r-sm)",
              background: "var(--popover)",
            }}
          >
            <span
              style={{
                fontFamily: "var(--font-ui)",
                fontSize: "var(--t-body)",
                fontWeight: "var(--fw-medium)",
                color: "var(--text)",
              }}
            >
              {item.role}
            </span>
            <span
              style={{
                fontFamily: "var(--font-data)",
                fontSize: "var(--t-micro)",
                letterSpacing: "var(--tracking-data)",
                color: "var(--text-faint)",
              }}
            >
              {item.title}
            </span>
            <p
              style={{
                margin: 0,
                fontFamily: "var(--font-ui)",
                fontSize: "var(--t-sm)",
                lineHeight: "var(--lh-body)",
                color: "var(--text-muted)",
                maxInlineSize: "var(--measure-prose)",
              }}
            >
              {item.hint}
            </p>
          </article>
        ))}
      </div>
    </section>
  )
}

function Page({ children }: { readonly children: ReactNode }): ReactNode {
  return (
    <div
      style={{
        padding: "var(--page-y) var(--page-x)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--s8)",
        maxWidth: "var(--measure-record)",
        margin: "0 auto",
      }}
    >
      {children}
    </div>
  )
}

const meta: Meta = {
  title: "UI Kit/Style Lab/Index",
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj

/** The Style Lab at a glance. Seven groups, eighteen specimens, each one
    a single Storybook story in this section. The Status System sits
    alone at the top because it is the load-bearing part of the design
    language; the six scale groups below it are the vocabulary the kit
    draws on. */
export const Index: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Style Lab"
        note="The design system, in specimen pages. Every page here reads straight off the tokens in app/styles, so a change to a token lands in this lab without anyone re-stating it."
      >
        {GROUPS.map((group) => (
          <Group
            key={group.title}
            title={group.title}
            tagline={group.tagline}
            items={group.items}
          />
        ))}
      </Section>
    </Page>
  ),
}