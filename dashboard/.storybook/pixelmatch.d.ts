// .storybook/pixelmatch.d.ts
//
// `pixelmatch` is pinned to 5.3.0 (see test-runner.ts's import comment) —
// the last version published as CommonJS, and it never shipped its own
// types at that version. `@types/pixelmatch` is a deprecated stub pointing
// at 7.x's *bundled* types (ESM-only, not installed here), so it resolves
// to nothing useful. This is the same signature 5.3.0 through 7.x all share.

declare module "pixelmatch" {
  export interface PixelmatchOptions {
    readonly threshold?: number
    readonly includeAA?: boolean
    readonly alpha?: number
    readonly aaColor?: readonly [number, number, number]
    readonly diffColor?: readonly [number, number, number]
    readonly diffColorAlt?: readonly [number, number, number]
    readonly diffMask?: boolean
  }

  export default function pixelmatch(
    img1: Uint8Array | Uint8ClampedArray,
    img2: Uint8Array | Uint8ClampedArray,
    output: Uint8Array | Uint8ClampedArray | null,
    width: number,
    height: number,
    options?: PixelmatchOptions
  ): number
}
