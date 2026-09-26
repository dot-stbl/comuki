import type { ReactNode } from "react"

import { Section } from "@/shared/ui"

/* The nine-rung z-index ladder from `tokens.css`. Named for what sits on
   them, never for how high they are: a consumer states a role and the
   ladder decides the number. Ten apart so a component can sit half a
   step above its neighbour without climbing a whole rung, and so the
   gaps stay legible when one does. */
interface ZRung {
  readonly token: string
  readonly role: string
  readonly owner: string
}

const RUNGS: readonly ZRung[] = [
  { token: "--z-base", role: "the page's own plane", owner: "the document" },
  { token: "--z-raised", role: "a cell or row lifted out of its siblings", owner: "the page" },
  { token: "--z-sticky", role: "chrome pinned inside a scroll port (table head band)", owner: "the scroll port" },
  { token: "--z-dock", role: "chrome docked to the window edge, the page scrolls under", owner: "the viewport" },
  { token: "--z-dropdown", role: "a menu or listbox belonging to a control", owner: "the control" },
  { token: "--z-overlay", role: "anything that dims the page behind it, every scrim", owner: "the document" },
  { token: "--z-modal", role: "a panel riding on top of a scrim it does not own", owner: "the scrim" },
  { token: "--z-popover", role: "a surface that must clear a modal (select inside a form dialog)", owner: "the modal" },
  { token: "--z-tooltip", role: "the last word, never a target", owner: "the document" },
]

interface ShadowStep {
  readonly token: string
  readonly role: string
  readonly sample: React.CSSProperties["boxShadow"]
}

const SHADOWS: readonly ShadowStep[] = [
  {
    token: "--shadow-modal",
    role: "panel riding a scrim",
    sample: "var(--shadow-modal)",
  },
  {
    token: "--shadow-lift",
    role: "bottom-of-viewport lift (dock strip)",
    sample: "var(--shadow-lift)",
  },
  {
    token: "--shadow-header",
    role: "sticky header inside a scroll port",
    sample: "var(--shadow-header)",
  },
  {
    token: "--shadow-pinned",
    role: "seam beside a pinned cell (right-side hairline)",
    sample: "var(--shadow-pinned)",
  },
  {
    token: "--shadow-focus-ring",
    role: "the soft focus ring as a shadow (controls)",
    sample: "var(--shadow-focus-ring)",
  },
  {
    token: "--shadow-focus-ring-inset",
    role: "the same ring turned inward (rail row, pool strip)",
    sample: "var(--shadow-focus-ring-inset)",
  },
]

interface Motion {
  readonly token: string
  readonly value: string
  readonly role: string
}

const MOTION: readonly Motion[] = [
  { token: "--ease", value: "cubic-bezier(0.2, 0.6, 0.2, 1)", role: "the only easing curve in the product" },
  { token: "--dur", value: "180ms", role: "the only duration in the product" },
]

function ZRow({ rung, index }: { readonly rung: ZRung; readonly index: number }): ReactNode {
  /* Each rung is a row in the ladder. The stair-step on the right is the
     ladder rendered as nine step shapes: the deeper the rung, the higher
     up the shape. */
  const height = 0.4 + index * 0.18
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "9rem 1fr 9rem",
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
          {rung.token}
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
          {index * 10}
        </span>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: "var(--s2)" }}>
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-body)",
            fontWeight: "var(--fw-medium)",
            color: "var(--text)",
          }}
        >
          {rung.role}
        </span>
        <span
          style={{
            fontFamily: "var(--font-ui)",
            fontSize: "var(--t-micro)",
            color: "var(--text-faint)",
          }}
        >
          owner: {rung.owner}
        </span>
      </div>
      <div
        style={{
          position: "relative",
          height: `${height}rem`,
          background: "var(--primary)",
          borderRadius: "var(--r-xs)",
        }}
      />
    </div>
  )
}

