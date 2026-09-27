import type { CSSProperties, ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Section } from "@/shared/ui"

/* The eight-step spacing scale from `tokens.css`. Retuned upward after the
   board was walked end-to-end: the first scale ran 2/5/7/10px, which is
   instrument-panel tight, and fifteen screens of it read as one
   undifferentiated mass. The scale is now 4/6/8/12/16/20/24/32, so the
   small end still packs a table row and the large end can actually
   separate one region from the next. */
interface SpaceStep {
  readonly token: string
  readonly px: number
  readonly role: string
}

const STEPS: readonly SpaceStep[] = [
  { token: "--s1", px: 4, role: "between siblings in a control: icon and label" },
  { token: "--s2", px: 6, role: "gutter of a small piece of furniture" },
  { token: "--s3", px: 8, role: "the working step: between labels and their values" },
  { token: "--s4", px: 12, role: "between sections of a stack, between adjacent rails" },
  { token: "--s5", px: 16, role: "the breathing step: between unrelated blocks" },
  { token: "--s6", px: 20, role: "screen vertical padding" },
  { token: "--s7", px: 24, role: "screen horizontal padding" },
  { token: "--s8", px: 32, role: "between regions of the same screen" },
]

function SpaceBar({ step }: { readonly step: SpaceStep }): ReactNode {
  /* Each row draws the step as a horizontal bar in --primary, then sits
     the role text beside it. The bar's width is the token — the px value
     is read off CSS, the row is a live scale rather than a measured one. */
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "9rem 1fr",
        gap: "var(--s5)",
        alignItems: "center",
        paddingBlock: "var(--s3)",
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
          {step.px}px
        </span>
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: "var(--s4)" }}>
        <div
          style={{
            height: "var(--h-meter)",
            width: `var(${step.token})`,
            background: "var(--primary)",
            borderRadius: "var(--hairline)",
          }}
          aria-label={`${step.token} is ${step.px} pixels`}
        />
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-sm)",
            color: "var(--text-muted)",
          }}
        >
          {step.role}
        </span>
      </div>
    </div>
  )
}

interface ClusterProps {
  readonly children: ReactNode
  readonly gap: string
  readonly label: string
}

/* The "stacked" specimen at the bottom: a stack of cards with a chosen
   gap, drawn at three different steps so the reader can feel the
   difference between them. */
function Cluster({ children, gap, label }: ClusterProps): ReactNode {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: gap,
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
          letterSpacing: "var(--tracking-label)",
          color: "var(--text-faint)",
        }}
      >
        {label}
      </span>
      {children}
    </div>
  )
}

function StackCard({ label }: { readonly label: string }): ReactNode {
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: "var(--s3)",
        padding: "var(--s3) var(--s4)",
        border: "var(--hairline) solid var(--rule)",
        borderRadius: "var(--r-sm)",
        background: "var(--popover)",
        fontFamily: "var(--font-ui)",
        fontSize: "var(--t-sm)",
        color: "var(--text)",
      }}
    >
      <span
        style={{
          width: "var(--s3)",
          height: "var(--s3)",
          borderRadius: "var(--hairline)",
          background: "var(--primary)",
        }}
      />
      {label}
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

const containerStyle: CSSProperties = {
  display: "flex",
  flexDirection: "column",
}

const meta: Meta = {
  title: "UI Kit/Style Lab/Tokens/Spacing",
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj

/** The eight spacing steps drawn as bars in the order of the scale. Each
    bar's width is the step itself, a live visual scale the body can
    measure against the role the step plays. */
export const Scale: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Spacing scale"
        note="Eight steps, four to thirty-two. The first three are the working set; --s4 is the section step; --s5 is the breathing step; --s6 and --s7 are the screen padding; --s8 is the gap between regions on the same screen. Nothing below --s1 exists, and a gap smaller than 4px is a mistake, not a decision."
      >
        <div style={containerStyle}>
          {STEPS.map((step) => (
            <SpaceBar key={step.token} step={step} />
          ))}
        </div>
      </Section>
    </Page>
  ),
}

const CARDS = [
  "Pool / eu-west",
  "Worker / worker-3",
  "Run / 1f3a2",
  "Queue / 7 waiting",
  "Audit / 12s ago",
] as const

/** The same five cards stacked with three different gaps, so the role of
    each step is felt rather than read. The visual difference between
    --s4, --s5, and --s6 is the difference between tight, normal, and
    generous, the only three spacings the operator usually has to pick. */
export const Stacks: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Stacks"
        note="The same five items at three different gaps. --s4 is the section step, tight, but you can still see where one card ends and the next begins. --s5 is the breathing step, what you reach for when the stack is the answer to a question. --s6 is generous, and is the wrong pick for a stack of related items; it is for a stack of unrelated items the operator has to scan."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(18rem, 1fr))",
            gap: "var(--s5)",
          }}
        >
          <Cluster gap="var(--s4)" label="--s4 / 12px / tight">
            {CARDS.map((label) => (
              <StackCard key={label} label={label} />
            ))}
          </Cluster>
          <Cluster gap="var(--s5)" label="--s5 / 16px / normal">
            {CARDS.map((label) => (
              <StackCard key={label} label={label} />
            ))}
          </Cluster>
          <Cluster gap="var(--s6)" label="--s6 / 20px / generous">
            {CARDS.map((label) => (
              <StackCard key={label} label={label} />
            ))}
          </Cluster>
        </div>
      </Section>
    </Page>
  ),
}