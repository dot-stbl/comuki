import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import type { RunStatus, WorkItem } from "@/domains/runs/model/types"
import { orderedItems } from "@/domains/runs/model/work-items"
import { toRunSummary } from "@/domains/runs/api/mappers"
import { RUNS_SEED } from "@/shared/api/mock"

import { RunGraph } from "./run-graph"

/**
 * The graph fills whatever it is given, so every story hands it a definite
 * height — which is also the contract a screen has to honour.
 */
function Board({
  items,
  current,
}: {
  items: WorkItem[]
  current?: string
}) {
  const [selected, setSelected] = useState<string | undefined>(current)
  return (
    <div style={{ height: "26rem" }}>
      <RunGraph
        items={items}
        current={current}
        selected={selected}
        onSelect={setSelected}
      />
    </div>
  )
}

function work(
  id: string,
  profile: string,
  label: string,
  status: RunStatus,
  dependsOn: string[] = []
): WorkItem {
  return { id, profile, label, status, dependsOn }
}

const seeded = RUNS_SEED.map(toRunSummary)

/** The brain closed this one without planning: three items, one lane. */
const chain = seeded.find((run) => run.workItems.length === 3) ?? seeded[0]

/** Four lanes off one plan — the widest branch written by hand. */
const branching =
  seeded.find((run) => run.id === "2a6f1c33") ?? seeded[0]

/** Forty-two items, four lanes wide and eight deep. */
const large = seeded.reduce((widest, run) =>
  run.workItems.length > widest.workItems.length ? run : widest
)

/** A run that died at the third step; everything behind it is going nowhere. */
const failed = seeded.find((run) => run.id === "9d72b5f0") ?? seeded[0]

/** A run holding at a human gate in the middle of its plan. */
const waiting = seeded.find((run) => run.id === "5b1d7e40") ?? seeded[0]

/**
 * A plan that joins back to something two columns behind it. The layered form
 * cannot draw this edge by adjacency, so the node has to say so.
 */
const longEdge: WorkItem[] = orderedItems([
  work("w1", "explorer", "снять карту модуля", "success"),
  work("w2", "planner", "разбить задачу на два лейна", "success", ["w1"]),
  work("w3", "implementer", "вынести ретраи в слой", "success", ["w2"]),
  work("w4", "implementer", "прокинуть тайм-аут", "success", ["w2"]),
  work("w5", "implementer", "убрать дубли в очереди", "running", ["w3"]),
  // Reaches back past the column beside it, to the two items at depth 2.
  work("w6", "reviewer", "сверить с контрактом", "queued", ["w3", "w4", "w5"]),
  work("w7", "tester", "прогнать смоук", "queued", ["w6"]),
])

const meta: Meta<typeof RunGraph> = {
  title: "Runs/Run graph",
  component: RunGraph,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof RunGraph>

/** A plan the brain closed in three items: one lane, no branch, no marks. */
export const SingleChain: Story = {
  render: () => <Board items={chain.workItems} current={chain.current} />,
}

/** Four implementers off one plan. The branching column names its own width. */
export const Branching: Story = {
  render: () => (
    <Board items={branching.workItems} current={branching.current} />
  ),
}

/**
 * A failure mid-plan. The failed item takes area, and everything queued behind
 * it is drawn as blocked — a dashed border and a word, because its own status
 * still says `queued` and that is true but misleading.
 */
export const FailedAndBlocked: Story = {
  render: () => <Board items={failed.workItems} current={failed.current} />,
}

/** A human gate in the middle of the graph, holding everything behind it. */
export const WaitingOnAHuman: Story = {
  render: () => <Board items={waiting.workItems} current={waiting.current} />,
}

/**
 * The named risk: a dependency that skips a column. The node marks itself,
 * hovering or focusing it lights what it waits on, and the inspector beside it
 * lists the same set as controls.
 */
export const LongDependency: Story = {
  render: () => <Board items={longEdge} current="w5" />,
}

/**
 * Forty-two items. The graph scrolls sideways rather than shrinking a node,
 * and a column that runs out of room scrolls inside itself.
 */
export const LargeRun: Story = {
  render: () => <Board items={large.workItems} current={large.current} />,
}

/** The plan preview an approval draws: static, no selection, capped height. */
export const StaticPreview: Story = {
  render: () => (
    <RunGraph
      items={branching.workItems}
      current={branching.current}
      fit="content"
      label="Plan — work item graph"
    />
  ),
}

/** No plan at all. The detail screen answers this at screen scale; a graph
 *  handed nothing says so rather than rendering a blank box. */
export const NoPlan: Story = {
  render: () => <Board items={[]} />,
}
