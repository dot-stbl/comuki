import type { CSSProperties, ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Section } from "@/shared/ui"

/* The five-stop reading measure scale from `tokens.css`. `ch` where the
   cap is about the words, `rem` where it is about the furniture. A
   measure is the widest a run of words is allowed to run, chosen by
   what the words are doing rather than by the width the window happens
   to be. */
interface MeasureStep {
  readonly token: string
  readonly value: string
  readonly role: string
  readonly sample: string
}

const STEPS: readonly MeasureStep[] = [
  {
    token: "--measure-note",
    value: "52ch",
    role: "a state's sentence or a lead, the line that has to be read, not scanned",
    sample:
      "Pool is scaling on a quota plus the provider's capacity API, so every pool has two ceilings with two different owners.",
  },
  {
    token: "--measure-prose",
    value: "68ch",
    role: "a paragraph of body prose",
    sample:
      "The two ceilings answer to different people: the quota is ours, the capacity is theirs, and only one of them is refusing the next container at any moment. The console has to show which one it is without making the operator read the page header to find out. That is what this line is for, and the line is 68 characters wide so a paragraph does not outrun the eye before it gets to the next thought.",
  },
  {
    token: "--measure-wide",
    value: "78ch",
    role: "a region's own prose, a section lead, a summary spoken under a full-width heading",
    sample:
      "The dashboard is a board, not a stack of pages. Five screens are pinned at once, and the operator reads them in the order their eyes happen to land, not the order the router set. The chrome has to make the board readable at any density, and the chrome is the same chrome regardless of which five screens are open. That is why the chrome is a token, not a stylesheet: it has to land in the same place in seven themes without anyone re-stating it.",
  },
  {
    token: "--measure-form",
    value: "44rem",
    role: "a controlled block of inputs, and the structured answers the same width",
    sample: "form",
  },
  {
    token: "--measure-record",
    value: "60rem",
    role: "one record's page, the single width every detail screen takes, caps-centred",
    sample: "record",
  },
]

const PROSE_LIPSUM =
  "The two ceilings answer to different people: the quota is ours, the capacity is theirs, and only one of them is refusing the next container at any moment. The console has to show which one it is without making the operator read the page header to find out."

function MeasureSpecimen({ step }: { readonly step: MeasureStep }): ReactNode {
  /* Each specimen renders the sample text at the measured width, so the
     reader can see the right margin exactly where the cap says it falls.
     `max-inline-size` on the inner paragraph holds the line, and a
     right-side guide in --rule marks the cap. */
  const innerStyle: CSSProperties = {
    margin: 0,
    fontFamily: "var(--font-ui)",
    fontSize: "var(--t-body)",
    lineHeight: "var(--lh-body)",
    color: "var(--text)",
    maxInlineSize: `var(${step.token})`,
  }
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "9rem 1fr",
        gap: "var(--s5)",
        alignItems: "start",
        paddingBlock: "var(--s5)",
        borderTop: "var(--hairline) solid var(--rule)",
      }}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s2)" }}>
        <span
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            letterSpacing: "var(--tracking-data)",
            color: "var(--text-faint)",
          }}
        >
          {step.token}
        </span>
        <span
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            letterSpacing: "var(--tracking-data)",
            color: "var(--text-muted)",
            fontVariantNumeric: "tabular-nums",
          }}
        >
          {step.value}
        </span>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s3)" }}>
        {step.token === "--measure-form" ? (
          <FormFurniture />
        ) : step.token === "--measure-record" ? (
          <RecordFurniture />
        ) : (
          <p style={innerStyle}>{step.sample}</p>
        )}
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-micro)",
            color: "var(--text-faint)",
          }}
        >
          {step.role}
        </span>
      </div>
    </div>
  )
}

