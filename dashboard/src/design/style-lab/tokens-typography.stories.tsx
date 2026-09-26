import type { CSSProperties, ReactNode } from "react"

import { Section } from "@/shared/ui"

/* The seven-step type scale from `tokens.css`. The order matters: 11/12/13
   are the micro labels and the values the operator scans, 14 is the body,
   16/21/24 are the headings. Everything moved up one step with the
   spacing, for the same reason: 10px micro-labels beside 13px body left
   no room for a hierarchy to be *seen*, only inferred. */
interface TypeStep {
  readonly token: string
  readonly rem: string
  readonly role: string
  readonly sample: string
}

const SCALE: readonly TypeStep[] = [
  { token: "--t-micro", rem: "0.6875rem · 11", role: "micro label", sample: "PLEXOR · EU-WEST" },
  { token: "--t-xs", rem: "0.75rem · 12", role: "small label", sample: "synced 12s ago" },
  { token: "--t-sm", rem: "0.8125rem · 13", role: "value, body subtitle", sample: "12 of 16 workers up" },
  { token: "--t-body", rem: "0.875rem · 14", role: "body, control", sample: "Pool is scaling on a quota" },
  { token: "--t-h2", rem: "1rem · 16", role: "screen title", sample: "Pools" },
  { token: "--t-h1", rem: "1.3125rem · 21", role: "page title", sample: "Compute" },
  { token: "--t-display", rem: "1.5rem · 24", role: "display", sample: "12,847" },
]

interface Voice {
  readonly token: string
  readonly role: string
  readonly sentence: string
}

const VOICES: readonly Voice[] = [
  {
    token: "--font-ui",
    role: "interface voice — Archivo",
    sentence: "A pool's two ceilings answer to different owners; only one is binding.",
  },
  {
    token: "--font-data",
    role: "data voice — JetBrains Mono",
    sentence: "0.123 / 0.456 / 0.789 · worker-3 · claim 12s",
  },
]

interface TrackingRow {
  readonly token: string
  readonly value: string
  readonly role: string
  readonly sample: string
}

const TRACKING: readonly TrackingRow[] = [
  { token: "--tracking-label", value: "0.08em", role: "region heading · upper or lower", sample: "RUNNING NOW" },
  { token: "--tracking-data", value: "0.02em", role: "data label · value beside heading", sample: "7 in flight · 3 queued" },
  { token: "--tracking-display", value: "-0.02em", role: "display type", sample: "12,847 runs" },
]

interface Leading {
  readonly token: string
  readonly value: string
  readonly role: string
  readonly example: ReactNode
}

const LEADING: ReadonlyArray<Omit<Leading, "example"> & { readonly example: string }> = [
  {
    token: "--lh-flat",
    value: "1",
    role: "control text — height comes from the box, not the line",
    example: "Claim",
  },
  {
    token: "--lh-tight",
    value: "1.2",
    role: "one to three words that may still wrap",
    example: "Waiting on review",
  },
  {
    token: "--lh-display",
    value: "1.05",
    role: "large type — the bigger the cap, the more air proportional leading adds",
    example: "Pools",
  },
  {
    token: "--lh-body",
    value: "1.55",
    role: "prose — what a person reads, not scans",
    example: "A pool scales on quota plus provider capacity, so two ceilings with two owners.",
  },
]

function TypeStepRow({ step }: { readonly step: TypeStep }): ReactNode {
  const sampleStyle: CSSProperties = {
    fontFamily: "var(--font-ui)",
    fontSize: `var(${step.token})`,
    fontWeight: "var(--fw-medium)",
    lineHeight: "var(--lh-display)",
    color: "var(--text)",
    margin: 0,
  }
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "9rem 1fr",
        gap: "var(--s5)",
        alignItems: "baseline",
        paddingBlock: "var(--s4)",
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
          }}
        >
          {step.rem}
        </span>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s2)" }}>
        <p style={sampleStyle}>{step.sample}</p>
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

