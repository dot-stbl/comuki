import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Section } from "@/shared/ui"

/* The five-step radius scale from `tokens.css`. The corner grows with the
   box; there is no zero step on this scale. The steps are optical, and
   optical in both directions: a small box wearing a large corner stops
   being a rectangle and reads as a capsule, and a large box wearing a
   small corner reads as square whatever the number is. The step is
   chosen by the box, not by the component's category. */
interface RadiusStep {
  readonly token: string
  readonly px: number
  readonly role: string
}

const RADII: readonly RadiusStep[] = [
  { token: "--hairline", px: 1, role: "the one-pixel unit: hairline gaps, the smallest marks" },
  { token: "--r-xs", px: 3, role: "marks and tracks: meters, drawn channels, swatches, the switch thumb, a checkbox, an inline code span" },
  { token: "--r-sm", px: 5, role: "small furniture: badges, chips, tags, menu and rail items, tooltips" },
  { token: "--r-md", px: 7, role: "controls and the blocks that stand beside them: buttons, text inputs and selects, popovers, notice bands, the flow nodes" },
  { token: "--r-lg", px: 10, role: "the screen's own surfaces: the data-table frame, panels, dialogs" },
]

interface HeightStep {
  readonly token: string
  readonly rem: string
  readonly role: string
}

const HEIGHTS: readonly HeightStep[] = [
  { token: "--h-button-sm", rem: "1.5rem / 24", role: "icon-only buttons, table row approve/cancel" },
  { token: "--h-button", rem: "1.75rem / 28", role: "the default button" },
  { token: "--h-button-lg", rem: "2.125rem / 34", role: "primary action above the default" },
  { token: "--h-button-xl", rem: "2.375rem / 38", role: "floating chrome that has to read against a busy board" },
  { token: "--h-meter", rem: "0.5rem / 8", role: "swatches, track heights, the brand mark" },
  { token: "--h-row-head", rem: "2rem / 32", role: "the data-table header band" },
  { token: "--h-bar", rem: "3rem / 48", role: "the run graph depth band" },
  { token: "--h-day-series", rem: "3rem / 48", role: "day-series chart band" },
  { token: "--h-spark", rem: "1.5rem / 24", role: "the one-line sparkline beside a figure" },
]

interface IconStep {
  readonly token: string
  readonly rem: string
  readonly role: string
}

const ICONS: readonly IconStep[] = [
  { token: "--icon-xs", rem: "0.6875rem / 11", role: "beside micro labels" },
  { token: "--icon-sm", rem: "0.8125rem / 13", role: "beside values" },
  { token: "--icon-md", rem: "0.9375rem / 15", role: "the default beside body, and the default control" },
  { token: "--icon-lg", rem: "1.125rem / 18", role: "the large control" },
  { token: "--icon-xl", rem: "1.75rem / 28", role: "the dock trigger, dominant mark on a corner" },
]

function RadiusRow({ step }: { readonly step: RadiusStep }): ReactNode {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "9rem 1fr",
        gap: "var(--s5)",
        alignItems: "center",
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
            fontVariantNumeric: "tabular-nums",
          }}
        >
          {step.px}px
        </span>
      </div>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: "var(--s5)",
        }}
      >
        <div
          style={{
            width: "5rem",
            height: "3.5rem",
            background: "var(--card)",
            border: "var(--hairline) solid var(--border-strong)",
            borderRadius: `var(${step.token})`,
          }}
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

function RadiusPair(): ReactNode {
  /* Three boxes, three radii, drawn at the same size. The visual lesson:
     a small radius on a small box looks square; a small radius on a big
     box also looks square. A large radius on a small box looks like a
     capsule. The corner is not a decoration you apply or withhold, it is
     a property of the box. */
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "repeat(3, minmax(0, 1fr))",
        gap: "var(--s5)",
        alignItems: "end",
      }}
    >
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s3)" }}>
        <div
          style={{
            width: "100%",
            height: "2rem",
            background: "var(--accent)",
            border: "var(--hairline) solid var(--border-strong)",
            borderRadius: "var(--r-xs)",
          }}
        />
        <span style={captionStyle}>2rem box, --r-xs (3px)</span>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s3)" }}>
        <div
          style={{
            width: "100%",
            height: "5rem",
            background: "var(--accent)",
            border: "var(--hairline) solid var(--border-strong)",
            borderRadius: "var(--r-sm)",
          }}
        />
        <span style={captionStyle}>5rem box, --r-sm (5px)</span>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s3)" }}>
        <div
          style={{
            width: "100%",
            height: "8rem",
            background: "var(--accent)",
            border: "var(--hairline) solid var(--border-strong)",
            borderRadius: "var(--r-lg)",
          }}
        />
        <span style={captionStyle}>8rem box, --r-lg (10px)</span>
      </div>
    </div>
  )
}

