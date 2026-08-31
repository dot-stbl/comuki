import type { ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import type {
  EvalDelta,
  KnowledgeKind,
  RuleKind,
} from "@/domains/knowledge/model/types"

import {
  EvalDeltaMark,
  KindMark,
  PinnedMark,
  RuleKindMark,
} from "./knowledge-badges"

const KINDS: KnowledgeKind[] = ["rule", "doc", "skill"]
const RULE_KINDS: RuleKind[] = ["hard", "soft"]
const DELTAS: EvalDelta[] = ["+", "-", "="]

function Row({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        display: "flex",
        flexWrap: "wrap",
        gap: "var(--s4)",
        alignItems: "center",
        padding: "var(--s6)",
      }}
    >
      {children}
    </div>
  )
}

const meta: Meta<typeof KindMark> = {
  title: "Knowledge/Badges",
  component: KindMark,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof KindMark>

/**
 * Which shelf an entry sits on. Colourless on purpose: a doc is not a state,
 * and saturation on this screen belongs to the readings that carry a
 * consequence. The icon is the whole distinction.
 */
export const EntryKinds: Story = {
  render: () => (
    <Row>
      {KINDS.map((kind) => (
        <KindMark key={kind} kind={kind} />
      ))}
    </Row>
  ),
}

/**
 * Hard stops the swarm and takes the product's hold hue; soft advises and takes
 * the least saturated of the six. The lock says it a second way.
 */
export const RuleKinds: Story = {
  render: () => (
    <Row>
      {RULE_KINDS.map((ruleKind) => (
        <RuleKindMark key={ruleKind} ruleKind={ruleKind} />
      ))}
    </Row>
  ),
}

/** What a rule edit did to one golden task — arrow and word, never a bare `+`. */
export const EvalDeltas: Story = {
  render: () => (
    <Row>
      {DELTAS.map((delta) => (
        <EvalDeltaMark key={delta} delta={delta} />
      ))}
    </Row>
  ),
}

/**
 * Every mark carries a silhouette as well as a hue, so the whole set survives
 * greyscale — which is the claim the two-channel rule makes and this story is
 * the check on it.
 */
export const InGreyscale: Story = {
  render: () => (
    <div style={{ filter: "grayscale(1)" }}>
      <Row>
        {KINDS.map((kind) => (
          <KindMark key={kind} kind={kind} />
        ))}
        {RULE_KINDS.map((ruleKind) => (
          <RuleKindMark key={ruleKind} ruleKind={ruleKind} />
        ))}
        {DELTAS.map((delta) => (
          <EvalDeltaMark key={delta} delta={delta} />
        ))}
      </Row>
    </div>
  ),
}

/** The one accent on the screen: being pinned is why reproducibility reads 100%. */
export const Pinned: Story = {
  render: () => (
    <Row>
      <PinnedMark />
      <PinnedMark revision="a1b9e0" />
    </Row>
  ),
}
