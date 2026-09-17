/**
 * The id a field's hint or error takes, so its control can point
 * `aria-describedby` at whichever one is showing.
 *
 * A plain function in its own module because the components beside it are
 * components and nothing else — fast refresh stops working for a file that
 * exports both.
 */
export function fieldDescriptionId(id: string): string {
  return `${id}-description`
}

/**
 * The id a field's label takes, so a control that cannot be named by a plain
 * `<label for>` can point `aria-labelledby` at it instead.
 *
 * The kit's `Select` is that control: React Aria composes the trigger's name
 * from its own value node, which *replaces* any label association, so the name
 * has to be handed in rather than inferred.
 */
export function fieldLabelId(id: string): string {
  return `${id}-label`
}

/**
 * The `aria-describedby` a control ends up wearing: the caller's, plus the
 * field's own hint-or-error line.
 *
 * This exists because a field that simply wrote its own value clobbered the
 * caller's. The controls spread `{...rest}` first and then set their own
 * `aria-*`, so an `aria-describedby` handed in from outside — the login screen
 * pointing both boxes at the failure band above them — was overwritten with
 * the field's own id, or with `undefined` when the field had no hint. Swapping
 * the spread order only reverses who loses: then the field's own error line
 * goes unannounced the moment a call site passes anything. `aria-describedby`
 * is a *list*, so the fix is to join it rather than to pick a winner.
 *
 * Not `cn()`: that is `twMerge(clsx(...))`, a function that parses its inputs
 * as Tailwind class names and drops the ones it judges to be superseded. Ids
 * are not class names and must never be run through a merger that is allowed
 * to delete them.
 *
 * Returns `undefined` rather than `""` when there is nothing to point at — an
 * empty `aria-describedby` is an attribute pointing at nothing, which some
 * screen readers announce as a missing description rather than as no
 * description.
 */
export function describedBy(
  ...ids: (string | undefined | null | false)[]
): string | undefined {
  const present = ids.filter((id): id is string => Boolean(id))
  return present.length > 0 ? present.join(" ") : undefined
}
