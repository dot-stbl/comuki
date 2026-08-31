/* The capacity card's one layout invariant: two tracks on a single shared
 * axis, and nothing a row carries may paint across that axis.
 *
 * The defect this file stands for was found on the real screen. The binding
 * tag beside the word `allocatable` is wider than the fixed `6.5rem` name
 * column each row used to size for itself; a grid cell does not clip its item,
 * so the tag went on drawing over the channel — the meter stripe that is the
 * card's whole reading. Widening the column per row would not have fixed it,
 * only moved the flaw: two rows each sizing their own name column put the two
 * channels at different x, and comparing the two bars is the entire task. The
 * fix hoisted the grid to the rows' parent, so both rows land in the same
 * three columns and the name column is sized once, by the worst case.
 *
 * jsdom computes no layout, so no rendered test can see that overlap: every
 * box is zero wide and nothing is ever next to anything. What is checkable is
 * the source of the behaviour, so this reads the stylesheet back off disk and
 * asserts the declarations a browser would act on — the way `badge-fit.test.ts`
 * guards the badge family and `app/styles/responsive.test.ts` guards the
 * header band — plus the DOM shape those declarations depend on.
 */
import { readFileSync } from "node:fs"
import { render } from "@testing-library/react"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"

import type {
  ComputePool,
  ComputeProvider,
} from "@/domains/compute/model/types"

import { CapacityCard } from "./capacity-card"

/* The sheet beside the component, resolved from this file rather than from the
   process's working directory — a test that only passes when run from the
   package root fails in CI for a reason that has nothing to do with the
   product. Comments are stripped so a property named inside a comment can
   never answer for a rule. */
