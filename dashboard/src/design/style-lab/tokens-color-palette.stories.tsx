import type { ReactNode } from "react"

import { Section } from "@/shared/ui"

/* The primitives every theme states. Order matches the comment block in
   `app/styles/themes.css`: structural neutrals, then the accent and its
   foreground, then the seven status hues. Kept here in the same order so
   a reader can compare a swatch to the source declaration without jumping.

   The four status hues picked out below (running / queued / failed /
   success) are the four that span the workload vocabulary end-to-end; the
   other three (waiting, escalated, cancelled) sit between them on the
   lightness scale and the styles.css block holds the proof. */
interface PrimitiveRow {
  readonly token: string
  readonly label: string
  readonly cssVar: string
}

const STRUCTURAL: readonly PrimitiveRow[] = [
  { token: "--background", label: "floor", cssVar: "var(--background)" },
  { token: "--rail", label: "rail", cssVar: "var(--rail)" },
  { token: "--lane", label: "lane", cssVar: "var(--lane)" },
  { token: "--lane-alt", label: "lane-alt", cssVar: "var(--lane-alt)" },
  { token: "--card", label: "card", cssVar: "var(--card)" },
  { token: "--popover", label: "popover", cssVar: "var(--popover)" },
  { token: "--accent", label: "accent", cssVar: "var(--accent)" },
  { token: "--border", label: "rule", cssVar: "var(--border)" },
  { token: "--border-strong", label: "rule-strong", cssVar: "var(--border-strong)" },
  { token: "--foreground", label: "text", cssVar: "var(--foreground)" },
  { token: "--muted-foreground", label: "text-muted", cssVar: "var(--muted-foreground)" },
  { token: "--text-faint", label: "text-faint", cssVar: "var(--text-faint)" },
  { token: "--primary", label: "accent-fill", cssVar: "var(--primary)" },
  { token: "--destructive", label: "destructive", cssVar: "var(--destructive)" },
]

const STATUS_HUES: readonly PrimitiveRow[] = [
  { token: "--st-running", label: "running", cssVar: "var(--st-running)" },
  { token: "--st-queued", label: "queued", cssVar: "var(--st-queued)" },
  { token: "--st-waiting", label: "waiting", cssVar: "var(--st-waiting)" },
  { token: "--st-escalated", label: "escalated", cssVar: "var(--st-escalated)" },
  { token: "--st-failed", label: "failed", cssVar: "var(--st-failed)" },
  { token: "--st-success", label: "success", cssVar: "var(--st-success)" },
  { token: "--st-cancelled", label: "cancelled", cssVar: "var(--st-cancelled)" },
]

/* The seven registered themes, in the order they appear in `themes.css`.
   `--preview-*` tokens read each theme's own primitives regardless of
   which theme is active — that's the whole point of the preview block, and
   it's what makes this specimen legible inside any theme/mode pair. */
interface ThemePreview {
  readonly id: string
  readonly name: string
  readonly tagline: string
}

const THEMES: readonly ThemePreview[] = [
  { id: "dichromat-deck", name: "dichromat-deck", tagline: "protanope-safe — colourless chrome, lightness axis" },
  { id: "graphite", name: "graphite", tagline: "cold near-black, cyan accent" },
  { id: "dockside", name: "dockside", tagline: "warm umber room, copper accent" },
  { id: "blueprint", name: "blueprint", tagline: "cyanotype chrome, ice accent" },
  { id: "bureau", name: "bureau", tagline: "paper chrome, stamp-violet accent" },
  { id: "aperture", name: "aperture", tagline: "depth inverted — chrome up, data down" },
  { id: "dispatcher", name: "dispatcher", tagline: "original board, turquoise accent" },
]

interface SwatchProps {
  readonly row: PrimitiveRow
}

function Swatch({ row }: SwatchProps): ReactNode {
  /* Pick a legible label colour for whatever fill the swatch is wearing.
     The seven status hues are fills, the four text greys are foregrounds
     painted on chrome, and the structural neutrals are surfaces. */
  const isFill =
    row.token === "--primary" ||
    row.token === "--destructive" ||
    row.token.startsWith("--st-")
  const isText =
    row.token === "--foreground" ||
    row.token === "--muted-foreground" ||
    row.token === "--text-faint"
  const labelColor = isFill
    ? "var(--primary-foreground)"
    : isText
      ? "var(--background)"
      : "var(--foreground)"
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s2)",
        padding: "var(--s3)",
        background: row.cssVar,
        border: "var(--hairline) solid var(--border)",
        borderRadius: "var(--r-sm)",
        minHeight: "5rem",
        justifyContent: "space-between",
      }}
    >
      <span
        style={{
          fontFamily: "var(--font-data)",
          fontSize: "var(--t-micro)",
          letterSpacing: "var(--tracking-data)",
          color: labelColor,
          opacity: 0.75,
        }}
      >
        {row.token}
      </span>
      <span
        style={{
          fontFamily: "var(--font-ui)",
          fontSize: "var(--t-xs)",
          fontWeight: "var(--fw-medium)",
          color: labelColor,
        }}
      >
        {row.label}
      </span>
    </div>
  )
}

