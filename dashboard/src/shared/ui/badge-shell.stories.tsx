import type { CSSProperties, ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { Check, Hourglass, TriangleAlert } from "lucide-react"

import { badgeShell, type BadgeShellSize } from "./badge-shell"

/**
 * The shell has no component of its own — it is a class recipe, the way
 * `buttonClass` is — so the stories need a stand-in that does what a real
 * caller does: take the shell's classes and add its own colour on top.
 *
 * `edge` is the caller's half of the contract. The shell reads
 * `--badge-edge` and never declares it, so the hairline's colour arrives from
 * the call site and can never lose a cascade race to the shell's own rule.
 */
function Mark({
  size,
  edge,
  hue,
  children,
}: {
  size?: BadgeShellSize
  edge?: string
  hue?: string
  children: ReactNode
}) {
  return (
    <span
      className={badgeShell({ size })}
      style={
        {
          "--badge-edge": edge ?? "var(--border)",
          color: hue ?? "var(--text)",
          background: hue
            ? `color-mix(in oklab, ${hue} 10%, transparent)`
            : "transparent",
        } as CSSProperties
      }
    >
      {children}
    </span>
  )
}

const meta: Meta<typeof Mark> = {
  title: "UI Kit/Data/Badge Shell",
  component: Mark,
  parameters: { layout: "centered" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof Mark>

const row: CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: "var(--s4)",
  flexWrap: "wrap",
}

/**
 * The table step, and the one nine domain families sit at. It is sized so a
 * mark inside a 32px row is never the tallest thing in it; the icon follows the
 * step without the call site saying so.
 */
export const Small: Story = {
  render: () => (
    <div style={row}>
      <Mark size="sm">queued</Mark>
      <Mark size="sm" hue="var(--st-success)">
        <Check aria-hidden="true" />
        passed
      </Mark>
      <Mark size="sm" hue="var(--st-waiting)">
        <Hourglass aria-hidden="true" />
        waiting on a human
      </Mark>
    </div>
  ),
}

/**
 * The step `StatusBadge` takes when it is standing on its own rather than
 * riding a column — a larger gap, a real vertical padding, the body's small
 * size and an icon a step up to match.
 */
export const Medium: Story = {
  render: () => (
    <div style={row}>
      <Mark size="md">queued</Mark>
      <Mark size="md" hue="var(--st-success)">
        <Check aria-hidden="true" />
        passed
      </Mark>
      <Mark size="md" hue="var(--st-waiting)">
        <Hourglass aria-hidden="true" />
        waiting on a human
      </Mark>
    </div>
  ),
}

/**
 * The caller's half: the edge.
 *
 * The shell writes `border-color: var(--badge-edge, transparent)` and stops
 * there. A caller that wants the default hairline declares
 * `--badge-edge: var(--border)`; a state that wants to be seen before it is
 * read declares a stronger mix of its own hue; the two borderless families
 * declare nothing and get the fallback. All three are on this row, and none of
 * them is an override — the property is declared in exactly one place per
 * badge, so which stylesheet the bundler emitted first cannot change what they
 * look like.
 */
export const OwnEdge: Story = {
  render: () => (
    <div style={row}>
      <Mark>default hairline</Mark>
      <Mark
        hue="var(--st-failed)"
        edge="color-mix(in oklab, var(--st-failed) 40%, transparent)"
      >
        <TriangleAlert aria-hidden="true" />
        escalated edge
      </Mark>
      <Mark hue="var(--st-queued)" edge="transparent">
        no edge at all
      </Mark>
    </div>
  ),
}
