import type { Meta, StoryObj } from "@storybook/react"

import type { EvalCase } from "@/domains/knowledge/model/types"

import { EvalHarnessTable } from "./eval-harness-table"

const CASES: EvalCase[] = [
  { task: "idempotent-webhook", before: "fail", after: "pass", delta: "+" },
  { task: "jwt-rotation", before: "pass", after: "pass", delta: "=" },
  { task: "table-virtualize", before: "fail", after: "pass", delta: "+" },
  { task: "theme-migrate", before: "pass", after: "fail", delta: "-" },
]

const meta: Meta<typeof EvalHarnessTable> = {
  title: "Knowledge/Eval harness",
  component: EvalHarnessTable,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
  decorators: [
    (Story) => (
      <div style={{ padding: "var(--s6)" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof EvalHarnessTable>

/**
 * Four golden tasks, before and after the current rule edit. The delta column
 * says the difference in words rather than leaving the operator to read two
 * badges and subtract them — and sorting it puts the regression first.
 */
export const Harness: Story = {
  render: () => <EvalHarnessTable cases={CASES} />,
}

/**
 * A revision nothing has been run against yet. The empty band says which of the
 * two nothings it is: no harness run, rather than a harness that found nothing.
 */
export const NotRunYet: Story = {
  render: () => <EvalHarnessTable cases={[]} />,
}
