/* What a palette has to be true of before it is allowed to ship.
 *
 * None of this can be seen in jsdom and most of it cannot be seen by eye at
 * all — "these two yellows are 4.6 L* apart" is not a judgement anybody makes
 * reliably, and "this pair survives protanopia" is not a judgement anybody
 * makes at all. So the promises are arithmetic, and they are made here rather
 * than in a comment.
 *
 * Three floors, in increasing order of how much they cost to keep:
 *
 *   contrast    text has to be readable on the surface behind it. WCAG 4.5:1.
 *   lightness   the six statuses have to survive greyscale — a projector, a
 *               photocopy, a screenshot pasted into a ticket. Only lightness
 *               survives that, so the six are held apart in L\*.
 *   dichromacy  `running` and `success` are the two statuses with no weave
 *               (`--weave-*: none`), so hue is their *only* channel. They are
 *               therefore held to a separation that has to hold under both
 *               red-green projections, not just under normal vision.
 */
import { describe, expect, it } from "vitest"

import {
  contrast,
  deltaEok,
  deuteranopia,
  greyscale,
  lightness,
  onInvariantPlane,
  parseHex,
  protanopia,
} from "./color"
import type { Palette, Theme } from "./themes"
import { DEFAULT_THEME_ID, PALETTE_KEYS, STATUS_KEYS, THEMES } from "./themes"

const MODES = ["dark", "light"] as const

/** Every theme in every mode, as a flat list of cases. */
const CASES: readonly (readonly [string, Palette, Theme])[] = THEMES.flatMap(
  (theme) =>
    MODES.map(
      (mode) => [`${theme.id} · ${mode}`, theme.palette[mode], theme] as const
    )
)

const DEFAULT_CASES = CASES.filter(
  ([, , theme]) => theme.id === DEFAULT_THEME_ID
)

/**
 * The floors apply to every theme a person can be given by default or by
 * choice on the current bar. The one `legacy` palette is measured instead of
 * being held — see "the incumbent is kept as a record" at the bottom.
 */
const HELD_CASES = CASES.filter(([, , theme]) => !theme.legacy)
const LEGACY_CASES = CASES.filter(([, , theme]) => theme.legacy)

function statusPairs(palette: Palette) {
  const pairs: { name: string; a: string; b: string }[] = []
  for (let i = 0; i < STATUS_KEYS.length; i += 1) {
    for (let j = i + 1; j < STATUS_KEYS.length; j += 1) {
      const first = STATUS_KEYS[i] as (typeof STATUS_KEYS)[number]
      const second = STATUS_KEYS[j] as (typeof STATUS_KEYS)[number]
      pairs.push({
        name: `${first}/${second}`,
        a: palette[first],
        b: palette[second],
      })
    }
  }
  return pairs
}

/* ------------------------------------------------------------------ *
 * The registry is complete and well formed.
 * ------------------------------------------------------------------ */

describe("every theme states the whole vocabulary", () => {
  it("ships more than one theme", () => {
    // A theme system with a single theme is a hook, not a system — and every
    // case below would pass vacuously against a list of one.
    expect(THEMES.length).toBeGreaterThan(1)
  })

  it("names the default among them", () => {
    expect(THEMES.map((theme) => theme.id)).toContain(DEFAULT_THEME_ID)
  })

  it("gives every theme a distinct id", () => {
    expect(new Set(THEMES.map((theme) => theme.id)).size).toBe(THEMES.length)
  })

  it.each(CASES.map(([name, palette]) => [name, palette] as const))(
    "%s declares every primitive as a six-digit hex",
    (_name, palette) => {
      const missing = PALETTE_KEYS.filter(
        (key) => !/^#[0-9a-f]{6}$/.test(palette[key])
      )
      expect(missing).toEqual([])
    }
  )

  it.each(THEMES.map((theme) => [theme.id, theme] as const))(
    "%s says what it is for",
    (_id, theme) => {
      expect(theme.name.length).toBeGreaterThan(0)
      expect(theme.note.length).toBeGreaterThan(20)
      // "default" is a slot, not a name. The default theme has to say what it
      // *is* the same way the other five do.
      expect(theme.id).not.toBe("default")
    }
  )
})

/* ------------------------------------------------------------------ *
 * Contrast, computed rather than trusted.
 * ------------------------------------------------------------------ */

