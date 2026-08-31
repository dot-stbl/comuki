/* The theme system, as one door.
 *
 *   themes.ts       the registry — six palettes, each with a dark and a light
 *                   rendering, plus the note the picker shows
 *   theme-css.ts    the registry compiled to `../styles/themes.css`
 *   theme-picker.ts the control for the palette axis
 *   color.ts        the arithmetic the tests hold the palettes to
 *
 * The *mode* axis lives in `app/theme-provider.tsx` beside the palette axis,
 * and its control is `app/layout/theme-control.tsx`. Both controls belong in
 * the topbar's tail, palette first: the palette is chosen once and the mode is
 * flipped, so the one that changes less often reads first.
 */
export { ThemePicker } from "./theme-picker"
export {
  DEFAULT_THEME_ID,
  findTheme,
  isThemeId,
  PALETTE_KEYS,
  STATUS_KEYS,
  THEMES,
} from "./themes"
export type { Palette, Theme } from "./themes"
