/**
 * The slug is the one field on this form that is not prose.
 *
 * It is the handle that appears as a column in the runs list, the queue, the
 * cost report and every role scope — so it is a *value*, and it has to be
 * validated as one before it is written rather than sanitised quietly
 * afterwards. A slug the operator did not choose is a slug they will not
 * recognise in the column where it lands.
 *
 * Every message is a sentence about the one thing that is wrong, in the order
 * a person types them into trouble: nothing typed, then capitals, then spaces,
 * then anything else that is not a letter, a digit or a hyphen.
 */

/** Long enough for `payments-platform`, short enough to stay a column. */
export const SLUG_MAX = 40
const SLUG_MIN = 2

/** Lowercase words, joined by single hyphens, starting and ending on a word. */
const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

/**
 * The slug a name suggests.
 *
 * A proposal, never a correction: the form fills the field with this while the
 * operator has not touched it and stops the moment they do. Silently rewriting
 * what somebody typed into the field is how a handle ends up being something
 * nobody chose.
 */
export function slugify(name: string): string {
  return name
    .toLowerCase()
    .normalize("NFKD")
    // Strip the accents `NFKD` just separated, so `Inés` proposes `ines`
    // rather than `in-s`.
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, SLUG_MAX)
    .replace(/-+$/g, "")
}

/**
 * What is wrong with this slug, or `null` when nothing is.
 *
 * `taken` is the slugs that already exist. Uniqueness is checked here rather
 * than at submit because it is the same kind of fact as the pattern — a reason
 * this value cannot be the handle — and splitting the two would put half the
 * answer under the field and half in a toast.
 */
export function validateSlug(
  value: string,
  taken: readonly string[] = []
): string | null {
  const slug = value.trim()

  if (slug.length === 0) {
    return "a slug is required"
  }
  if (/\s/.test(value)) {
    return "no spaces — use a hyphen"
  }
  if (/[A-Z]/.test(slug)) {
    return "slugs are lowercase"
  }
  if (slug.length < SLUG_MIN) {
    return `at least ${SLUG_MIN} characters`
  }
  if (slug.length > SLUG_MAX) {
    return `at most ${SLUG_MAX} characters`
  }
  if (/^[-]|[-]$/.test(slug)) {
    return "must start and end with a letter or digit"
  }
  if (!SLUG_PATTERN.test(slug)) {
    return "letters, digits and single hyphens only"
  }
  if (taken.includes(slug)) {
    return "that slug is taken"
  }
  return null
}