describe("text clears 4.5:1 in every theme and both modes", () => {
  it.each(CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: body, secondary and tertiary text on the floor",
    (_name, palette) => {
      // `--text-faint` is included on purpose: tertiary text still carries
      // content here, so it is held to the text floor rather than to the
      // "decorative grey" level it started at. Reported as a table so a
      // failure says which of the three fell short, and by how much.
      const failing = (["text", "muted", "faint"] as const)
        .map((role) => ({
          role,
          ratio: Number(contrast(palette[role], palette.floor).toFixed(2)),
        }))
        .filter((entry) => entry.ratio < 4.5)
      expect(failing).toEqual([])
    }
  )

  it.each(CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: body text on every chrome surface, not just the floor",
    (_name, palette) => {
      for (const surface of [
        palette.rail,
        palette.lane,
        palette.laneAlt,
        palette.raised,
      ]) {
        expect(contrast(palette.text, surface)).toBeGreaterThanOrEqual(4.5)
      }
    }
  )

  it.each(CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: the accent's own label, and the destructive one",
    (_name, palette) => {
      expect(contrast(palette.accentFg, palette.accent)).toBeGreaterThanOrEqual(
        4.5
      )
      // `--danger-foreground` is `--primary-foreground`; the fill it sits on is
      // `--destructive`. That alias is only safe while this holds.
      expect(
        contrast(palette.accentFg, palette.destructive)
      ).toBeGreaterThanOrEqual(4.5)
    }
  )

  it.each(HELD_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: every status label on the lane it is read in",
    (_name, palette) => {
      // A status is a label inside a data row, so `--lane` is the surface it
      // actually meets — not the floor, which is behind the table rather than
      // under the text.
      for (const key of STATUS_KEYS) {
        expect({
          status: key,
          ratio: contrast(palette[key], palette.lane) >= 4.5,
        }).toEqual({ status: key, ratio: true })
      }
    }
  )
})

/* ------------------------------------------------------------------ *
 * Greyscale, and then the two red-green projections.
 * ------------------------------------------------------------------ */

describe("the six statuses stay six statuses without hue", () => {
  it.each(HELD_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: no two statuses land within 4 L* of each other",
    (_name, palette) => {
      // Greyscale separation *is* lightness separation. Four is the floor the
      // imported directions clear; the default clears five, asserted below.
      const tight = statusPairs(palette)
        .map((pair) => ({
          pair: pair.name,
          dL: Number(
            Math.abs(lightness(pair.a) - lightness(pair.b)).toFixed(2)
          ),
        }))
        .filter((entry) => entry.dL < 4)
      expect(tight).toEqual([])
    }
  )

  it.each(HELD_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: running and success hold apart under both projections",
    (_name, palette) => {
      // These two are the only statuses whose weave is `none`, so colour is
      // their whole encoding. Everything else has a hatch to fall back on.
      const channels = {
        normal: deltaEok(palette.running, palette.success),
        protan: deltaEok(
          protanopia(palette.running),
          protanopia(palette.success)
        ),
        deutan: deltaEok(
          deuteranopia(palette.running),
          deuteranopia(palette.success)
        ),
      }
      const failing = Object.entries(channels).filter(
        ([, distance]) => distance < 0.09
      )
      expect(failing).toEqual([])
      expect(
        Math.abs(lightness(palette.running) - lightness(palette.success))
      ).toBeGreaterThanOrEqual(4)
    }
  )
})

/* ------------------------------------------------------------------ *
 * The default theme, held to the standard it was designed to.
 * ------------------------------------------------------------------ */