interface PreviewSwatchProps {
  readonly token: string
  readonly label: string
}

function PreviewSwatch({ token, label }: PreviewSwatchProps): ReactNode {
  /* Read each theme's own primitive via the `--preview-{theme}-{label}`
     bridge. The bridge is theme-independent: it holds each theme's values
     keyed by name, so the swatch stays accurate whatever theme the toolbar
     has the product in. */
  const cssVar = `var(${token})`
  const isFill = token.includes("running") || token.includes("failed") || token.includes("accent")
  const labelColor = isFill ? "var(--primary-foreground)" : "var(--foreground)"
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s2)",
        padding: "var(--s3)",
        background: cssVar,
        border: "var(--hairline) solid var(--border)",
        borderRadius: "var(--r-sm)",
        minHeight: "4.5rem",
        justifyContent: "flex-end",
      }}
    >
      <span
        style={{
          fontFamily: "var(--font-ui)",
          fontSize: "var(--t-micro)",
          fontWeight: "var(--fw-medium)",
          color: labelColor,
        }}
      >
        {label}
      </span>
    </div>
  )
}

interface ThemeCardProps {
  readonly theme: ThemePreview
  readonly mode: "light" | "dark"
}

function ThemeCard({ theme, mode }: ThemeCardProps): ReactNode {
  const modeSuffix = mode === "dark" ? "-dark" : ""
  const previewSlots: ReadonlyArray<{ readonly token: string; readonly label: string }> = [
    { token: `--preview-${theme.id}-floor${modeSuffix}`, label: "floor" },
    { token: `--preview-${theme.id}-rail${modeSuffix}`, label: "rail" },
    { token: `--preview-${theme.id}-rule${modeSuffix}`, label: "rule" },
    { token: `--preview-${theme.id}-text${modeSuffix}`, label: "text" },
    { token: `--preview-${theme.id}-accent${modeSuffix}`, label: "accent" },
    { token: `--preview-${theme.id}-running${modeSuffix}`, label: "running" },
    { token: `--preview-${theme.id}-failed${modeSuffix}`, label: "failed" },
  ]
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s4)",
        padding: "var(--s5)",
        border: "var(--hairline) solid var(--border)",
        borderRadius: "var(--r-md)",
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
            fontSize: "var(--t-body)",
            fontWeight: "var(--fw-semibold)",
            color: "var(--text)",
          }}
        >
          {theme.name}
        </span>
        <span
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-micro)",
            letterSpacing: "var(--tracking-label)",
            color: "var(--text-faint)",
          }}
        >
          {mode}
        </span>
      </header>
      <span
        style={{
          fontFamily: "var(--font-ui)",
          fontSize: "var(--t-micro)",
          color: "var(--text-faint)",
        }}
      >
        {theme.tagline}
      </span>
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(7, minmax(0, 1fr))",
          gap: "var(--s2)",
        }}
      >
        {previewSlots.map((slot) => (
          <PreviewSwatch key={slot.token} token={slot.token} label={slot.label} />
        ))}
      </div>
    </div>
  )
}

function ActiveThemeStrip(): ReactNode {
  /* The primitives the active theme is currently holding, read straight off
     the document via `var()` references. The toolbar's theme/mode selectors
     are what change them, so this strip is the live readout of whatever the
     operator picked. */
  const tokens = [...STRUCTURAL, ...STATUS_HUES]
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "repeat(auto-fill, minmax(7.5rem, 1fr))",
        gap: "var(--s2)",
      }}
    >
      {tokens.map((row) => (
        <Swatch key={row.token} row={row} />
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
  title: "Compositions/Style Lab/Tokens/Color Palette",
  parameters: { layout: "fullscreen" },
}

/** The palette the toolbar is currently on — primitives and status hues read
    off the live document, so flipping the theme or mode selectors in the
    toolbar repaints this strip. */
export function ActiveTheme(): ReactNode {
  return (
    <Page>
      <Section
        variant="screen"
        title="Active theme"
        note="What the toolbar's selectors are showing right now. Flip the theme or mode above and the swatches repaint — the structural rows are the primitives, the seven bottom rows are the status hues the workload vocabulary speaks."
      >
        <ActiveThemeStrip />
      </Section>
    </Page>
  )
}

/** Every registered theme, in both modes. The `--preview-*` block holds each
    theme's own primitives readable from any theme context, so this gallery
    is legible inside whichever theme the operator picked in the toolbar. */
export function AllThemes(): ReactNode {
  return (
    <Page>
      <Section
        variant="screen"
        title="Theme gallery"
        note="Seven themes × two modes. Each row shows the floor, the rail chrome, a rule, the text on it, the accent fill, and the two endpoints of the status hue range — running and failed — because those are the two the vocabulary guarantees lightness distance between. The preview tokens are theme-independent, so this reads identically whatever the toolbar holds."
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--s6)" }}>
          {THEMES.flatMap((theme) => [
            <ThemeCard key={`${theme.id}-light`} theme={theme} mode="light" />,
            <ThemeCard key={`${theme.id}-dark`} theme={theme} mode="dark" />,
          ])}
        </div>
      </Section>
    </Page>
  )
}
