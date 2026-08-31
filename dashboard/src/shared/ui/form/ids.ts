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
