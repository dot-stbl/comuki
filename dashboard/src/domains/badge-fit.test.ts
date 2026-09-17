/* What a badge does when the row it rides runs out of room.
 *
 * Nine domains draw their own marks — approvals, compute, knowledge, models,
 * queue, settings, sources, tasks, verify — and each one is a small
 * `inline-flex` box with a hairline, a wash and a word in it. They were built
 * from the same measurements but not from the same layout contract, and the
 * two that drifted both drifted the same way: they cancelled the badge's
 * content-based minimum with `min-inline-size: 0`.
 *
 * That single declaration is what turns a badge into something that paints over
 * its neighbours. Every badge here is `white-space: nowrap`, and a `nowrap` box
 * has no soft wrap opportunity in it — so its min-content size *is* its whole
 * label, and as a flex item it simply cannot be squeezed. `min-inline-size: 0`
 * throws that away: the box shrinks, the label does not, and the label goes on
 * drawing at full width outside a border that is now narrower than it. In
 * `approval-badges` there was nothing behind it at all. In `tasks-badges` there
 * was `overflow: hidden; text-overflow: ellipsis` — which reads like a cut and
 * is not one, because `text-overflow` applies to block containers and a badge
 * is a flex container, so the promised ellipsis never rendered and the cut fell
 * mid-glyph.
 *
 * The product already has the rule this should have followed: **when space runs
 * out, a component either scrolls to keep its reading or shrinks to a shape.
 * Never crush a reading to fit.** A badge is a reading. So the contract for the
 * whole family is one line — `flex: 0 0 auto` — and the give happens on the
 * other side of the row: the prose or the value beside the badge truncates, or
 * the row wraps and grows downward.
 *
 * ## Why this file now names three rules where it used to name eleven
 *
 * It listed eleven because eleven stylesheets each said the contract for
 * themselves. They do not any more. The box moved to `shared/ui/badge-shell`,
 * every badge in the product composes it through `badgeShell()`, and the
 * contract is declared exactly once. Eleven copies of an invariant is eleven
 * chances to lose it — which is why two of them had already been lost — so
 * aiming the guard at the one surviving declaration is not a smaller test, it
 * is the same test pointed at the place the answer moved to.
 *
 * What is guarded is therefore three things rather than one:
 *
 * - the shell keeps the contract;
 * - the two marks that are *not* badges and so do not compose it keep it
 *   themselves — `knowledge .pinned`, which has no border and no wash to put in
 *   a shell, and `settings .tag`, whose padding is the filter chip's step
 *   rather than the badge step;
 * - and every caller that *does* compose the shell keeps no second copy of it,
 *   which is the exact regression that would let the drift back in.
 *
 * jsdom computes no layout, so no rendered test can see an overlap: every box
 * is zero wide and nothing is ever next to anything. What is checkable is the
 * source of the behaviour, so this reads the stylesheets back off disk and
 * asserts the declarations a browser would act on — the way
 * `data-table.test.tsx` guards the pinned-column invariants it also cannot see,
 * and `app/styles/responsive.test.ts` guards the header band.
 */
import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { describe, expect, it } from "vitest"

/* `src/domains/`, from this file rather than from the process's working
   directory: a test that only passes when it is run from the package root is a
   test that fails in CI for a reason that has nothing to do with the product. */
const DOMAINS = dirname(fileURLToPath(import.meta.url))

const COMMENTS = /\/\*[\s\S]*?\*\//g

function read(path: string): string {
  // A path may reach out of the domains folder — the shell every badge is drawn
  // in, and the kit's own status badge, both live in `shared/ui`. And a path may
  // be a component rather than a stylesheet: which shell a badge composes is as
  // much a source fact as what its sheet declares, and stripping the block
  // comments is what keeps a doc comment that *mentions* `badgeShell()` from
  // standing in for a call to it.
  return readFileSync(join(DOMAINS, path), "utf8").replace(COMMENTS, "")
}

