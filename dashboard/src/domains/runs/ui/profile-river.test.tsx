import { describe, expect, it, vi } from "vitest"
import { fireEvent, render } from "@testing-library/react"

import { buildProfileFlow } from "@/domains/runs/model/profile-flow"
import type {
  RunStatus,
  RunSummary,
  WorkItem,
} from "@/domains/runs/model/types"

import { ProfileRiver } from "./profile-river"

function work(
  id: string,
  profile: string,
  status: RunStatus,
  dependsOn: string[] = []
): WorkItem {
  return { id, profile, label: `шаг ${id}`, status, dependsOn }
}

function run(
  id: string,
  status: RunStatus,
  current: string,
  workItems: WorkItem[]
): RunSummary {
  return {
    id,
    projectId: "p_test",
    app: "billing-api",
    title: `run ${id}`,
    status,
    current,
    model: "worker",
    cost: 0,
    tokens: 0,
    durationSec: 0,
    done: false,
    workItems,
  }
}

const flow = buildProfileFlow([
  run("a", "waiting", "w3", [
    work("w1", "explorer", "success"),
    work("w2", "implementer", "success", ["w1"]),
    work("w3", "verifier", "waiting", ["w2"]),
  ]),
  run("b", "escalated", "w3", [
    work("w1", "explorer", "success"),
    work("w2", "implementer", "success", ["w1"]),
    work("w3", "verifier", "escalated", ["w2"]),
  ]),
  run("c", "running", "w2", [
    work("w1", "explorer", "success"),
    work("w2", "implementer", "running", ["w1"]),
  ]),
])

function nodes(container: HTMLElement): HTMLElement[] {
  return [
    ...container.querySelectorAll<HTMLElement>('[data-test="river-node"]'),
  ]
}

describe("ProfileRiver", () => {
  it("draws one node per observed profile, in the derived column order", () => {
    const { container } = render(
      <ProfileRiver flow={flow} selected={null} onSelect={() => {}} />
    )

    expect(nodes(container).map((node) => node.dataset.profile)).toEqual([
      "explorer",
      "implementer",
      "verifier",
    ])
  })

  it("says the pool in words, and names the marked profile among them", () => {
    const { container } = render(
      <ProfileRiver flow={flow} selected={null} onSelect={() => {}} />
    )

    // Hue and weave are never the only channel, and the one profile the screen
    // points at first is not a sighted-only cue either.
    expect(
      nodes(container).map((node) => node.getAttribute("aria-label"))
    ).toEqual([
      "explorer: no work. 3 cleared the profile.",
      "implementer: 1 running. 2 cleared the profile.",
      "verifier: 1 escalated, 1 waiting. 0 cleared the profile. 2 waiting on a human, more than any other profile.",
    ])
  })

  it("reports the profile a click means, and shows which one is filtered", () => {
    const onSelect = vi.fn()
    const { container } = render(
      <ProfileRiver flow={flow} selected="implementer" onSelect={onSelect} />
    )

    const implementer = nodes(container).find(
      (node) => node.dataset.profile === "implementer"
    )
    expect(implementer?.getAttribute("aria-pressed")).toBe("true")

    fireEvent.click(nodes(container)[0])
    expect(onSelect).toHaveBeenCalledWith("explorer")
  })
})
