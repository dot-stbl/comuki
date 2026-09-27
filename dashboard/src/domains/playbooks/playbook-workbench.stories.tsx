import { useEffect } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { expect, fireEvent, waitFor } from "@storybook/test"

import {
  DOCS_ONLY_NO_DEPLOY_PLAYBOOK,
  HOTFIX_MANUAL_DEPLOY_GATE_PLAYBOOK,
  INVALID_CYCLE_PLAYBOOK,
  INVALID_PLAYBOOK,
  LIVE_RUN_NEEDS_DECISION,
  REPLAY_OBSERVED_DRIFT,
  STANDARD_FEATURE_PLAYBOOK,
  STUDIO_DEFAULT,
} from "./model/playbook-fixtures"
import { InvalidPlaybookNotice, PlaybookWorkbench } from "./playbook-workbench"

/**
 * Playbook Workbench — the operator's view of a playbook in three modes.
 *
 * One screen, three panels, one decision vocabulary. The mode is the panel
 * the workbench is showing; the surrounding chrome (the header, the graph,
 * the inspector) is the same across all three, because the operator does
 * not want to learn two screens to answer one question.
 */

/** A wrapper that gives the workbench a definite height, the way a page
 *  would. Without it, the body grid has nothing to resolve against and the
 *  inspector collapses to its content height. */
function Frame({
  children,
  height = "32rem",
}: {
  children: React.ReactNode
  height?: string
}) {
  // The preview's theme decorator only toggles `.dark` — without a
  // `data-theme` the token sheet has no values in light mode and every
  // custom property resolves to nothing, which is how the first pass of
  // these stories shipped contrast nobody had actually designed. Pin the
  // registry's default theme for as long as the frame is mounted.
  useEffect(() => {
    const root = document.documentElement
    const hadTheme = root.dataset.theme
    root.dataset.theme ??= "dichromat-deck"
    return () => {
      if (!hadTheme) {
        delete root.dataset.theme
      }
    }
  }, [])

  // The frame paints the theme's own floor. Without it every translucent
  // status tint composites over the backgrounds addon's canvas, which does
  // not follow the theme toolbar — a light-mode story on a dark canvas is
  // how a passing palette fails axe.
  return (
    <div style={{ height, padding: "var(--s4)", background: "var(--background)" }}>
      {children}
    </div>
  )
}

/** This repo's components key on `data-test`, not testing-library's default
 *  `data-testid` — see `chat-message.test.tsx`'s `at()` helper. */
function byTest(root: HTMLElement, name: string): HTMLElement {
  const found = root.querySelector<HTMLElement>(`[data-test="${name}"]`)
  if (!found) {
    throw new Error(`[data-test="${name}"] not found in story canvas`)
  }
  return found
}

const meta: Meta<typeof PlaybookWorkbench> = {
  title: "Playbooks/Workbench",
  component: PlaybookWorkbench,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs", "playbooks"],
}

export default meta
type Story = StoryObj<typeof PlaybookWorkbench>

/**
 * The Studio panel — the workbench as the operator sees it on first open.
 *
 * The header reads `standard-feature / comuki / published v4 / client git
 * source`. The graph is the default six nodes in pipeline order, with the
 * Repair loop carrying its visible hairline boundary. The inspector
 * answers the three questions a planner asks before they trust the run:
 * is it pinned, what does it look like when it fails, how big a deal is
 * the human gate.
 */
export const Studio: Story = {
  render: () => (
    <Frame>
      <PlaybookWorkbench
        fixture={STUDIO_DEFAULT}
        flow={STANDARD_FEATURE_PLAYBOOK}
      />
    </Frame>
  ),
  play: async ({ canvasElement }) => {
    // React Flow keeps every node `visibility: hidden` until its
    // ResizeObserver has measured it — asserting straight after storyReady
    // races that and reads a hidden node. Waiting is the honest contract:
    // the canvas is done when its first node is actually on screen.
    const node = byTest(canvasElement, "playbook-flow-node")
    await waitFor(async () => {
      await expect(node).toBeVisible()
    })
    // userEvent trips over React Flow's d3 pointer plumbing (crashes inside
    // its own pointer plumbing, "reading 'document'"); a dispatched click is
    // enough — React's root listener fires the node's onClick either way.
    fireEvent.click(node)
    const wrapper = node.closest(".react-flow__node")
    await expect(wrapper).not.toBeNull()
    await expect(wrapper).toHaveClass("selected")
  },
}

/**
 * The hotfix playbook — the same six shapes, but the human gate is a
 * *manual* deploy gate. The gate node says so in its own meta line, because
 * "who presses the deploy button" is the one difference a duty engineer
 * needs to see before trusting a hotfix run.
 */