function ShadowTile({ step }: { readonly step: ShadowStep }): ReactNode {
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
        {step.token}
      </span>
      <div
        style={{
          width: "100%",
          height: "3rem",
          background: "var(--popover)",
          borderRadius: "var(--r-sm)",
          boxShadow: step.sample,
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
  )
}

function ZLadder(): ReactNode {
  /* A visual ladder: nine shapes stacked at the height of their rung. The
     visual order matches the role order in the table below. */
  const ladders = RUNGS.map((rung, index) => (
    <div
      key={rung.token}
      style={{
        position: "relative",
        height: `${0.6 + index * 0.22}rem`,
        background: "var(--primary)",
        borderRadius: "var(--r-xs)",
        marginBottom: "var(--s2)",
      }}
    />
  ))
  return <div style={{ display: "flex", flexDirection: "column" }}>{ladders}</div>
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
  title: "Compositions/Style Lab/Tokens/Depth",
  parameters: { layout: "fullscreen" },
}

/** The nine-rung z-index ladder, drawn as a stair step. The owner of
    each rung is named on the right: most rungs are owned by the
    document, but the dock is owned by the viewport and the dropdown
    is owned by the control that opened it. */
export function ZIndex(): ReactNode {
  return (
    <Page>
      <Section
        variant="screen"
        title="Z-index ladder"
        note="Nine rungs, ten apart. Before this there were eight bare numbers in eight files and no way to read them. The rungs are named for what sits on them, never for how high they are, so a consumer states a role and the ladder decides the number. Two overlays are never open in the same stacking context by accident; which one paints on top is decided by DOM order, which is the same thing as 'whichever was opened last'."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "1fr 12rem",
            gap: "var(--s6)",
          }}
        >
          <div style={{ display: "flex", flexDirection: "column" }}>
            {RUNGS.map((rung, index) => (
              <ZRow key={rung.token} rung={rung} index={index} />
            ))}
          </div>
          <div
            style={{
              padding: "var(--s5)",
              border: "var(--hairline) solid var(--border)",
              borderRadius: "var(--r-md)",
              background: "var(--card)",
              alignSelf: "start",
              position: "sticky",
              top: "var(--s5)",
            }}
          >
            <span
              style={{
                fontFamily: "var(--font-data)",
                fontSize: "var(--t-micro)",
                letterSpacing: "var(--tracking-label)",
                color: "var(--text-faint)",
                textTransform: "uppercase",
                display: "block",
                marginBottom: "var(--s4)",
              }}
            >
              Ladder
            </span>
            <ZLadder />
          </div>
        </div>
      </Section>
    </Page>
  )
}

/** The five shadow tokens, plus the focus-ring recipe. Each tile shows
    the token's actual shadow on a small block, so the recipe is visible
    without any interaction. */
export function Shadows(): ReactNode {
  return (
    <Page>
      <Section
        variant="screen"
        title="Shadows"
        note="Six shadow tokens, mode-dependent: the recipe is cast in the theme's own ink so a warm floor gets a warm shadow; the dark ones are black, because on a dark floor a tinted shadow reads as a smudge. Offset and blur, never a zero-offset halo. --shadow-focus-ring and --shadow-focus-ring-inset are the focus recipes; the first sits on a control's own edge, the second turns inward where the box is flush against a scroller."
      >
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(16rem, 1fr))",
            gap: "var(--s4)",
          }}
        >
          {SHADOWS.map((step) => (
            <ShadowTile key={step.token} step={step} />
          ))}
        </div>
      </Section>
    </Page>
  )
}

/** The two motion tokens: one easing curve, one duration. A new motion
    recipe has to use them both, or the operator sees one button drift
    and the next snap. */
export function Motion(): ReactNode {
  return (
    <Page>
      <Section
        variant="screen"
        title="Motion"
        note="Two tokens. One easing curve, one duration. The curve is biased toward the end of the motion, so a control that opens reads as if it had weight, and a control that closes reads as if it was set down. The duration is 180ms, short enough to feel instantaneous and long enough to read as deliberate."
      >
        <div style={{ display: "flex", flexDirection: "column" }}>
          {MOTION.map((motion) => (
            <div
              key={motion.token}
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
                  {motion.token}
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
                  {motion.value}
                </span>
              </div>
              <span
                style={{
                  fontFamily: "var(--font-ui)",
                  fontSize: "var(--t-sm)",
                  color: "var(--text-muted)",
                }}
              >
                {motion.role}
              </span>
            </div>
          ))}
        </div>
      </Section>
    </Page>
  )
}
