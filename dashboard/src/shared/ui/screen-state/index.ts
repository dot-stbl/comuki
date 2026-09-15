/**
 * The states a screen is in when it has nothing to show.
 *
 * §17 names four — Empty, Loading, Error, Forbidden. Three of them are one
 * shape and live in `ScreenState`; `ForbiddenState` is the fourth, which is
 * that same shape with the sentence already written, because "which role would
 * work" is not a screen-specific answer. Loading is the one that is genuinely a
 * different shape and lives next door as `Skeleton`.
 *
 * A composite primitive, so the folder is the unit: domains import from
 * `@/shared/ui` and nothing reaches past this file.
 */
export { ForbiddenState, type ForbiddenStateProps } from "./forbidden-state"
export {
  ScreenState,
  StateText,
  type ScreenStateInset,
  type ScreenStateKind,
  type ScreenStateProps,
  type StateTextProps,
} from "./screen-state"
