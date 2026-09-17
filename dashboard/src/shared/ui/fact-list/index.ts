/**
 * Named values about one record — a composite primitive, so its own folder.
 *
 * `FactList` is the `<dl>` and `Fact` is the `<dt>`/`<dd>` pair; neither is any
 * use without the other, which is what makes them one primitive rather than
 * two. Internals are private the way the form kit's are: domains import from
 * `@/shared/ui` and nothing reaches past this file.
 */
export {
  Fact,
  FactList,
  type FactListLayout,
  type FactListProps,
  type FactListSize,
  type FactProps,
} from "./fact-list"