/** The declaration bodies of every rule in `sheet` whose selector list names `selector`. */
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
 * `overflow` does not answer with `text-overflow`'s value and asking for
 * `flex` does not answer with `flex-wrap`'s.
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
 * The family, and the one contract all of it keeps.
 * ------------------------------------------------------------------ */

/** The one sheet every badge's box now comes out of. */
const SHELL = "../shared/ui/badge-shell.module.css"

/**
 * Every rule in the product that is a `nowrap` box carrying a reading and
 * riding a row beside something else.
 *
 * The shell is first because it *is* the box now — nine domain families and the
 * kit's own `StatusBadge` are all drawn in it, including the one that most
 * needed saying: a status badge is rendered both inside a table cell and
 * outside one. Inside, it inherited `nowrap` from `.cell` and behaved; outside
 * — the tracker panel, the status mapping preview, the attention list, the
 * inspector's gate chips — it could be squeezed to its longest word and wrap
 * its own label mid-badge.
 *
 * The other two are the marks that deliberately do not compose the shell, and
 * they are in this list for the same reason the badges were: they are not
 * badges, but they are the same shape of problem — a short unbreakable string
 * in a flex row.
 */
const FAMILY: readonly (readonly [string, string])[] = [
  [SHELL, ".shell"],
  ["knowledge/ui/knowledge-badges.module.css", ".pinned"],
  ["settings/ui/settings-badges.module.css", ".tag"],
]

/**
 * The shell's two size steps.
 *
 * A step is `gap`, `padding` and `font-size` — how big, and never how the box
 * fits. The fit contract lives on `.shell` and is stated once, so these two are
 * the one door left open in the shell sheet through which a geometry
 * declaration could come back without anybody touching `.shell` itself.
 */
const STEPS: readonly string[] = [".sm", ".md"]

/**
 * Every caller that composes the shell: the component, its own stylesheet, and
 * the rule in it that carries the caller's half — colour, weight, the edge.
 *
 * The sources and verify pairs have no `.badge` rule left at all, because their
 * border is transparent until a state says otherwise and the shell's own
 * fallback already says exactly that; their first state stands for them here.
 */
const COMPOSED: readonly (readonly [string, string, string])[] = [
  [
    "approvals/ui/approval-badges.tsx",
    "approvals/ui/approval-badges.module.css",
    ".badge",
  ],
  [
    "compute/ui/compute-badges.tsx",
    "compute/ui/compute-badges.module.css",
    ".badge",
  ],
  [
    "knowledge/ui/knowledge-badges.tsx",
    "knowledge/ui/knowledge-badges.module.css",
    ".badge",
  ],
  ["models/ui/model-badges.tsx", "models/ui/model-badges.module.css", ".badge"],
  ["queue/ui/queue-badges.tsx", "queue/ui/queue-badges.module.css", ".badge"],
  [
    "settings/ui/settings-badges.tsx",
    "settings/ui/settings-badges.module.css",
    ".badge",
  ],
  ["tasks/ui/tasks-badges.tsx", "tasks/ui/tasks-badges.module.css", ".badge"],
  [
    "sources/ui/connection-state-badge.tsx",
    "sources/ui/connection-state-badge.module.css",
    ".connected",
  ],
  [
    "verify/ui/verify-result-badge.tsx",
    "verify/ui/verify-result-badge.module.css",
    ".passed",
  ],
  [
    "../shared/ui/status-badge.tsx",
    "../shared/ui/status-badge.module.css",
    ".badge",
  ],
]

/**
 * What the shell declares, and therefore what no caller may declare again.
 *
 * Not tidiness — correctness. Two single-class rules in two different CSS
 * modules have equal specificity, so which of them wins is settled by the order
 * the bundler happened to emit the two files in. That is not a contract, it is
 * not stable between dev and build, and it does not survive an import being
 * moved up a line. The shell's one deliberately changeable declaration is the
 * border colour, and it is reached through `--badge-edge`: a custom property
 * declared in exactly one place per badge cannot lose a race it is not in.
 */
