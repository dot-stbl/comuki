import type { CSSProperties } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { BrandIcon, type BrandIconSize } from "./brand-icon"
import { BRAND_IDS, BRAND_MARKS } from "./brand-marks"

const meta: Meta<typeof BrandIcon> = {
  title: "UI Kit/Brand/BrandIcon",
  component: BrandIcon,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof BrandIcon>

const SIZES: BrandIconSize[] = ["xs", "sm", "md", "lg"]

const cell: CSSProperties = {
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  minWidth: "var(--s8)",
}

const caption: CSSProperties = {
  fontFamily: "var(--font-data)",
  fontSize: "var(--t-micro)",
  color: "var(--text-faint)",
}

/**
 * The whole set at every step of the icon scale.
 *
 * This grid is the one place the trade is visible: at `xs` — the table-row step
 * — the Docker whale and the GitLab tanuki have closed into shapes, while
 * GitHub, Jira and the Comuki container still read. That is the argument for
 * the accessible name and the tooltip being part of the contract rather than a
 * courtesy: a mark this small is a *recognition* cue for someone who already
 * knows it, and nothing at all for someone who does not.
 */
export const AllMarksAtEverySize: Story = {
  render: () => (
    <table style={{ borderCollapse: "collapse" }}>
      <thead>
        <tr>
          <th style={{ ...caption, textAlign: "start", padding: "var(--s3)" }}>
            mark
          </th>
          {SIZES.map((size) => (
            <th key={size} style={{ ...caption, padding: "var(--s3)" }}>
              {size}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {BRAND_IDS.map((brand) => (
          <tr key={brand}>
            <td
              style={{
                ...caption,
                color: "var(--text-muted)",
                padding: "var(--s3)",
                whiteSpace: "nowrap",
              }}
            >
              {BRAND_MARKS[brand].title}
            </td>
            {SIZES.map((size) => (
              <td key={size} style={{ padding: "var(--s3)" }}>
                <span style={cell}>
                  <BrandIcon brand={brand} size={size} />
                </span>
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  ),
}

/** Every mark takes `currentColor`, so it belongs to whatever it sits in. */
export const TakesCurrentColor: Story = {
  render: () => (
    <div style={{ display: "flex", gap: "var(--s5)", alignItems: "center" }}>
      {["var(--text)", "var(--text-faint)", "var(--primary)"].map((tone) => (
        <span
          key={tone}
          style={{
            color: tone,
            display: "inline-flex",
            gap: "var(--s3)",
            alignItems: "center",
          }}
        >
          {BRAND_IDS.map((brand) => (
            <BrandIcon key={brand} brand={brand} size="lg" />
          ))}
        </span>
      ))}
    </div>
  ),
}

/**
 * The name travels with the mark.
 *
 * `label` defaults to the vendor's own spelling; a surface that speaks in the
 * product's lower-case voice passes its own word instead. `label={null}` is the
 * only way to switch the name off, and it is correct only when the control
 * around the mark already carries one.
 */
export const NamedInTheSurfaceVoice: Story = {
  render: () => (
    <div
      style={{
        display: "flex",
        gap: "var(--s5)",
        alignItems: "center",
        ...caption,
        color: "var(--text-muted)",
      }}
    >
      <BrandIcon brand="github" size="md" />
      <span style={{ display: "inline-flex", gap: "var(--s2)" }}>
        <BrandIcon brand="github" size="md" label="github" />
        github
      </span>
      <span style={{ display: "inline-flex", gap: "var(--s2)" }}>
        <BrandIcon brand="github" size="md" label={null} />
        named by the words beside it
      </span>
    </div>
  ),
}

/** The default, for the controls table. */
export const Default: Story = {
  args: { brand: "github", size: "md" },
}
