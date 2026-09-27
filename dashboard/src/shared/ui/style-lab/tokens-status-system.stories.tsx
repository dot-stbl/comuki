import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Section, StatusBadge } from "@/shared/ui"

import { ForceTheme } from "./force-theme"

/* Status is a closed vocabulary of seven: running, queued, waiting,
   escalated, failed, success, cancelled. The badge below wears the same
   word — and the hatch under it is theme-independent on purpose. It is
   the channel that does not depend on which palette is showing, and that
   is why every palette in the registry has to hold running and success
   furthest apart in lightness — those two wear no hatch, so the palette
   alone has to carry them. */
const STATUSES = [
  "running",
  "queued",
  "waiting",
  "escalated",
  "failed",
  "success",
  "cancelled",
] as const

type Status = (typeof STATUSES)[number]

interface StatusEncoding {
  readonly status: Status
  readonly summary: string
  readonly hatchAngle: string
}

const ENCODINGS: readonly StatusEncoding[] = [
  {
    status: "running",
    summary: "no hatch — palette carries it. The lightest status, and the only one whose weave token is `none`.",
    hatchAngle: "—",
  },
  {
    status: "queued",
    summary: "90° hatch, one pixel of ink in a six-pixel period — the quietest horizontal channel, marking a thing waiting its turn.",
    hatchAngle: "90° · 1/6px",
  },
  {
    status: "waiting",
    summary: "0° hatch, one pixel of ink in a four-pixel period — a vertical weave, the second-densest in the set.",
    hatchAngle: "0° · 1/4px",
  },
  {
    status: "escalated",
    summary: "45° hatch, two pixels in five — the up-and-right weave, and the densest of the three diagonals.",
    hatchAngle: "45° · 2/5px",
  },
  {
    status: "failed",
    summary: "−45° hatch, two pixels in five — the mirror of escalated, reading as down-and-right. Same density; the angle is the cue.",
    hatchAngle: "−45° · 2/5px",
  },
  {
    status: "success",
    summary: "no hatch — palette carries it. The other anchor of the lightness scale, paired with running at the other end.",
    hatchAngle: "—",
  },
  {
    status: "cancelled",
    summary: "67.5° hatch, one pixel in eight — the bisector of the gap left between 45° and 90°, and the quietest hatch in the set.",
    hatchAngle: "67.5° · 1/8px",
  },
]

interface ChannelSampleProps {
  readonly status: Status
  readonly channel: "hue" | "weave" | "both"
}

function ChannelSample({ status, channel }: ChannelSampleProps): ReactNode {
  /* Each sample sits on its own `[data-status]` element so the dual-channel
     declaration in `tokens.css` resolves `--hue` and `--weave` for it.
     `hue` is just the fill colour; `weave` is the hatch; `both` layers them.
     The fill and backgroundImage are written inline rather than through a
     helper because they are three lines long each, and a helper would have
     been a name without a notion. */
  const fill =
    channel === "hue" ? "var(--hue)" : "var(--card)"
  const backgroundImage = channel === "hue" ? "none" : "var(--weave)"
  return (
    <div
      data-status={status}
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "100%",
        height: "3.5rem",
        background: fill,
        backgroundImage: backgroundImage,
        border: "var(--hairline) solid var(--border)",
        borderRadius: "var(--r-sm)",
      }}
    />
  )
}

function StatusRow({ status }: { readonly status: Status }): ReactNode {
  const encoding = ENCODINGS.find((entry) => entry.status === status)
  if (!encoding) return null
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "8rem 1fr",
        gap: "var(--s5)",
        alignItems: "start",
        paddingBlock: "var(--s5)",
        borderTop: "var(--hairline) solid var(--rule)",
      }}
    >
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          gap: "var(--s3)",
        }}
      >
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-body)",
            fontWeight: "var(--fw-semibold)",
            color: "var(--text)",
          }}
        >
          {status}
        </span>
        <StatusBadge status={status} />
      </div>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(3, minmax(0, 1fr))",
          gap: "var(--s3)",
        }}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--s2)" }}>
          <ChannelSample status={status} channel="hue" />
          <ChannelLabel label="hue" sublabel="--hue" />
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--s2)" }}>
          <ChannelSample status={status} channel="weave" />
          <ChannelLabel label="weave" sublabel="--weave" />
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--s2)" }}>
          <ChannelSample status={status} channel="both" />
          <ChannelLabel label="both" sublabel="fill + hatch" />
        </div>
      </div>
    </div>
  )
}

function ChannelLabel({ label, sublabel }: { readonly label: string; readonly sublabel: string }): ReactNode {
  return (
    <div
      style={{
        display: "flex",
        alignItems: "baseline",
        justifyContent: "space-between",
        gap: "var(--s3)",
      }}
    >
      <span
        style={{
          fontFamily: "var(--font-ui)",
          fontSize: "var(--t-micro)",
          fontWeight: "var(--fw-medium)",
          color: "var(--text-muted)",
        }}
      >
        {label}
      </span>
      <span
        style={{
          fontFamily: "var(--font-data)",
          fontSize: "var(--t-micro)",
          letterSpacing: "var(--tracking-data)",
          color: "var(--text-faint)",
        }}
      >
        {sublabel}
      </span>
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
  title: "UI Kit/Style Lab/Tokens/Status System",
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj

/** Each status shown in three ways side by side: the hue alone, the weave
    alone, and the two channels layered — to prove that the meaning survives
    when either channel is taken away. The seventh row, `cancelled`, and the
    badge to its left, are the proof that the whole encoding cost one
    `data-status` block in `tokens.css`. */
export const Encoding: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Dual channel"
        note="Every status wears two marks. The hue is the palette's role for the status; the weave is the hatch the same status paints over it. A consumer reads either channel; the audit trail in tokens.css is the place where both are stated once, and the badge at the left of each row is the third place where the encoding lands, on top of the fill and the hatch."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--s4)" }}>
          {STATUSES.map((status) => (
            <StatusRow key={status} status={status} />
          ))}
        </div>
      </Section>
    </Page>
  ),
}

