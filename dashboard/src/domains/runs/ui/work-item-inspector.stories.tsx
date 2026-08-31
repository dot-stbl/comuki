import type { Meta, StoryObj } from "@storybook/react"

import { toRunSummary, toWorkItemInspector } from "@/domains/runs/api/mappers"
import { orderedItems, planGraph } from "@/domains/runs/model/work-items"
import { RUNS_SEED } from "@/shared/api/mock"

import { WorkItemInspectorPanel } from "./work-item-inspector"

/** The hand-written run with a real diff, a real gate and a real log. */
const seed = RUNS_SEED.find((run) => run.id === "8f3c2a91") ?? RUNS_SEED[0]
const run = toRunSummary(seed)
const items = orderedItems(run.workItems)
const graph = planGraph(items)

function panelFor(itemId: string) {
  const item = items.find((entry) => entry.id === itemId) ?? items[0]
  return (
    <div style={{ height: "34rem" }}>
      <WorkItemInspectorPanel
        item={item}
        index={items.indexOf(item) + 1}
        total={items.length}
        info={toWorkItemInspector(seed, item.id)}
        waitsOn={graph.dependencies.get(item.id) ?? []}
        onSelect={() => {}}
      />
    </div>
  )
}

const meta: Meta<typeof WorkItemInspectorPanel> = {
  title: "Runs/Work item inspector",
  component: WorkItemInspectorPanel,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof WorkItemInspectorPanel>

/** An implementer mid-run: a full gate and a diff as its output. */
export const Implementing: Story = {
  render: () => panelFor("w4"),
}

/** The first item in the plan — it waits on nothing, and says so. */
export const PlanRoot: Story = {
  render: () => panelFor("w1"),
}

/** A reviewer joining two lanes: two dependencies, both one column back. */
export const JoiningTwoLanes: Story = {
  render: () => panelFor("w5"),
}

/** An item that has not started: no env, no figures, an honest log. */
export const NotStarted: Story = {
  render: () => panelFor("w8"),
}
