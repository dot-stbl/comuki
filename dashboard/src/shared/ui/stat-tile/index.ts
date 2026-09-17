/**
 * The tile a screen states one reading on — a composite primitive, so its own
 * folder.
 *
 * Three exports because three screens want three different amounts of it: the
 * whole tile, the figure alone (a card that already has a header to put a
 * reading in), and the label voice alone (anything that names a value and is
 * not a form control). Internals are private the way the form kit's are:
 * domains import from `@/shared/ui` and nothing reaches past this file.
 */
export {
  StatFigure,
  StatLabel,
  StatTile,
  type StatFigureProps,
  type StatLabelProps,
  type StatTileProps,
} from "./stat-tile"