const captionStyle: React.CSSProperties = {
  fontFamily: "var(--font-data)",
  fontSize: "var(--t-micro)",
  letterSpacing: "var(--tracking-data)",
  color: "var(--text-faint)",
  fontVariantNumeric: "tabular-nums",
}

function HeightRow({ step }: { readonly step: HeightStep }): ReactNode {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "11rem 1fr",
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
          {step.rem}
        </span>
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: "var(--s4)" }}>
        <div
          style={{
            height: `var(${step.token})`,
            width: "8rem",
            background: "var(--accent)",
            border: "var(--hairline) solid var(--border-strong)",
            borderRadius: "var(--r-xs)",
          }}
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

function IconRow({ step }: { readonly step: IconStep }): ReactNode {
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
          {step.rem}
        </span>
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: "var(--s4)" }}>
        <div
          style={{
            height: `var(${step.token})`,
            width: `var(${step.token})`,
            background: "var(--primary)",
            borderRadius: "var(--r-xs)",
          }}
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
  title: "UI Kit/Style Lab/Tokens/Geometry",
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj

/** The five-step radius scale. Each row pairs the token with the size of
    box it was written for, and a sample rectangle drawn at that radius.
    The corner is optical: it is a property of the box, not a decoration
    a call site picks from a palette. */
export const Radius: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Radius"
        note="Five steps, one to ten. There is no zero step on this scale; a box without a corner reads as either unfinished or as a debug element. The old scale ran 2 / 3.6 / 6px *and nothing at all on every data surface*, so half the product was rounded and half was square, which is exactly what it read as."
      >
        <div style={{ display: "flex", flexDirection: "column" }}>
          {RADII.map((step) => (
            <RadiusRow key={step.token} step={step} />
          ))}
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--s4)", marginTop: "var(--s6)" }}>
          <span
            style={{
              fontFamily: "var(--font-data)",
              fontSize: "var(--t-micro)",
              letterSpacing: "var(--tracking-label)",
              color: "var(--text-faint)",
              textTransform: "uppercase",
            }}
          >
            Corner follows box, not category
          </span>
          <RadiusPair />
        </div>
      </Section>
    </Page>
  ),
}

/** The five control heights plus the four chrome heights. Every height
    has a role, and the role is not "this is a button". */
export const Heights: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Heights"
        note="Nine heights in active use. Four are buttons, climbing from --h-button-sm at the WCAG 2.2 minimum target to --h-button-xl for floating chrome; the rest are chrome, meter swatches, header bands and chart bands. The default button came down a step across the board so the buttons stopped reading heavy against 13px body type, and --h-button-xl went up one step from the lower recommended size because the dock trigger sits alone and needs to read as a distinct affordance."
      >
        <div style={{ display: "flex", flexDirection: "column" }}>
          {HEIGHTS.map((step) => (
            <HeightRow key={step.token} step={step} />
          ))}
        </div>
      </Section>
    </Page>
  ),
}

/** The five icon sizes. An icon sits at the cap height of the type beside
    it, never above it: xs beside micro labels, sm beside values, md
    beside body, lg for the large control, xl for the floating chrome. */
export const Icons: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Icons"
        note="Five sizes in active use. An icon at 11 sits beside an 11px micro label; an icon at 28 fills the 38px floating trigger. Call sites do not hand-size icons; kit controls size their own SVGs from this. A free size is a knob nobody has a use for, and a knob nobody uses is how two different `fontSize` literals happened the first time."
      >
        <div style={{ display: "flex", flexDirection: "column" }}>
          {ICONS.map((step) => (
            <IconRow key={step.token} step={step} />
          ))}
        </div>
      </Section>
    </Page>
  ),
}