function VoiceCard({ voice }: { readonly voice: Voice }): ReactNode {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s3)",
        padding: "var(--s5)",
        border: "var(--hairline) solid var(--border)",
        borderRadius: "var(--r-md)",
        background: "var(--card)",
      }}
    >
      <span
        style={{
          fontFamily: "var(--font-data)",
          fontSize: "var(--t-micro)",
          letterSpacing: "var(--tracking-data)",
          color: "var(--text-faint)",
        }}
      >
        {voice.token}
      </span>
      <span
        style={{
          fontFamily: "var(--font-ui)",
          fontSize: "var(--t-body)",
          fontWeight: "var(--fw-semibold)",
          color: "var(--text)",
        }}
      >
        {voice.role}
      </span>
      <p
        style={{
          margin: 0,
          fontFamily: `var(${voice.token})`,
          fontSize: "var(--t-h2)",
          lineHeight: "var(--lh-display)",
          color: "var(--text)",
          maxInlineSize: "var(--measure-prose)",
        }}
      >
        {voice.sentence}
      </p>
    </div>
  )
}

function TrackingRow({ row }: { readonly row: TrackingRow }): ReactNode {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "9rem 1fr",
        gap: "var(--s5)",
        alignItems: "baseline",
        paddingBlock: "var(--s4)",
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
          {row.token}
        </span>
        <span
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            letterSpacing: "var(--tracking-data)",
            color: "var(--text-muted)",
          }}
        >
          {row.value}
        </span>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s2)" }}>
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-body)",
            fontWeight: "var(--fw-medium)",
            letterSpacing: `var(${row.token})`,
            color: "var(--text)",
          }}
        >
          {row.sample}
        </span>
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-micro)",
            color: "var(--text-faint)",
          }}
        >
          {row.role}
        </span>
      </div>
    </div>
  )
}

function LeadingRow({ entry }: { readonly entry: (typeof LEADING)[number] }): ReactNode {
  const isProse = entry.token === "--lh-body"
  const style: CSSProperties = {
    margin: 0,
    fontFamily: "var(--font-ui)",
    fontSize: isProse ? "var(--t-body)" : "var(--t-h2)",
    fontWeight: isProse ? "var(--fw-regular)" : "var(--fw-medium)",
    lineHeight: `var(${entry.token})`,
    color: "var(--text)",
    maxInlineSize: isProse ? "var(--measure-prose)" : undefined,
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
          {entry.token}
        </span>
        <span
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            letterSpacing: "var(--tracking-data)",
            color: "var(--text-muted)",
          }}
        >
          {entry.value}
        </span>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s3)" }}>
        <p style={style}>{entry.example}</p>
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-micro)",
            color: "var(--text-faint)",
          }}
        >
          {entry.role}
        </span>
      </div>
    </div>
  )
}

function WeightRow(): ReactNode {
  const weights = [
    { token: "--fw-regular", value: "400", label: "regular" },
    { token: "--fw-medium", value: "500", label: "medium" },
    { token: "--fw-semibold", value: "600", label: "semibold" },
    { token: "--fw-bold", value: "700", label: "bold" },
  ]
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: `repeat(${weights.length}, minmax(0, 1fr))`,
        gap: "var(--s4)",
      }}
    >
      {weights.map((weight) => (
        <div
          key={weight.token}
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "var(--s2)",
            padding: "var(--s4)",
            border: "var(--hairline) solid var(--border)",
            borderRadius: "var(--r-sm)",
            background: "var(--card)",
          }}
        >
          <span
            style={{
              fontFamily: "var(--font-data)",
              fontSize: "var(--t-micro)",
              letterSpacing: "var(--tracking-data)",
              color: "var(--text-faint)",
            }}
          >
            {weight.token}
          </span>
          <span
            style={{
              fontFamily: "var(--font-ui)",
              fontSize: "var(--t-h1)",
              fontWeight: `var(${weight.token})`,
              lineHeight: "var(--lh-display)",
              color: "var(--text)",
            }}
          >
            Pools
          </span>
          <span
            style={{
              fontFamily: "var(--font-data)",
              fontSize: "var(--t-micro)",
              letterSpacing: "var(--tracking-data)",
              color: "var(--text-muted)",
            }}
          >
            {weight.value}
          </span>
        </div>
      ))}
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

