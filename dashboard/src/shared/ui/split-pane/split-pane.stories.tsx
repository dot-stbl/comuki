import { useRef, useState } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import type { PanelImperativeHandle } from "react-resizable-panels"

import { Button } from "../button"
import { SplitPane, SplitPanel, SplitSeparator } from "./split-pane"

const meta: Meta<typeof SplitPane> = {
  title: "UI Kit/Layout/SplitPane",
  component: SplitPane,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof SplitPane>

const fill = {
  height: "100%",
  display: "grid",
  placeItems: "center",
  fontFamily: "var(--font-data)",
  fontSize: "var(--t-sm)",
  color: "var(--muted-foreground)",
  background: "var(--lane)",
}

function Frame({ children }: { children: React.ReactNode }) {
  return <div style={{ height: "28rem" }}>{children}</div>
}

/** The board over the working surface: drag the rule between them. */
export const Vertical: Story = {
  render: () => (
    <Frame>
      <SplitPane orientation="vertical">
        <SplitPanel id="board" defaultSize="62%" minSize="18%">
          <div style={fill}>board</div>
        </SplitPanel>
        <SplitSeparator orientation="vertical" aria-label="Resize the board" />
        <SplitPanel id="surface" minSize="25%">
          <div style={fill}>table</div>
        </SplitPanel>
      </SplitPane>
    </Frame>
  ),
}

function CollapsibleDemo() {
  const board = useRef<PanelImperativeHandle | null>(null)
  const [collapsed, setCollapsed] = useState(false)

  return (
    <Frame>
        <SplitPane orientation="vertical">
          <SplitPanel
            id="board"
            panelRef={board}
            defaultSize="55%"
            minSize="18%"
            collapsible
            collapsedSize="2.25rem"
            onResize={(size) => setCollapsed(size.asPercentage < 8)}
          >
            <div style={fill}>{collapsed ? "strip" : "board"}</div>
          </SplitPanel>
          <SplitSeparator orientation="vertical" aria-label="Resize the board" />
          <SplitPanel id="surface" minSize="25%">
            <div style={{ ...fill, gap: "var(--s4)" }}>
              <Button
                size="sm"
                onClick={() =>
                  collapsed
                    ? board.current?.expand()
                    : board.current?.collapse()
                }
              >
                {collapsed ? "Expand board" : "Collapse board"}
              </Button>
          </div>
        </SplitPanel>
      </SplitPane>
    </Frame>
  )
}

/** Collapsing leaves a strip, not a gap — the shape has to survive. */
export const Collapsible: Story = {
  render: () => <CollapsibleDemo />,
}

/** Side by side, for detail panes. */
export const Horizontal: Story = {
  render: () => (
    <Frame>
      <SplitPane orientation="horizontal">
        <SplitPanel id="list" defaultSize="60%" minSize="25%">
          <div style={fill}>list</div>
        </SplitPanel>
        <SplitSeparator
          orientation="horizontal"
          aria-label="Resize the detail pane"
        />
        <SplitPanel id="detail" minSize="20%">
          <div style={fill}>detail</div>
        </SplitPanel>
      </SplitPane>
    </Frame>
  ),
}