describe("the default theme is built inside the dichromat's gamut", () => {
  it.each(DEFAULT_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: every colour is a fixed point of both projections",
    (_name, palette) => {
      // The Viénot–Brettel–Mollon projections both map onto the plane where
      // red equals green. A colour already on that plane comes back unchanged —
      // which is the entire claim this theme makes: a protanope and a
      // trichromat are looking at the same screen, not at a compensated one.
      const moved = PALETTE_KEYS.map((key) => ({
        key,
        value: palette[key],
        protan: protanopia(palette[key]),
        deutan: deuteranopia(palette[key]),
      })).filter(
        (entry) => entry.protan !== entry.value || entry.deutan !== entry.value
      )
      expect(moved).toEqual([])
    }
  )

  it.each(DEFAULT_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: every colour sits on the red = green plane by construction",
    (_name, palette) => {
      // The same fact stated the cheap way, so a future edit that breaks it
      // fails on the hex rather than on a floating-point projection.
      const offPlane = PALETTE_KEYS.map((key) => ({
        key,
        value: palette[key],
      })).filter((entry) => {
        const [r, g] = parseHex(entry.value)
        return r !== g
      })
      expect(offPlane).toEqual([])
    }
  )

  it.each(DEFAULT_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: the status ladder keeps 5 L* per rung",
    (_name, palette) => {
      const tight = statusPairs(palette)
        .map((pair) => ({
          pair: pair.name,
          dL: Number(
            Math.abs(lightness(pair.a) - lightness(pair.b)).toFixed(2)
          ),
        }))
        .filter((entry) => entry.dL < 5)
      expect(tight).toEqual([])
    }
  )

  it.each(DEFAULT_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: no pair of statuses is closer than 0.07 ΔEok, in any vision",
    (_name, palette) => {
      const tight = statusPairs(palette)
        .map((pair) => ({
          pair: pair.name,
          worst: Number(
            Math.min(
              deltaEok(pair.a, pair.b),
              deltaEok(protanopia(pair.a), protanopia(pair.b)),
              deltaEok(deuteranopia(pair.a), deuteranopia(pair.b))
            ).toFixed(4)
          ),
        }))
        .filter((entry) => entry.worst < 0.07)
      expect(tight).toEqual([])
    }
  )

  it.each(DEFAULT_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: destructive is its own value, not the projected olive",
    (_name, palette) => {
      // It shares `failed`'s rung deliberately — the same alarm in its
      // imperative mood — but it is not the same token and it is not allowed
      // to quietly become one of the other five.
      const collisions = STATUS_KEYS.filter(
        (key) => key !== "failed" && palette[key] === palette.destructive
      )
      expect(collisions).toEqual([])
      expect(contrast(palette.accentFg, palette.destructive)).toBeGreaterThan(
        4.5
      )
    }
  )
})

/* ------------------------------------------------------------------ *
 * The default ladder, rebuilt from the numbers it was designed to.
 * ------------------------------------------------------------------ */

/* Each status as it was specified: a rung on the lightness ladder, and how far
   along the surviving blue↔yellow axis it sits (negative is warm, positive is
   cool, zero is neutral grey). Rebuilding the hexes from these is what makes
   the design reproducible rather than a set of numbers somebody once typed —
   and it is the only record of *why* `#b4b442` is that and not something near
   it.

   Consecutive rungs alternate side, with one deliberate exception: `queued`
   is the dullest of the six by design ("admitted but not started", the least
   saturated status in DESIGN.md), so it sits at a near-zero offset and lands
   on the same side as its neighbour. It is told apart by chroma instead —
   a near-neutral grey beside a full amber. Every other adjacent pair differs
   in side outright.

   `running` and `success` are the pair with no weave; they sit at opposite
   ends of the ladder and on opposite sides of nothing — both are cool, but 26
   L\* apart, which is more separation than any other pair gets. */
const LADDER = {
  dark: {
    running: [60.5, 108],
    queued: [65.8, -24],
    waiting: [71.1, -114],
    escalated: [76.4, 70],
    failed: [81.7, -170],
    success: [87, 40],
    destructive: [81.7, -212],
  },
  light: {
    running: [43, 132],
    queued: [37.4, -16],
    waiting: [31.8, -76],
    escalated: [26.2, 140],
    failed: [20.6, -34],
    success: [15, 112],
    destructive: [20.6, -52],
  },
} as const

describe("the default ladder is reproducible from its design", () => {
  it.each(["dark", "light"] as const)(
    "%s: every status is where the ladder says it is",
    (mode) => {
      const palette = THEMES.find((theme) => theme.id === DEFAULT_THEME_ID)
        ?.palette[mode]
      const rebuilt = Object.fromEntries(
        Object.entries(LADDER[mode]).map(([key, [rung, offset]]) => [
          key,
          onInvariantPlane(rung, offset),
        ])
      )
      const shipped = Object.fromEntries(
        Object.keys(LADDER[mode]).map((key) => [
          key,
          palette?.[key as keyof typeof palette],
        ])
      )
      expect(shipped).toEqual(rebuilt)
    }
  )

  it.each(["dark", "light"] as const)(
    "%s: neighbouring rungs never share both a side and a chroma",
    (mode) => {
      // Two statuses one rung apart are the closest thing this palette has to
      // a collision. They are allowed to share a side of the axis only when
      // one of them is the near-neutral `queued` — and then the chroma gap has
      // to do the work instead.
      const rungs = STATUS_KEYS.map((key) => LADDER[mode][key]).sort(
        (a, b) => a[0] - b[0]
      )
      const collisions = rungs
        .map((rung, index) => ({ rung, previous: rungs[index - 1] }))
        .filter((entry) => entry.previous !== undefined)
        .filter(
          ({ rung, previous }) =>
            Math.sign(rung[1]) === Math.sign(previous?.[1] ?? 0) &&
            Math.abs(Math.abs(rung[1]) - Math.abs(previous?.[1] ?? 0)) < 60
        )
      expect(collisions).toEqual([])
    }
  )

  it.each(CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: the six statuses are still six after hue is thrown away",
    (_name, palette) => {
      // Greyscale is the harshest of the four vision channels — it keeps only
      // lightness — so a palette that survives it survives the other three.
      const greys = STATUS_KEYS.map((key) => greyscale(palette[key]))
      expect(new Set(greys).size).toBe(STATUS_KEYS.length)
    }
  )
})

