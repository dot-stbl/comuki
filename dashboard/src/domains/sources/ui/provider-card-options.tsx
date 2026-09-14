import { SquareKanban } from "lucide-react"

import type { Provider } from "@/domains/sources/model/providers"
import { BrandIcon, type ProviderCardOption } from "@/shared/ui"

import styles from "./provider-card-options.module.css"

/**
 * The registry read out as picker cards.
 *
 * Both places that ask "which provider" — the task intake's source question
 * and the connect form's first decision — ask it of the same array, so the
 * reading of one registry row into one card lives once: the mark it is drawn
 * as (a `BrandIcon` where the kit has an honest one, a lucide board glyph
 * where drawing the vendor's would be inventing a trademark), the word only
 * where the mark does not already say it, and the one line that tells this
 * card apart from the one above it.
 *
 * A sources-domain helper rather than a kit one for the reason the registry
 * itself is: the kit knows the *shape* of a card, and the product's provider
 * table is the product's fact. The tasks domain reaches for it the same way
 * it reaches for the registry itself.
 */
export function providerCardOptions(
  providers: readonly Provider[]
): ProviderCardOption[] {
  return providers.map((provider) => ({
    value: provider.key,
    label: provider.label,
    mark:
      provider.brand === null ? (
        /* Sized by the class to sit exactly where the kit's own scale puts a
           brand mark, so the spelled card and the drawn ones keep one
           rhythm. */
        <SquareKanban className={styles.glyph} aria-hidden="true" />
      ) : (
        <BrandIcon brand={provider.brand} size="lg" label={null} />
      ),
    /* A mark that already says the provider's name — the drained
       github/gitlab/jira glyphs, whose brand id *is* this provider — does
       not need the name spelled beside it; a mark that stands in for
       something else (the board glyph for yandex tracker, the product's
       own container for native) keeps its visible name. The name never
       leaves the radio's `aria-label`, so the group reads the same either
       way. */
    name: provider.brand === provider.key ? undefined : provider.label,
    note: provider.intakeNote,
  }))
}
