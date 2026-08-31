import type { Meta, StoryObj } from "@storybook/react"

import type { QueueItem, Worker } from "@/domains/queue/model/types"

import { AgeMeter, LeaseMeter } from "./meters"

function item(status: QueueItem["status"], ageSec: number): QueueItem {
  return {
    id: "wi_0001",
    runId: "8f3c2a91",
    projectId: "p_comuki",
    profile: "verifier",
    label: "check payout metrics after the rollout",
    status,
    ageSec,
    claimedBy: status === "running" ? "wk_2f8a" : null,
    blockedOn: [],
  }
}

function worker(leaseSec: number | null, heartbeatAgeSec: number): Worker {
  return {
    id: "wk_e34d",
    projectId: "p_plexor",
    profile: "implementer",
    state: leaseSec === null ? "idle" : "busy",
    itemId: leaseSec === null ? null : "wi_0104",
    provider: "kubernetes",
    handle: "k8s/plexor-prod/worker-implementer-e34d",
    heartbeatAgeSec,
    leaseSec,
    upSec: 1512,
    digest: "sha256:9c41ab",
  }
}

/** A column of cells at the table's own width, so the meters are read the way
 *  they are actually read: down a narrow track, at a glance. */
function Column({
  rows,
}: {
  rows: Array<{ note: string; cell: React.ReactNode }>
}) {
  return (
    <div style={{ padding: "var(--s6)", display: "grid", gap: "var(--s2)" }}>
      {rows.map((row) => (
        <div
          key={row.note}
          style={{
            display: "grid",
            gridTemplateColumns: "6rem 1fr",
            alignItems: "center",
            gap: "var(--s5)",
            height: "1.625rem",
          }}
        >
          <div style={{ width: "6rem" }}>{row.cell}</div>
          <span
            style={{
              fontFamily: "var(--font-ui)",
              fontSize: "var(--t-sm)",
              color: "var(--muted-foreground)",
            }}
          >
            {row.note}
          </span>
        </div>
      ))}
    </div>
  )
}

const meta: Meta<typeof AgeMeter> = {
  title: "Queue/Meters",
  component: AgeMeter,
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj<typeof AgeMeter>

/**
 * Age is what turns this screen from a list into an instrument — and only on
 * the one status where it accuses anyone. A queued row grows a bar under its
 * figure; every other status is just a duration.
 */
export const Age: Story = {
  render: () => (
    <Column
      rows={[
        { note: "queued 8 seconds — the system working", cell: <AgeMeter item={item("queued", 8)} /> },
        { note: "queued a minute — no longer instant", cell: <AgeMeter item={item("queued", 74)} /> },
        { note: "queued 11 minutes — nothing is going to claim this", cell: <AgeMeter item={item("queued", 664)} /> },
        { note: "queued 43 minutes — the profile matches no worker at all", cell: <AgeMeter item={item("queued", 2612)} /> },
        { note: "running 20 minutes — its worker's lease answers for this one", cell: <AgeMeter item={item("running", 1186)} /> },
        { note: "blocked 3 hours — waiting on its own run, and fine", cell: <AgeMeter item={item("blocked", 13260)} /> },
      ]}
    />
  ),
}

/** The pool's half of the same failure: a lease nobody is defending. */
export const Lease: Story = {
  render: () => (
    <Column
      rows={[
        { note: "idle — no lease to hold", cell: <LeaseMeter worker={worker(null, 1)} /> },
        { note: "three minutes left, heartbeating", cell: <LeaseMeter worker={worker(214, 3)} /> },
        { note: "seconds left, still heartbeating — routine", cell: <LeaseMeter worker={worker(18, 4)} /> },
        { note: "no heartbeat for over a minute — the item is stuck until it lapses", cell: <LeaseMeter worker={worker(6, 74)} /> },
      ]}
    />
  ),
}
