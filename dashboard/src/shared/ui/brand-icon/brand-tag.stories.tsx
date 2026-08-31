import type { CSSProperties } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { BrandTag } from "./brand-tag"

const meta: Meta<typeof BrandTag> = {
  title: "UI Kit/Brand/BrandTag",
  component: BrandTag,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof BrandTag>

const row: CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: "var(--s5)",
}

const head: CSSProperties = {
  fontFamily: "var(--font-data)",
  fontSize: "var(--t-micro)",
  letterSpacing: "var(--tracking-data)",
  color: "var(--text-faint)",
}

/**
 * A provider column as the two screens draw it.
 *
 * Four marks and one word, side by side — which is the honest shape of this
 * rule rather than an embarrassment about it. Yandex publishes no monochrome
 * Tracker mark and the product glyph is carried by its colour, so the column
 * says the words rather than showing a trademark somebody drew from memory. A
 * ragged column is the correct rendering of a ragged fact.
 */
export const AProviderColumn: Story = {
  render: () => (
    <div style={{ display: "grid", gap: "var(--s3)" }}>
      <span style={head}>provider</span>
      <span style={row}>
        <BrandTag brand="github" label="github" />
        <BrandTag brand="gitlab" label="gitlab" />
        <BrandTag brand="jira" label="jira" />
        <BrandTag brand={null} label="yandex tracker" />
        <BrandTag brand="comuki" label="native" />
      </span>
    </div>
  ),
}

/** The compute registry's two backends, at the same step. */
export const AKindColumn: Story = {
  render: () => (
    <div style={{ display: "grid", gap: "var(--s3)" }}>
      <span style={head}>kind</span>
      <span style={row}>
        <BrandTag brand="docker" label="docker" />
        <BrandTag brand="kubernetes" label="kubernetes" />
      </span>
    </div>
  ),
}

/**
 * The fallback is not a failure state.
 *
 * `brand={null}` is a first-class answer, and the label is required in both
 * branches for the same reason: it is the mark's accessible name where there is
 * a mark, and the cell's own text where there is not.
 */
export const SpelledOut: Story = {
  args: { brand: null, label: "yandex tracker" },
}

/** The default, for the controls table. */
export const Default: Story = {
  args: { brand: "github", label: "github" },
}
