/**
 * The kit's select, in its own folder because it is a composite — a trigger, an
 * overlay and a list — the way the data table and the split pane are.
 *
 * It took the name `shared/ui/select` off the loose shadcn shim that used to
 * hold it, the same way the kit's tooltip and status badge took theirs. The two
 * showcase surfaces that still want the old one say `_legacy/select` out loud.
 */
export { Select, type SelectOption, type SelectProps } from "./select"