/** The same matrix, pinned to the dark mode the product ships in. The
    toolbar global is a reader's choice; a palette specimen is a document,
    and a document that changed with the toolbar would show one theme and
    merely describe the other. */
export const DarkReading: Story = {
  render: () => (
    <ForceTheme mode="dark">
      <Page>
        <Section
          variant="screen"
          title="Dual channel"
          note="Every status wears two marks. The hue is the palette's role for the status; the weave is the hatch the same status paints over it. A consumer reads either channel; the audit trail in tokens.css is the place where both are stated once, and the badge at the left of each row is the third place where the encoding lands, on top of the fill and the hatch."
        >
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--s4)" }}>
            {STATUSES.map((status) => (
              <StatusRow key={status} status={status} />
            ))}
          </div>
        </Section>
      </Page>
    </ForceTheme>
  ),
}

/** The same matrix in the light mode — the mode the palette's floors were
    derived against, and the one where a status hue that only clears
    contrast in the dark shows itself immediately. The two pinned readings
    side by side are the whole point of the lab. */
export const LightReading: Story = {
  render: () => (
    <ForceTheme mode="light">
      <Page>
        <Section
          variant="screen"
          title="Dual channel"
          note="Every status wears two marks. The hue is the palette's role for the status; the weave is the hatch the same status paints over it. A consumer reads either channel; the audit trail in tokens.css is the place where both are stated once, and the badge at the left of each row is the third place where the encoding lands, on top of the fill and the hatch."
        >
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--s4)" }}>
            {STATUSES.map((status) => (
              <StatusRow key={status} status={status} />
            ))}
          </div>
        </Section>
      </Page>
    </ForceTheme>
  ),
}

/** The seven hatch angles written as their inputs — the second column of
    `tokens.css` brought into the open. Reading the table top to bottom is
    reading the lightness scale in the same order: `running` and `success`
    wear no hatch, then the four diagonals climb from 67.5° (the bisector
    of the gap) to 0° (the densest vertical). */
export const WeaveAngles: Story = {
  render: () => (
    <Page>
      <Section variant="screen" title="Weave table" note="The seven hatch declarations in `tokens.css`, transposed to a table. Every angle is 22.5° from its neighbour; the gap between 45° (escalated) and 90° (queued) is the widest left, so 67.5° (cancelled) bisects it. The pattern density follows the same order — the quietest hatch in the set is the bisector, and `running` and `success` are the only rows with no hatch at all.">
        <table
          style={{
            width: "100%",
            borderCollapse: "collapse",
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-sm)",
            color: "var(--text)",
          }}
        >
          <thead>
            <tr style={{ borderBottom: "var(--hairline) solid var(--rule)" }}>
              <th style={cellHead()}>status</th>
              <th style={cellHead()}>angle</th>
              <th style={cellHead()}>density</th>
              <th style={cellHead()}>period</th>
              <th style={cellHead()}>ink</th>
              <th style={cellHead()}>role</th>
            </tr>
          </thead>
          <tbody>
            {ENCODINGS.map((entry) => (
              <tr key={entry.status} style={{ borderBottom: "var(--hairline) solid var(--rule)" }}>
                <td style={cellBody()}>
                  <span
                    style={{
                      fontFamily: "var(--font-ui)",
                      fontWeight: "var(--fw-medium)",
                    }}
                  >
                    {entry.status}
                  </span>
                </td>
                <td style={cellMono()}>{entry.hatchAngle.split(" ")[0]}</td>
                <td style={cellMono()}>
                  {entry.hatchAngle.includes(" ") ? entry.hatchAngle.split(" ")[1] : "—"}
                </td>
                <td style={cellMono()}>—</td>
                <td style={cellMono()}>—</td>
                <td style={cellMuted()}>{entry.summary.split(" — ")[0]}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Section>
    </Page>
  ),
}

function cellHead(): React.CSSProperties {
  return {
    textAlign: "left",
    padding: "var(--s3) var(--s4)",
    fontWeight: "var(--fw-medium)",
    letterSpacing: "var(--tracking-label)",
    color: "var(--text-faint)",
    fontSize: "var(--t-micro)",
    textTransform: "uppercase",
  }
}

function cellBody(): React.CSSProperties {
  return {
    padding: "var(--s3) var(--s4)",
    borderTop: "var(--hairline) solid var(--rule)",
  }
}

function cellMono(): React.CSSProperties {
  return {
    padding: "var(--s3) var(--s4)",
    borderTop: "var(--hairline) solid var(--rule)",
    fontVariantNumeric: "tabular-nums",
    letterSpacing: "var(--tracking-data)",
    color: "var(--text-muted)",
  }
}

function cellMuted(): React.CSSProperties {
  return {
    padding: "var(--s3) var(--s4)",
    borderTop: "var(--hairline) solid var(--rule)",
    color: "var(--text-muted)",
  }
}