/* Colour arithmetic, so the palettes can be *argued about* rather than
 * eyeballed.
 *
 * Nothing in this file is used at runtime by a screen. It exists because the
 * only honest way to hold a promise like "body text clears 4.5:1 in every theme
 * and both modes" is to compute it in a test, and because the default theme is
 * built inside a colour space that has to be measured to be believed.
 *
 * Three things are worth knowing before reading:
 *
 * 1. **L\*** is CIE lightness — perceptual, unlike the `L` in HSL. Two colours
 *    a browser renders as very different hues can share an L\*, and in
 *    greyscale (or on a projector, or in a photocopied screenshot) they then
 *    *are* the same colour. Every status ladder in this product is spaced in
 *    L\* for that reason.
 *
 * 2. **Protanopia and deuteranopia** are simulated with the Viénot–Brettel–
 *    Mollon matrices, applied in *linear* RGB. These are the same matrices the
 *    palette review page used, so the numbers quoted there and the numbers a
 *    test prints here are the same numbers.
 *
 * 3. The image of both projections is exactly the plane `linear R = linear G`.
 *    A colour already on that plane is therefore a *fixed point*: simulating
 *    either kind of red-green blindness returns it unchanged. That is not a
 *    convenience, it is the whole basis of the default theme — see
 *    `themes.ts`.
 */

export type Rgb = readonly [number, number, number]

/** `#rrggbb` → three 0–255 channels. Accepts an optional leading `#`. */
export function parseHex(hex: string): Rgb {
  const body = hex.trim().replace(/^#/, "")
  if (!/^[0-9a-fA-F]{6}$/.test(body)) {
    throw new Error(`not a six-digit hex colour: ${hex}`)
  }
  return [
    Number.parseInt(body.slice(0, 2), 16),
    Number.parseInt(body.slice(2, 4), 16),
    Number.parseInt(body.slice(4, 6), 16),
  ]
}

/** Three 0–255 channels → `#rrggbb`, clamped and rounded. */
export function toHex(rgb: Rgb): string {
  const channel = (value: number) =>
    Math.max(0, Math.min(255, Math.round(value)))
      .toString(16)
      .padStart(2, "0")
  return `#${channel(rgb[0])}${channel(rgb[1])}${channel(rgb[2])}`
}

/** sRGB 0–255 → linear-light 0–1. */
export function toLinear(channel: number): number {
  const s = channel / 255
  return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
}

/** Linear-light 0–1 → sRGB 0–255. */
export function fromLinear(value: number): number {
  const s =
    value <= 0.0031308
      ? value * 12.92
      : 1.055 * Math.pow(value, 1 / 2.4) - 0.055
  return s * 255
}

export function linearRgb(hex: string): Rgb {
  const [r, g, b] = parseHex(hex)
  return [toLinear(r), toLinear(g), toLinear(b)]
}

/** WCAG relative luminance. */
export function luminance(hex: string): number {
  const [r, g, b] = linearRgb(hex)
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** WCAG 2.x contrast ratio, 1–21. Order of the arguments does not matter. */
export function contrast(a: string, b: string): number {
  const la = luminance(a)
  const lb = luminance(b)
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)
}

const SRGB_TO_XYZ = [
  [0.4124564, 0.3575761, 0.1804375],
  [0.2126729, 0.7151522, 0.072175],
  [0.0193339, 0.119192, 0.9503041],
] as const
const D65 = [0.95047, 1.0, 1.08883] as const

/** CIE L\*a\*b\* (D65). Only `L` is used by the ladders, but the pair comes free. */
export function lab(hex: string): Rgb {
  const [r, g, b] = linearRgb(hex)
  const xyz = SRGB_TO_XYZ.map((row) => row[0] * r + row[1] * g + row[2] * b)
  const f = (t: number) =>
    t > 216 / 24389 ? Math.cbrt(t) : ((24389 / 27) * t + 16) / 116
  const [fx, fy, fz] = xyz.map((value, index) => f(value / D65[index])) as [
    number,
    number,
    number,
  ]
  return [116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)]
}

/** CIE lightness, 0 (black) – 100 (white). */
export function lightness(hex: string): number {
  return lab(hex)[0]
}

/** OKLab, for a perceptual distance that behaves near the blue corner. */
export function oklab(hex: string): Rgb {
  const [r, g, b] = linearRgb(hex)
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)
  return [
    0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s,
  ]
}

/** Euclidean distance in OKLab — the "how different are these" number. */
export function deltaEok(a: string, b: string): number {
  const x = oklab(a)
  const y = oklab(b)
  return Math.hypot(x[0] - y[0], x[1] - y[1], x[2] - y[2])
}

/* Viénot–Brettel–Mollon, in linear RGB. Both matrices send every colour onto
   the plane where the first two rows agree — i.e. onto `linear R = linear G`. */
const PROTAN = [
  [0.11238, 0.88761, 0],
  [0.11238, 0.88762, 0],
  [0.00401, -0.00401, 1],
] as const
const DEUTAN = [
  [0.29275, 0.70725, 0],
  [0.29275, 0.70725, 0],
  [-0.02234, 0.02234, 1],
] as const

function project(hex: string, matrix: readonly (readonly number[])[]): string {
  const v = linearRgb(hex)
  const out = matrix.map((row) =>
    Math.max(
      0,
      Math.min(
        1,
        (row[0] ?? 0) * v[0] + (row[1] ?? 0) * v[1] + (row[2] ?? 0) * v[2]
      )
    )
  )
  return toHex([
    fromLinear(out[0] ?? 0),
    fromLinear(out[1] ?? 0),
    fromLinear(out[2] ?? 0),
  ])
}

/** The colour as a protanope sees it. */
export function protanopia(hex: string): string {
  return project(hex, PROTAN)
}

/** The colour as a deuteranope sees it. */
export function deuteranopia(hex: string): string {
  return project(hex, DEUTAN)
}

/** The colour with hue thrown away — a photocopy, a projector, a bad monitor. */
export function greyscale(hex: string): string {
  const y = fromLinear(luminance(hex))
  return toHex([y, y, y])
}

/** The three ways a reader can lose hue, plus the honest original. */
export const VISION = {
  normal: (hex: string) => hex,
  protanopia,
  deuteranopia,
  greyscale,
} as const

export type VisionChannel = keyof typeof VISION

/**
 * The `#rrggbb` on the protan/deutan-invariant plane that lands closest to
 * `targetL`, with blue offset from the shared red=green byte by `blueOffset`.
 *
 * This is how every colour in the default theme was chosen: pick a rung on the
 * lightness ladder, pick how far along the surviving blue↔yellow axis it sits,
 * and let the arithmetic find the byte. Bisection rather than an inverse,
 * because the sRGB transfer function has a linear toe and inverting it in
 * closed form is more code than forty halvings.
 */
export function onInvariantPlane(targetL: number, blueOffset: number): string {
  const clamp = (v: number) => Math.max(0, Math.min(255, v))
  let low = 0
  let high = 255
  for (let i = 0; i < 40; i += 1) {
    const mid = (low + high) / 2
    const found = lightness(toHex([mid, mid, clamp(mid + blueOffset)]))
    if (found < targetL) {
      low = mid
    } else {
      high = mid
    }
  }
  const grey = Math.round((low + high) / 2)
  return toHex([grey, grey, clamp(grey + blueOffset)])
}