export default {
  title: "Compositions/Style Lab/Tokens/Typography",
  parameters: { layout: "fullscreen" },
}

/** The seven-step type scale. Each row holds the token name, the resolved
    size in rem and pixels, the role it plays in the product, and a sample
    sentence so the reading weight is visible at a glance. */
export function Scale(): ReactNode {
  return (
    <Page>
      <Section variant="screen" title="Type scale" note="Eleven through twenty-four. The lower three are values and labels the operator scans; the body step is the smallest that still reads as running prose; the upper three are headings and the figure they sit beside. A new step in this scale has to buy its place — the gap between two steps is what carries the hierarchy.">
        <div style={{ display: "flex", flexDirection: "column" }}>
          {SCALE.map((step) => (
            <TypeStepRow key={step.token} step={step} />
          ))}
        </div>
      </Section>
    </Page>
  )
}

/** The two voices. A token carrying a value or a label sits in the data
    voice; a token carrying a sentence sits in the interface voice. The
    distinction is not just visual — the data voice carries tabular figures
    and tight tracking, the interface voice carries the running prose the
    operator reads. */
export function Voices(): ReactNode {
  return (
    <Page>
      <Section variant="screen" title="Voices" note="The two voices the product speaks. --font-ui carries meaning; --font-data carries values. A label is in the data voice because it names a region, a value is in the data voice because it is a reading; a sentence that explains what something is, or what to do, is in the interface voice.">
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(20rem, 1fr))",
            gap: "var(--s4)",
          }}
        >
          {VOICES.map((voice) => (
            <VoiceCard key={voice.token} voice={voice} />
          ))}
        </div>
      </Section>
    </Page>
  )
}

/** The three tracking roles. The region heading wears the wide gesture,
    the inline value wears the tight one, and the display step tightens
    toward the cap. */
export function Tracking(): ReactNode {
  return (
    <Page>
      <Section variant="screen" title="Tracking" note="Three roles, not a free knob. The wide gesture marks a region heading; the tight gesture marks a value riding beside it; the negative gesture tightens the largest type, where proportional spacing adds air around the cap. A fourth would be a knob nobody has a name for — and an unnameable knob is how the raw `letter-spacing` literals happened the first time.">
        <div style={{ display: "flex", flexDirection: "column" }}>
          {TRACKING.map((row) => (
            <TrackingRow key={row.token} row={row} />
          ))}
        </div>
      </Section>
    </Page>
  )
}

/** The four leading modes. The text is not told how high to be by its size
    — it is told by what the text is *doing*. A button's height comes from
    its box, not from its line; a badge may still wrap; display type wants
    the air compressed; prose wants it opened up. */
export function Leading(): ReactNode {
  return (
    <Page>
      <Section variant="screen" title="Leading" note="Four modes, and a fifth is a defect. The reason there is no fifth: a leading that is 'a bit looser than tight' is a mode nobody can name, and an unnameable mode is how 29 raw literals across 27 files happened in the first place. --lh-flat lets the box set the height; --lh-tight holds a one-to-three-word label to two lines; --lh-display brings the cap down for large type; --lh-body opens prose up for reading.">
        <div style={{ display: "flex", flexDirection: "column" }}>
          {LEADING.map((entry) => (
            <LeadingRow key={entry.token} entry={entry} />
          ))}
        </div>
      </Section>
    </Page>
  )
}

/** The four weights. The voice picks the family; the role picks the weight.
    Regular carries prose; medium carries the values and labels the operator
    scans; semibold marks headings; bold is reserved for the moments where
    the message needs to out-shout the room. */
export function Weights(): ReactNode {
  return (
    <Page>
      <Section variant="screen" title="Weights" note="Four weights in regular use. Regular is the prose weight; medium is the body of a label or a value; semibold is the heading and the figure beside it; bold is for the moments where the message has to out-shout the room — an unscheduled restart, an account lockout. Black is not on this scale.">
        <WeightRow />
      </Section>
    </Page>
  )
}