export const StudioHotfixManualGate: Story = {
  render: () => (
    <Frame>
      <PlaybookWorkbench
        fixture={STUDIO_DEFAULT}
        flow={HOTFIX_MANUAL_DEPLOY_GATE_PLAYBOOK}
      />
    </Frame>
  ),
}

/**
 * The docs-only playbook — no Execute, no Repair loop, no deploy. The
 * shortest graph that still validates; the gate reads `no deploy`, and the
 * canvas shows a pipeline that ends in a person rather than a rollout.
 */
export const StudioDocsOnly: Story = {
  render: () => (
    <Frame>
      <PlaybookWorkbench
        fixture={STUDIO_DEFAULT}
        flow={DOCS_ONLY_NO_DEPLOY_PLAYBOOK}
      />
    </Frame>
  ),
}

/**
 * An invalid cycle on the canvas path — the same malformed graph the notice
 * story carries, handed to the workbench as a flow fixture. The validation
 * state refuses the executable canvas; nothing renders that could be
 * mistaken for a runnable plan.
 */
export const StudioInvalidCycle: Story = {
  render: () => (
    <Frame>
      <PlaybookWorkbench
        fixture={INVALID_PLAYBOOK}
        flow={INVALID_CYCLE_PLAYBOOK}
      />
    </Frame>
  ),
}

/**
 * The Live run panel — a run on generation 1 of 2 with Verify failed.
 *
 * The path reads `Intake success, Plan success, Execute success, Verify
 * failed, Repair generation 1 running, Human gate waiting`. The attention
 * panel names what needs the operator (`approve repair scope`) and shows
 * the evidence tally (`12 passed / 2 failed`).
 *
 * The interaction story asserts the operator can click *Approve repair* and
 * the button flips to its approved reading — the workbench owns the local
 * state today, and a future page wires the same shape to a real store.
 */
export const LiveRunNeedsDecision: Story = {
  render: () => (
    <Frame>
      <PlaybookWorkbench fixture={LIVE_RUN_NEEDS_DECISION} initialMode="live" />
    </Frame>
  ),
  play: async ({ canvasElement }) => {
    // The Approve repair button is the one control this story exercises —
    // clicking it flips the button label from "Approve repair" to
    // "Approved" and disables itself. The pause-run toggle is local too,
    // but the live run is not paused by default and we do not exercise it
    // here.
    const approve = byTest(canvasElement, "workbench-approve-repair")
    fireEvent.click(approve)
    // React batches the state flip; the harness reads the attribute before
    // the commit lands unless the assertion itself waits for it.
    await waitFor(async () => {
      await expect(approve).toBeDisabled()
    })
  },
}

/**
 * The Replay panel — a timeline of planned vs observed with a selected
 * event whose actual run diverged but stayed inside the policy window.
 *
 * The operator scans the timeline for `within-policy` or
 * `outside-policy` markers, clicks one, reads the detail. The selected
 * event here is the Plan stage — the brain wrote three implementers
 * instead of four — and the detail names the divergence and the outcome.
 * The compact generated DAG under the detail is what the brain wrote
 * when it replayed the run.
 */
export const ReplayObservedDrift: Story = {
  render: () => (
    <Frame height="36rem">
      <PlaybookWorkbench fixture={REPLAY_OBSERVED_DRIFT} />
    </Frame>
  ),
}

/**
 * The Invalid panel — the workbench refuses to render a malformed playbook
 * and says so out loud.
 *
 * The fixture carries a cycle (`Plan` depends on itself) so the workbench
 * would not survive a real compile. The studio still renders the header
 * (the playbook is named, even when broken) and the notice explains the
 * reason in one line.
 *
 * We do not use the full `PlaybookWorkbench` here — the cycle is the kind
 * of thing a real harness would catch before it reaches this surface. The
 * notice is what the surface says when it does.
 */
export const InvalidPlaybook: Story = {
  render: () => (
    <Frame>
      <InvalidPlaybookNotice
        reason={`The graph has a cycle: 'Plan (self)' depends on 'Plan', and 'Plan' depends on 'Plan (self)'. The harness could not compile this playbook — fix the edge before publishing.`}
      />
    </Frame>
  ),
}

/** The workbench in Invalid mode, for completeness — the graph does not
 *  draw, the inspector renders the same notice. Used when the cycle is
 *  detected upstream and the workbench still has to honour its layout. */
export const InvalidPlaybookInWorkbench: Story = {
  render: () => (
    <Frame>
      <PlaybookWorkbench fixture={INVALID_PLAYBOOK} initialMode="studio" />
    </Frame>
  ),
}