const SHELL_OWNED: readonly string[] = [
  "display",
  "align-items",
  "flex",
  "white-space",
  "border",
  "border-radius",
  "border-width",
  "border-style",
  "border-color",
  "font-family",
  "font-size",
  "line-height",
  "gap",
  "padding",
]

describe("a badge keeps its own reading and its own box", () => {
  it("declares that it never gives up width", () => {
    // Seven of these were already unshrinkable, but only as a side effect of
    // `white-space: nowrap` setting their min-content size. That is a fact
    // about text, not a stated intention, and the two sheets that drifted
    // drifted by overriding it. Said out loud, it survives the next edit.
    for (const [path, selector] of FAMILY) {
      expect({
        path,
        selector,
        flex: declared(read(path), selector, "flex"),
      }).toEqual({ path, selector, flex: "0 0 auto" })
    }
  })

  it("keeps the label on one line", () => {
    // The whole contract rests on this: `nowrap` is what makes the label's
    // min-content size the label, which is what makes the box unsqueezable in
    // the first place.
    for (const [path, selector] of FAMILY) {
      expect({
        path,
        selector,
        wrap: declared(read(path), selector, "white-space"),
      }).toEqual({ path, selector, wrap: "nowrap" })
    }
  })

  it("lets nothing cancel its content-based minimum", () => {
    // `min-inline-size: 0` / `min-width: 0` is the declaration that let a badge
    // shrink under its own unbreakable label and paint outside its border.
    // There is no correct value for it here, so the guard is on the property.
    for (const [path, selector] of FAMILY) {
      const sheet = read(path)
      expect({
        path,
        selector,
        logical: declared(sheet, selector, "min-inline-size"),
        physical: declared(sheet, selector, "min-width"),
      }).toEqual({ path, selector, logical: undefined, physical: undefined })
    }
  })

  it("never crushes its own reading to fit", () => {
    // A status cut to `escal` is a status nobody can filter on, and a ticket id
    // cut to `PLAT-4` names a different ticket. `text-overflow` would not even
    // do it honestly: it applies to block containers, and a badge is a flex
    // container, so on one of these it renders no ellipsis at all — only a hard
    // cut through a glyph. Where a column really is too narrow, the table cell
    // is what clips, and that is the product's one answer.
    for (const [path, selector] of FAMILY) {
      const sheet = read(path)
      expect({
        path,
        selector,
        overflow: declared(sheet, selector, "overflow"),
        ellipsis: declared(sheet, selector, "text-overflow"),
      }).toEqual({ path, selector, overflow: undefined, ellipsis: undefined })
    }
  })

  it("keeps a size step to the size and nothing else", () => {
    // The fit contract is declared once, on `.shell`. These two rules are the
    // only thing in that sheet which varies per caller, so they are the only
    // place a geometry declaration could reappear without anybody editing the
    // rule that carries the contract. A step says how big; it never says how
    // the box gives.
    const shell = read(SHELL)
    for (const selector of STEPS) {
      expect({
        selector,
        flex: declared(shell, selector, "flex"),
        wrap: declared(shell, selector, "white-space"),
        logical: declared(shell, selector, "min-inline-size"),
        physical: declared(shell, selector, "min-width"),
        overflow: declared(shell, selector, "overflow"),
        ellipsis: declared(shell, selector, "text-overflow"),
      }).toEqual({
        selector,
        flex: undefined,
        wrap: undefined,
        logical: undefined,
        physical: undefined,
        overflow: undefined,
        ellipsis: undefined,
      })
    }
  })

  it("is composed by every badge and copied by none", () => {
    // The half of the invariant that used to be enforced by there being eleven
    // copies of it. Now that the box is declared once, two things have to hold
    // for the four assertions above to mean anything: every badge actually goes
    // through that declaration, and nobody re-declares the box beside it. A
    // caller that did would win or lose against the shell depending on which
    // stylesheet the bundler emitted first — see `SHELL_OWNED`.
    for (const [component, sheet, selector] of COMPOSED) {
      expect({
        component,
        composes: read(component).includes("badgeShell("),
      }).toEqual({ component, composes: true })

      const css = read(sheet)
      for (const property of SHELL_OWNED) {
        expect({
          sheet,
          selector,
          property,
          value: declared(css, selector, property),
        }).toEqual({ sheet, selector, property, value: undefined })
      }
    }
  })
})