const SHEET = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "capacity-card.module.css"),
  "utf8"
).replace(/\/\*[\s\S]*?\*\//g, "")

/** The declaration bodies of every rule whose selector list names `selector`. */
function rules(sheet: string, selector: string): string[] {
  const found: string[] = []
  const block = /([^{}]+)\{([^{}]*)\}/g
  let match: RegExpExecArray | null
  while ((match = block.exec(sheet)) !== null) {
    const named = (match[1] ?? "")
      .split(",")
      .map((part) => part.trim())
      .includes(selector)
    if (named) {
      found.push(match[2] ?? "")
    }
  }
  return found
}

/**
 * One declared value, or `undefined` when no rule for `selector` sets it.
 *
 * The property is anchored to the start of a declaration, so asking for
 * `overflow` does not answer with `text-overflow`'s value.
 */
function declared(
  sheet: string,
  selector: string,
  property: string
): string | undefined {
  for (const body of rules(sheet, selector)) {
    const hit = new RegExp(`(?:^|;)\\s*${property}\\s*:([^;]+)`).exec(body)
    if (hit) {
      return (hit[1] ?? "").trim()
    }
  }
  return undefined
}

/* ------------------------------------------------------------------ *
 * The shared axis, asserted on the sheet that draws it.
 * ------------------------------------------------------------------ */

describe("both tracks measure against one shared axis", () => {
  it("sizes the name column from the widest name either row brings", () => {
    // The first track is the whole fix: `max-content` includes the `binding`
    // tag beside the wider name, so a name cell is always exactly as wide as
    // its own content and nothing inside it can reach the channel. The middle
    // track is zero-floored, so a narrow card takes the shortfall out of the
    // channel — a bar is a shape, and a shape gives before a reading does.
    // The figures column is `auto` because its `nowrap` numbers are readings,
    // and readings do not give.
    expect(declared(SHEET, ".tracks", "grid-template-columns")).toBe(
      "max-content minmax(0, 1fr) auto"
    )
  })

  it("puts the grid on the rows' parent, not on each row", () => {
    // A grid per row sizes that row's name column per row, and two name
    // columns of different widths put the two channels at different x — which
    // is the thing the card exists to compare. `display: contents` joins the
    // rows' cells to the one grid above instead, and the row element itself
    // stays in the tree to carry `data-binding` / `data-empty`.
    expect(declared(SHEET, ".track", "display")).toBe("contents")
  })

  it("keeps the rows' cells centred on one another and one gap apart", () => {
    // The channel is a fixed-height bar beside a text line; centring is what
    // holds the two on each other's midline. The gap carries both distances
    // the old layout kept apart — `--s2` between the two rows, `--s3` between
    // a row's cells — now said once, on the grid that owns both.
    expect(declared(SHEET, ".tracks", "align-items")).toBe("center")
    expect(declared(SHEET, ".tracks", "gap")).toBe("var(--s2) var(--s3)")
  })
})

/* ------------------------------------------------------------------ *
 * The binding tag: a reading, held to the badge family's contract.
 * ------------------------------------------------------------------ */

describe("the binding tag is a reading that keeps its own box", () => {
  it("declares the badge family's contract", () => {
    // The tag is the same shape of thing as the badges `badge-fit.test.ts`
    // guards: a short unbreakable word riding a row. `flex: 0 0 auto` and
    // `nowrap` make it unsqueezable, and the guards below are the properties
    // that undo that — `min-inline-size: 0` lets a badge shrink under its own
    // label and paint outside its border, and an `overflow` cut on a flex box
    // renders mid-glyph rather than as an ellipsis. There is no correct value
    // for any of them here, so the guard is on the property.
    expect(declared(SHEET, ".bindingTag", "flex")).toBe("0 0 auto")
    expect(declared(SHEET, ".bindingTag", "white-space")).toBe("nowrap")
    expect(declared(SHEET, ".bindingTag", "min-inline-size")).toBeUndefined()
    expect(declared(SHEET, ".bindingTag", "min-width")).toBeUndefined()
    expect(declared(SHEET, ".bindingTag", "overflow")).toBeUndefined()
    expect(declared(SHEET, ".bindingTag", "text-overflow")).toBeUndefined()
  })

  it("stays beside its name, in the cell the shared grid sizes around it", () => {
    render(<CapacityCard pool={POOL} provider={PROVIDER} projectKey="comuki" />)

    // The quota track is the binding one in this pool: full, while the
    // cluster still has room.
    const bindingRow = document.querySelector('[data-track="quota"]')
    expect(bindingRow?.hasAttribute("data-binding")).toBe(true)

    // The tag is inside the row's first cell — the name cell — so it is sized
    // into the `max-content` column together with the name, not riding the
    // channel or the figures. The word beside the name is a design decision
    // ("the binding track keeps the word `binding` beside its name"), and it
    // is also what the CSS above takes as its input.
    const tag = Array.from(
      bindingRow?.firstElementChild?.querySelectorAll("span") ?? []
    ).find((span) => span.textContent === "binding")
    expect(tag).toBeDefined()

    // Both rows contribute exactly three cells to the shared grid — name,
    // channel, figures. `display: contents` only works because every row is
    // the same shape; a fourth cell in either row would land in the next
    // row's first column.
    for (const row of Array.from(document.querySelectorAll("[data-track]"))) {
      expect(row.children).toHaveLength(3)
    }
  })

  it("keeps the no-answer row the same three cells as its twin", () => {
    render(
      <CapacityCard
        pool={POOL}
        provider={SILENT_PROVIDER}
        projectKey="comuki"
      />
    )

    // `null` is not zero: the capacity row refuses to invent a ceiling, and
    // it refuses in the same three cells a reading would have occupied — the
    // hatched channel and `no answer` sit in the shared columns, not on their
    // own axis beside them.
    const emptyRow = document.querySelector('[data-track="allocatable"]')
    expect(emptyRow?.hasAttribute("data-empty")).toBe(true)
    expect(emptyRow?.children).toHaveLength(3)
    expect(emptyRow?.textContent).toContain("no answer")
  })
})

/* ------------------------------------------------------------------ *
 * Fixtures — the QuotaBinds story in miniature: the project is full, the
 * cluster is not. That is the reading the screen exists for, and the one that
 * puts the tag on the screen at all.
 * ------------------------------------------------------------------ */

const PROVIDER: ComputeProvider = {
  id: "cp_k8s_prod",
  kind: "kubernetes",
  endpoint: "https://cp_k8s_prod.example:6443",
  state: "active",
  takingWork: true,
  allocatable: { used: 31, limit: 96, source: "capacity api" },
  note: "",
}

const SILENT_PROVIDER: ComputeProvider = {
  ...PROVIDER,
  id: "cp_k8s_staging",
  state: "unreachable",
  takingWork: false,
  allocatable: null,
}

const POOL: ComputePool = {
  projectId: "p_comuki",
  providerId: "cp_k8s_prod",
  minIdle: 2,
  maxIdle: 6,
  idle: 5,
  workers: 24,
  quota: { used: 24, limit: 24, source: "project quota" },
  profiles: ["implementer"],
}
