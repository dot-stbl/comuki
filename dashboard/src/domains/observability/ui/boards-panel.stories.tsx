import type { Meta, StoryObj } from "@storybook/react"

import type { ObservabilitySnapshot } from "@/domains/observability/model/types"
import { BoardsPanel } from "@/domains/observability/ui/boards-panel"
import { ConnectGuide } from "@/domains/observability/ui/connect-guide"
import {
  OBSERVABILITY_SEED,
  OBSERVABILITY_UNCONFIGURED_SEED,
} from "@/shared/api/mock/observability.seed"

/**
 * The section as the page composes it: the links, then how to get them. Both
 * halves, because the second is what makes this a section rather than a stub —
 * an operator opening a fresh installation finds three boards they cannot reach
 * and needs to be told whose job that is.
 */
function Section({ snapshot }: { snapshot: ObservabilitySnapshot }) {
  const noBoards = snapshot.boards.every((board) => board.url === null)

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: "var(--s8)",
        padding: "var(--s6)",
      }}
    >
      <BoardsPanel boards={snapshot.boards} />
      <ConnectGuide
        grafana={snapshot.grafana}
        boardsRepo={snapshot.boardsRepo}
        noBoards={noBoards}
      />
    </div>
  )
}

const meta: Meta<typeof BoardsPanel> = {
  title: "Observability/Boards",
  component: BoardsPanel,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof BoardsPanel>

/**
 * A platform where somebody did the work. Two boards open in grafana; the third
 * exists in our repository and has not been imported here, which is a next step
 * rather than a failure and reads as one.
 */
export const Configured: Story = {
  render: () => <Section snapshot={OBSERVABILITY_SEED} />,
}

/**
 * The state every new installation opens in: no grafana at all. The three
 * entries still list — their definitions are ours and exist whether or not
 * anyone has imported them — and the guide underneath is the whole page.
 */
export const NoBoards: Story = {
  render: () => <Section snapshot={OBSERVABILITY_UNCONFIGURED_SEED} />,
}

/**
 * A grafana that is configured with nothing imported into it. A different
 * problem from having no grafana, and it takes a different sentence.
 */
export const ConfiguredButEmpty: Story = {
  render: () => (
    <Section
      snapshot={{
        ...OBSERVABILITY_SEED,
        boards: OBSERVABILITY_SEED.boards.map((board) => ({
          ...board,
          url: null,
        })),
      }}
    />
  ),
}