/* ------------------------------------------------------------------ *
 * The other side of the row: where the give actually happens.
 *
 * A badge that refuses to shrink is only half an answer. The row it sits in has
 * to have somewhere for the pressure to go, or the row overflows instead — so
 * every one of these either wraps downward or hands a neighbour a real cut.
 * ------------------------------------------------------------------ */

describe("a row that carries a badge grows downward, never sideways", () => {
  it("wraps the approval card's first line and truncates the app name", () => {
    const card = read("approvals/ui/approval-card.module.css")
    expect(declared(card, ".head", "flex-wrap")).toBe("wrap")
    // Two badges, an app name and a clock share this line. The app name is the
    // one thing on it that is prose, so it is the one thing that gives.
    expect(declared(card, ".app", "min-inline-size")).toBe("0")
    expect(declared(card, ".app", "text-overflow")).toBe("ellipsis")
  })

  it("wraps the knowledge row's head and truncates the entry title", () => {
    const row = read("knowledge/ui/knowledge-entry-row.module.css")
    expect(declared(row, ".head", "flex-wrap")).toBe("wrap")
    expect(declared(row, ".title", "min-inline-size")).toBe("0")
    expect(declared(row, ".title", "text-overflow")).toBe("ellipsis")
  })

  it("wraps the knowledge sheet's mark strip", () => {
    const sheet = read("knowledge/ui/knowledge-detail-sheet.module.css")
    expect(declared(sheet, ".marks", "flex-wrap")).toBe("wrap")
  })

  it("wraps a pool's naming line rather than painting it over the figure", () => {
    // `.room` is `nowrap` and holds its figure; `.title` may shrink and has no
    // cut behind it, so without the wrap the project key and the provider mark
    // drew straight across the slots-free reading on a narrow pool grid.
    const card = read("compute/ui/capacity-card.module.css")
    expect(declared(card, ".head", "flex-wrap")).toBe("wrap")
  })

  it("wraps the environment tags in a cell instead of widening it", () => {
    const badges = read("settings/ui/settings-badges.module.css")
    expect(declared(badges, ".tags", "flex-wrap")).toBe("wrap")
  })

  it("wraps the inspector's gate chips and breaks its heading anywhere", () => {
    const inspector = read("runs/ui/work-item-inspector.module.css")
    expect(declared(inspector, ".chips", "flex-wrap")).toBe("wrap")
    // The status badge in the head is `flex: 0 0 auto` and parked at the end of
    // the line, so a step name that arrives as one unbroken token — a path, a
    // branch, an identifier — would otherwise overflow `.identity` and draw
    // under it.
    expect(declared(inspector, ".title", "overflow-wrap")).toBe("anywhere")
    expect(declared(inspector, ".badge", "flex")).toBe("0 0 auto")
  })

  it("truncates the prose beside a badge on the two heads that cannot wrap", () => {
    // Both put a badge on a single line with prose. Neither wraps, and neither
    // needs to: the prose is the reading that can afford an ellipsis, and the
    // badge is the one that cannot.
    const attention = read("home/ui/attention-list.module.css")
    expect(declared(attention, ".reason", "min-width")).toBe("0")
    expect(declared(attention, ".reason", "text-overflow")).toBe("ellipsis")

    const tracker = read("settings/ui/tracker-panel.module.css")
    expect(declared(tracker, ".name", "min-inline-size")).toBe("0")
    expect(declared(tracker, ".name", "text-overflow")).toBe("ellipsis")
  })
})
