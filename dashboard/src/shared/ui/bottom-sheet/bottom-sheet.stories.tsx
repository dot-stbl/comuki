import { useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"

import { Button } from "../button"
import { BottomSheet } from "./bottom-sheet"

/**
 * A modal panel docked to the bottom of the window. The two states a caller
 * names are the two stories: the depth the operator dragged, and the whole
 * window. Both carry toolbar controls and a body long enough that the scroll
 * contract is visible — the bar stays put and the content scrolls inside the
 * sheet, whatever depth it is at.
 */
const meta: Meta<typeof BottomSheet> = {
  title: "UI Kit/Overlays/BottomSheet",
  component: BottomSheet,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof BottomSheet>

/** Enough lines that the sheet has to scroll them, not the window. */
const LINES = Array.from({ length: 60 }, (_, index) => ({
  worker: `wkr-${String(index + 1).padStart(3, "0")}`,
  stage: index % 9,
  ms: (index * 137) % 900 + 60,
}))

/**
 * The port the content opens. The sheet's own body owns no scrollbar — it
 * hands the port to what it holds, the same way a full screen's content
 * region does — so the demo opens one, and the drag edge resizes around it.
 */
function Log() {
  return (
    <div
      data-test="story-sheet-log"
      style={{
        flex: "1 1 0",
        minHeight: 0,
        overflowY: "auto",
        display: "flex",
        flexDirection: "column",
        gap: "var(--s2)",
        paddingBlock: "var(--s5)",
      }}
    >
      {LINES.map((line) => (
        <div
          key={line.worker}
          style={{
            fontFamily: "var(--font-data)",
            fontSize: "var(--t-xs)",
            fontVariantNumeric: "tabular-nums",
            color: "var(--text-muted)",
          }}
        >
          {line.worker} · stage {line.stage} · {line.ms}ms
        </div>
      ))}
    </div>
  )
}

function Sheet({ initiallyExpanded = false }: { initiallyExpanded?: boolean }) {
  const [open, setOpen] = useState(true)
  const [expanded, setExpanded] = useState(initiallyExpanded)

  return (
    <>
      {open ? null : (
        <Button data-test="story-sheet-open" onClick={() => setOpen(true)}>
          Open the run log
        </Button>
      )}
      <BottomSheet
        open={open}
        onOpenChange={setOpen}
        title="Run log"
        storageKey="storybook.bottom-sheet"
        expanded={expanded}
        onExpandedChange={setExpanded}
        toolbar={
          <>
            <Button variant="ghost" size="sm">
              Follow
            </Button>
            <Button variant="ghost" size="sm">
              Clear
            </Button>
          </>
        }
      >
        <Log />
      </BottomSheet>
    </>
  )
}

/** The dragged depth: pull the top edge, and the depth is remembered. */
export const Panel: Story = {
  render: () => <Sheet />,
}

/** Filling the window: the edge retires, and the sheet comes back out at the
 *  depth it left rather than the whole window it showed. */
export const FullWindow: Story = {
  render: () => <Sheet initiallyExpanded />,
}
