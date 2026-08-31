import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { BoardsPanel } from "@/domains/observability/ui/boards-panel"
import { ConnectGuide } from "@/domains/observability/ui/connect-guide"
import {
  OBSERVABILITY_SEED,
  OBSERVABILITY_UNCONFIGURED_SEED,
} from "@/shared/api/mock/observability.seed"

describe("boards are links, and never an embed", () => {
  it("renders an anchor per reachable board and no iframe anywhere", () => {
    render(<BoardsPanel boards={OBSERVABILITY_SEED.boards} />)

    const links = [
      ...document.querySelectorAll('[data-test="board-link"]'),
    ] as HTMLAnchorElement[]

    expect(links.length).toBeGreaterThan(0)
    for (const link of links) {
      expect(link.tagName).toBe("A")
      expect(link.getAttribute("href")?.startsWith("https://")).toBe(true)
      expect(link.getAttribute("target")).toBe("_blank")
      expect(link.getAttribute("rel")).toBe("noreferrer")
    }

    // The requirements rule the iframe out (§15), and this is the assertion
    // that keeps it out: infra metrics and a run's own timeline are read on
    // different clocks, and a surface showing both teaches an operator to look
    // for a run's story where only half of it is.
    expect(document.querySelector("iframe")).toBeNull()
  })

  it("names the three boards and what each answers", () => {
    render(<BoardsPanel boards={OBSERVABILITY_SEED.boards} />)

    expect(screen.getByText("Comuki · runs")).toBeTruthy()
    expect(screen.getByText("Comuki · workers")).toBeTruthy()
    expect(screen.getByText("Comuki · cost")).toBeTruthy()
    expect(
      document.querySelectorAll('[data-test="board"]')
    ).toHaveLength(3)
  })

  it("tells a board that is not imported apart from one that is", () => {
    render(<BoardsPanel boards={OBSERVABILITY_SEED.boards} />)

    const pending = [
      ...document.querySelectorAll('[data-test="board-not-imported"]'),
    ]
    // Not an error and not a blank: the definition is in our repository and
    // nobody has imported it yet, which is a next step rather than a failure.
    expect(pending).toHaveLength(1)
    expect(pending[0].textContent).toContain("not imported yet")
  })
})

describe("the platform with no boards at all", () => {
  it("offers nothing to open, and says why rather than showing an empty box", () => {
    render(<BoardsPanel boards={OBSERVABILITY_UNCONFIGURED_SEED.boards} />)

    expect(document.querySelectorAll('[data-test="board-link"]')).toHaveLength(0)
    // The three entries still render: their definitions exist whether or not
    // anyone has imported them, and a page that listed nothing would read as a
    // load that failed.
    expect(document.querySelectorAll('[data-test="board"]')).toHaveLength(3)
    expect(
      document.querySelectorAll('[data-test="board-not-imported"]')
    ).toHaveLength(3)
  })

  it("says no grafana is configured, and names the next step", () => {
    render(
      <ConnectGuide
        grafana={OBSERVABILITY_UNCONFIGURED_SEED.grafana}
        boardsRepo={OBSERVABILITY_UNCONFIGURED_SEED.boardsRepo}
        noBoards
      />
    )

    expect(document.querySelector('[data-test="no-grafana"]')).not.toBeNull()
    expect(document.querySelector('[data-test="grafana-configured"]')).toBeNull()
    // No "open grafana" button, because there is nothing to open.
    expect(document.querySelector('[data-test="grafana-link"]')).toBeNull()

    // The half most pages leave out: the operator who finds nothing to click
    // is told exactly whose job it is and where the definitions live.
    const repoLink = document.querySelector(
      '[data-test="boards-repo-link"]'
    ) as HTMLAnchorElement
    expect(repoLink.getAttribute("href")).toBe(
      OBSERVABILITY_UNCONFIGURED_SEED.boardsRepo.url
    )
    expect(screen.getByText(/deploy\/grafana\/dashboards/)).toBeTruthy()
    expect(document.querySelectorAll("ol li")).toHaveLength(4)
  })

  it("distinguishes a configured grafana with nothing imported", () => {
    render(
      <ConnectGuide
        grafana={OBSERVABILITY_SEED.grafana}
        boardsRepo={OBSERVABILITY_SEED.boardsRepo}
        noBoards
      />
    )

    // Two different problems, two different sentences: a missing grafana and a
    // grafana nobody has imported into.
    expect(document.querySelector('[data-test="no-grafana"]')).toBeNull()
    expect(document.querySelector('[data-test="no-boards"]')).not.toBeNull()
    expect(
      document.querySelector('[data-test="grafana-configured"]')?.textContent
    ).toContain("grafana.comuki.internal")
  })

  it("keeps the guide on the page even when everything is working", () => {
    render(
      <ConnectGuide
        grafana={OBSERVABILITY_SEED.grafana}
        boardsRepo={OBSERVABILITY_SEED.boardsRepo}
        noBoards={false}
      />
    )

    expect(document.querySelector('[data-test="no-grafana"]')).toBeNull()
    expect(document.querySelector('[data-test="no-boards"]')).toBeNull()
    // Still four steps: it is the reference for adding the board that is still
    // missing, not only the empty state.
    expect(document.querySelectorAll("ol li")).toHaveLength(4)
    expect(document.querySelector('[data-test="grafana-link"]')).not.toBeNull()
  })
})

describe("the seeded boards", () => {
  it("ships a definition for each of the three, versioned with the platform", () => {
    expect(OBSERVABILITY_SEED.boards.map((board) => board.kind)).toEqual([
      "runs",
      "workers",
      "cost",
    ])
    for (const board of OBSERVABILITY_SEED.boards) {
      expect(board.uid.length).toBeGreaterThan(0)
      expect(board.summary.length).toBeGreaterThan(24)
    }
  })

  it("carries no field an embed would need", () => {
    // The negative decision, made structural: no panel id, no time range, no
    // datasource. A record that kept them would be an invitation to build the
    // thing the requirements ruled out.
    for (const board of OBSERVABILITY_SEED.boards) {
      const keys = Object.keys(board)
      expect(keys).not.toContain("panelId")
      expect(keys).not.toContain("from")
      expect(keys).not.toContain("to")
      expect(keys).not.toContain("datasource")
    }
  })

  it("seeds the state every new installation opens in", () => {
    expect(OBSERVABILITY_UNCONFIGURED_SEED.grafana).toBeNull()
    expect(
      OBSERVABILITY_UNCONFIGURED_SEED.boards.every((board) => board.url === null)
    ).toBe(true)
    // The definitions are still ours, so the repo coordinate survives.
    expect(OBSERVABILITY_UNCONFIGURED_SEED.boardsRepo).toEqual(
      OBSERVABILITY_SEED.boardsRepo
    )
  })
})