function FormFurniture(): ReactNode {
  /* The form measure is `44rem` because its inputs and its fact grids are
     laid out against fixed tracks rather than against the type beside it.
     The specimen is a small block of inputs at the cap width. */
  return (
    <div
      style={{
        maxInlineSize: "var(--measure-form)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--s4)",
        padding: "var(--s5)",
        border: "var(--hairline) solid var(--border)",
        borderRadius: "var(--r-md)",
        background: "var(--card)",
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
        Create pool
      </span>
      <FormField label="Name" value="pool-eu-west" />
      <FormField label="Replicas" value="3" />
      <FormField label="Region" value="eu-west-1" />
    </div>
  )
}

function FormField({ label, value }: { readonly label: string; readonly value: string }): ReactNode {
  return (
    <label
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s2)",
      }}
    >
      <span
        style={{
          fontFamily: "var(--font-ui)",
          fontSize: "var(--t-micro)",
          color: "var(--text-muted)",
        }}
      >
        {label}
      </span>
      <span
        style={{
          display: "block",
          padding: "var(--s2) var(--s3)",
          border: "var(--hairline) solid var(--border-strong)",
          borderRadius: "var(--r-md)",
          fontFamily: "var(--font-data)",
          fontSize: "var(--t-sm)",
          color: "var(--text)",
          background: "var(--popover)",
        }}
      >
        {value}
      </span>
    </label>
  )
}

function RecordFurniture(): ReactNode {
  /* The record measure is `60rem`, caps-centred. The specimen is a small
     record at the cap width. */
  return (
    <div
      style={{
        maxInlineSize: "var(--measure-record)",
        marginInline: "auto",
        padding: "var(--s6)",
        border: "var(--hairline) solid var(--border)",
        borderRadius: "var(--r-lg)",
        background: "var(--card)",
        display: "flex",
        flexDirection: "column",
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
        pool-eu-west
      </span>
      <span
        style={{
          fontFamily: "var(--font-ui)",
          fontSize: "var(--t-sm)",
          color: "var(--text-muted)",
        }}
      >
        3 replicas / 12 of 16 workers up / min idle 2 / last scale 12s ago
      </span>
    </div>
  )
}

function SideBySide(): ReactNode {
  /* Two paragraphs at two different measures, so the eye can compare.
     The left is --measure-note (52ch, the line that has to be read, not
     scanned); the right is --measure-prose (68ch, a paragraph of body
     prose). */
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "1fr 1fr",
        gap: "var(--s6)",
      }}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s3)" }}>
        <span
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            letterSpacing: "var(--tracking-label)",
            color: "var(--text-faint)",
            textTransform: "uppercase",
          }}
        >
          --measure-note / 52ch
        </span>
        <p
          style={{
            margin: 0,
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-body)",
            lineHeight: "var(--lh-body)",
            color: "var(--text)",
            maxInlineSize: "var(--measure-note)",
          }}
        >
          {PROSE_LIPSUM.split(". ")[0]}.
        </p>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s3)" }}>
        <span
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            letterSpacing: "var(--tracking-label)",
            color: "var(--text-faint)",
            textTransform: "uppercase",
          }}
        >
          --measure-prose / 68ch
        </span>
        <p
          style={{
            margin: 0,
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-body)",
            lineHeight: "var(--lh-body)",
            color: "var(--text)",
            maxInlineSize: "var(--measure-prose)",
          }}
        >
          {PROSE_LIPSUM}
        </p>
      </div>
    </div>
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
  title: "UI Kit/Style Lab/Tokens/Reading Measures",
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj

/** The five reading measure stops with running prose at the cap width.
    `ch` where the cap is about the words, `rem` where it is about the
    furniture: a note or a paragraph is measured in characters because
    the reader holds characters, while a form or a record is measured in
    rems because its inputs and its fact grids are laid out against
    fixed tracks rather than against the type beside it. */
export const Stops: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Reading measures"
        note="Five stops on one scale, so no screen invents a sixth. A measure is the widest a run of words is allowed to run, chosen by what the words are doing rather than by the width the window happens to be. The first three stops are about the words (ch); the last two are about the furniture (rem)."
      >
        <div style={{ display: "flex", flexDirection: "column" }}>
          {STEPS.map((step) => (
            <MeasureSpecimen key={step.token} step={step} />
          ))}
        </div>
      </Section>
    </Page>
  ),
}

/** The two prose stops side by side at the cap width, so the difference
    between a note and a paragraph is felt rather than read. */
export const NoteVsProse: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Note vs prose"
        note="--measure-note is the line that has to be read, not scanned. --measure-prose is a paragraph of body prose. The eye holds characters, not pixels, so the cap is in ch; the same paragraph at 68ch reads differently than the same paragraph at 52ch."
      >
        <SideBySide />
      </Section>
    </Page>
  ),
}