/* ------------------------------------------------------------------ *
 * The incumbent is kept as a record, and the record is asserted.
 * ------------------------------------------------------------------ */

describe("the legacy palette is exempted from the floors, not excused", () => {
  it("allows exactly one legacy theme", () => {
    // An exemption that can spread is not an exemption, it is a second bar.
    expect(LEGACY_CASES).toHaveLength(MODES.length)
  })

  it("is not the theme anybody gets without asking", () => {
    expect(LEGACY_CASES.some(([, , theme]) => theme.id === DEFAULT_THEME_ID))
      .toBe(false)
  })

  it.each(LEGACY_CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: still clears 4.5:1 for text on the floor",
    (_name, palette) => {
      // The chrome is held even here. What the incumbent fails is its status
      // set, and that is the whole of what it fails.
      for (const role of ["text", "muted", "faint"] as const) {
        expect({ role, ok: contrast(palette[role], palette.floor) >= 4.5 })
          .toEqual({ role, ok: true })
      }
    }
  )

  it("records which status labels are unreadable on the lane", () => {
    // Four of six, and `running` at 2.6:1 is the one that matters — this was
    // the shipped board. Repairing a value here fails this test, which is the
    // point: the exemption preserves a measurement, not a licence.
    const dark = LEGACY_CASES.find(([name]) => name.endsWith("dark"))![1]
    const failing = STATUS_KEYS.filter(
      (key) => contrast(dark[key], dark.lane) < 4.5
    )

    expect(failing.sort()).toEqual(
      ["escalated", "failed", "running", "success"].sort()
    )
  })

  it("records the two statuses that are one grey", () => {
    // 0.7 L* apart. In greyscale, on a photocopy, or to a dichromat, `failed`
    // and `escalated` were the same status — which is the question the default
    // theme was built to answer, and the reason this palette stays visible
    // beside it rather than being deleted.
    const dark = LEGACY_CASES.find(([name]) => name.endsWith("dark"))![1]
    const gap = Math.abs(lightness(dark.failed) - lightness(dark.escalated))

    expect(gap).toBeLessThan(1)
  })
})

/* ------------------------------------------------------------------ *
 * The three text tiers are a ladder, and the rungs are even.
 * ------------------------------------------------------------------ */

describe("body, muted and faint read as three levels, not two", () => {
  it.each(CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: the two gaps are within 4 L* of each other",
    (_name, palette) => {
      // Every theme once shipped a huge step from text to muted and a hair from
      // muted to faint — 23 and 7 in the default's dark mode. Two tiers that
      // close are one tier, and the third level of hierarchy did nothing. Both
      // quiet tiers are derived now, so the rungs come out even by construction
      // and this catches a hand-edit that breaks the derivation.
      const upper = Math.abs(lightness(palette.text) - lightness(palette.muted))
      const lower = Math.abs(lightness(palette.muted) - lightness(palette.faint))

      expect({ even: Math.abs(upper - lower) <= 4, upper, lower }).toMatchObject({
        even: true,
      })
    }
  )

  it.each(CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: keeps at least 12 L* between neighbouring tiers",
    (_name, palette) => {
      // The floor under "distinguishable". Below this the ladder is decorative.
      for (const [a, b] of [
        ["text", "muted"],
        ["muted", "faint"],
      ] as const) {
        expect({
          pair: `${a}/${b}`,
          apart: Math.abs(lightness(palette[a]) - lightness(palette[b])) >= 12,
        }).toEqual({ pair: `${a}/${b}`, apart: true })
      }
    }
  )

  it.each(CASES.map(([name, palette]) => [name, palette] as const))(
    "%s: faint is the quietest step that is still legible",
    (_name, palette) => {
      // It sits just above the readable floor by design. Far above it and it is
      // a second muted; below it and it is decoration pretending to be text.
      const ratio = contrast(palette.faint, palette.floor)
      expect({ ratio: ratio >= 4.5 && ratio < 6 }).toEqual({ ratio: true })
    }
  )
})
